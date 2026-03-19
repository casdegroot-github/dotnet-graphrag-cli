using GraphRagCli.Shared;
using Neo4j.Driver;

namespace GraphRagCli.Features.Ingest.GraphDb;

public class Neo4jIngestPostProcessor(IDriver driver)
{
    // --- Sync methods ---

    public async Task<int> TransferByBodyHashAsync(DateTime runTimestamp)
    {
        var (records, _, _) = await driver
            .ExecutableQuery(@"
                MATCH (stale)
                WHERE stale.lastIngestedAt < $runTimestamp
                  AND stale.bodyHash IS NOT NULL
                  AND (stale.summary IS NOT NULL OR stale.embedding IS NOT NULL)
                WITH stale
                MATCH (fresh)
                WHERE fresh.lastIngestedAt = $runTimestamp
                  AND fresh.bodyHash = stale.bodyHash
                SET fresh.summary = stale.summary,
                    fresh.searchText = stale.searchText,
                    fresh.tags = stale.tags,
                    fresh.embedding = stale.embedding,
                    fresh.embeddingHash = CASE WHEN stale.embedding IS NOT NULL THEN fresh.bodyHash ELSE null END,
                    fresh.needsSummary = CASE WHEN stale.summary IS NOT NULL THEN false ELSE true END
                RETURN count(fresh) AS transferred")
            .WithParameters(new { runTimestamp })
            .ExecuteAsync();

        return records.Single()["transferred"].As<int>();
    }

    public async Task<int> DeleteStaleEdgesAsync(DateTime runTimestamp)
    {
        var (records, _, _) = await driver
            .ExecutableQuery(@"
                MATCH ()-[r]->()
                WHERE r.lastIngestedAt < $runTimestamp
                DELETE r
                RETURN count(r) AS deleted")
            .WithParameters(new { runTimestamp })
            .ExecuteAsync();

        return records.Single()["deleted"].As<int>();
    }

    public async Task<int> DeleteStaleNodesAsync(DateTime runTimestamp)
    {
        var (records, _, _) = await driver
            .ExecutableQuery(@"
                MATCH (n)
                WHERE n.lastIngestedAt < $runTimestamp
                  AND NOT n:Solution
                DETACH DELETE n
                RETURN count(n) AS deleted")
            .WithParameters(new { runTimestamp })
            .ExecuteAsync();

        return records.Single()["deleted"].As<int>();
    }

    public async Task MarkStaleDependentsAsync(DateTime runTimestamp)
    {
        await driver
            .ExecutableQuery($"""
                MATCH (n)
                WHERE n.lastIngestedAt = $runTimestamp
                  AND n.embeddingHash IS NOT NULL
                  AND n.bodyHash <> n.embeddingHash
                WITH collect(n) AS changed
                UNWIND changed AS c
                MATCH (c)<-[:{RelType.CalledBy}|{RelType.DefinedBy}|{RelType.Implements}|{RelType.ReferencedBy}|{RelType.Extends}|{RelType.InheritsFrom}*1..2]-(dep:{NodeLabels.Embeddable})
                WHERE dep.bodyHash = dep.embeddingHash
                SET dep.stale = true
                """)
            .WithParameters(new { runTimestamp })
            .ExecuteAsync();
    }

    // --- Tier computation ---

    public async Task ComputeTiersAsync()
    {
        try { await driver.ExecutableQuery("CALL gds.graph.drop('tier-graph', false)").ExecuteAsync(); }
        catch { /* stale projection cleanup */ }
        

        // Clear existing tiers so excluded nodes don't keep stale values
        await driver.ExecutableQuery("MATCH (n) REMOVE n.tier").ExecuteAsync();

        try
        {
            // 1. Project all nodes and relationships (all edges are child→parent, so leaves = sources)
            await driver.ExecutableQuery(@"
                CALL gds.graph.project('tier-graph', '*', '*')").ExecuteAsync();

            // 2. SCC — find cycle groups, write componentId to nodes
            await driver.ExecutableQuery(@"
                CALL gds.scc.stream('tier-graph')
                YIELD nodeId, componentId
                WITH gds.util.asNode(nodeId) AS n, componentId
                SET n.sccId = componentId").ExecuteAsync();

            // 3. Topological sort — assigns tiers to non-cycle nodes
            await driver.ExecutableQuery(@"
                CALL gds.dag.topologicalSort.stream('tier-graph', {
                    computeMaxDistanceFromSource: true
                })
                YIELD nodeId, maxDistanceFromSource
                WITH gds.util.asNode(nodeId) AS n, toInteger(maxDistanceFromSource) AS tier
                SET n.tier = tier").ExecuteAsync();

            // 4. Iteratively assign tiers to nodes excluded from topologicalSort
            //    (cycle nodes and their dependents). Only assign when ALL non-SCC
            //    children have tiers, ensuring correct ordering.
            int assigned;
            do
            {
                var (records, _, _) = await driver.ExecutableQuery(@"
                    MATCH (n) WHERE n.tier IS NULL
                    OPTIONAL MATCH (untiered)-->(n)
                        WHERE untiered.tier IS NULL AND untiered.sccId <> n.sccId
                    WITH n, count(untiered) AS pending
                    WHERE pending = 0
                    OPTIONAL MATCH (peer) WHERE peer.sccId = n.sccId AND peer.tier IS NOT NULL
                    OPTIONAL MATCH (child)-->(n) WHERE child.tier IS NOT NULL
                    WITH n, COALESCE(max(peer.tier), max(child.tier) + 1, 0) AS tier
                    SET n.tier = tier
                    RETURN count(n) AS assigned").ExecuteAsync();
                assigned = records.Single()["assigned"].As<int>();
            } while (assigned > 0);

            // 5. Any remaining disconnected nodes get tier 0
            await driver.ExecutableQuery(@"
                MATCH (n) WHERE n.tier IS NULL SET n.tier = 0").ExecuteAsync();

            // 6. Clean up temporary property
            await driver.ExecutableQuery("MATCH (n) REMOVE n.sccId").ExecuteAsync();
        }
        finally
        {
            try { await driver.ExecutableQuery("CALL gds.graph.drop('tier-graph')").ExecuteAsync(); }
            catch { /* graph may not exist if projection failed */ }
        }
    }

    // --- Labeling ---

    public async Task LabelEmbeddableNodesAsync()
    {
        await driver
            .ExecutableQuery($"MATCH (n) WHERE n:{NodeType.Class} OR n:{NodeType.Interface} OR n:{NodeType.Method} OR n:{NodeType.Enum} SET n:{NodeLabels.Embeddable}")
            .ExecuteAsync();
    }

    public async Task<EntryPointResult> LabelEntryPointsAsync()
    {
        await driver
            .ExecutableQuery($"""
                MATCH (m:{NodeType.Method})
                WHERE m.isExtensionMethod = true
                  AND (m.extendedType CONTAINS 'IServiceCollection'
                    OR m.extendedType CONTAINS 'IHostBuilder'
                    OR m.extendedType CONTAINS 'IApplicationBuilder'
                    OR m.extendedType CONTAINS 'IEndpointRouteBuilder')
                SET m:{NodeLabels.EntryPoint}
                """)
            .ExecuteAsync();

        var (linkRecords, _, _) = await driver
            .ExecutableQuery($@"
                MATCH (iMethod:Method)-[:{RelType.DefinedBy}]->(iface:Interface)<-[:{RelType.Implements}]-(cls:Class)<-[:{RelType.DefinedBy}]-(cMethod:Method)
                WHERE iMethod.name = cMethod.name
                  AND size(split(iMethod.parameters, ',')) = size(split(cMethod.parameters, ','))
                MERGE (cMethod)-[:{RelType.ImplementsMethod}]->(iMethod)
                RETURN count(*) AS count")
            .ExecuteAsync();

        var linkedImplementations = linkRecords.Single()["count"].As<long>();

        var (epRecords, _, _) = await driver
            .ExecutableQuery($"MATCH (e:{NodeLabels.EntryPoint}) RETURN count(e) AS count")
            .ExecuteAsync();

        var entryPoints = epRecords.Single()["count"].As<long>();

        return new EntryPointResult(linkedImplementations, entryPoints);
    }

    public async Task<PublicApiResult> LabelPublicApiAsync(List<string>? nugetProjects)
    {
        var typeCounts = new Dictionary<string, long>();
        foreach (var (label, display) in new[] { (NodeType.Interface, "interfaces"), (NodeType.Class, "classes"), (NodeType.Enum, "enums") })
        {
            string query;
            object parameters;

            if (nugetProjects != null)
            {
                query = $"MATCH (node:{label})-[:{RelType.BelongsToNamespace}]->(ns:Namespace)-[:{RelType.BelongsToProject}]->(p:Project) " +
                        "WHERE node.visibility = 'public' AND p.fullName IN $projects " +
                        $"SET node:{NodeLabels.PublicApi} RETURN count(DISTINCT node) AS count";
                parameters = new { projects = nugetProjects };
            }
            else
            {
                query = "MATCH (node:" + label + ") " +
                        "WHERE node.visibility = 'public' " +
                        $"SET node:{NodeLabels.PublicApi} RETURN count(node) AS count";
                parameters = new { };
            }

            var (records, _, _) = await driver
                .ExecutableQuery(query)
                .WithParameters(parameters)
                .ExecuteAsync();

            typeCounts[display] = records.Single()["count"].As<long>();
        }

        var (methodRecords, _, _) = await driver
            .ExecutableQuery($"""
                MATCH (m:{NodeType.Method})-[:{RelType.DefinedBy}]->(parent:{NodeLabels.PublicApi})
                WHERE m.visibility = 'public'
                SET m:{NodeLabels.PublicApi}
                RETURN count(m) AS count
                """)
            .ExecuteAsync();

        var methodCount = methodRecords.Single()["count"].As<long>();

        var (totalRecords, _, _) = await driver
            .ExecutableQuery($"MATCH (n:{NodeLabels.PublicApi}) RETURN count(n) AS total")
            .ExecuteAsync();

        var total = totalRecords.Single()["total"].As<long>();

        return new PublicApiResult(typeCounts, methodCount, total);
    }
}