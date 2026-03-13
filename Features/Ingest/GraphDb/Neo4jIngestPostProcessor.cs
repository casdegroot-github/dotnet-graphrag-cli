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
                MATCH (c)<-[:CALLED_BY|DEFINED_BY|IMPLEMENTS|REFERENCES|EXTENDS|INHERITS_FROM*1..2]-(dep:{NodeLabels.Embeddable})
                WHERE dep.bodyHash = dep.embeddingHash
                SET dep.stale = true
                """)
            .WithParameters(new { runTimestamp })
            .ExecuteAsync();
    }

    // --- Tier computation ---

    public async Task ComputeTiersAsync()
    {
        await driver
            .ExecutableQuery(@"
                MATCH (n)
                OPTIONAL MATCH path = ()-[*1..20]->(n)
                WITH n, length(path) AS pathLength
                WITH n, COALESCE(max(pathLength), 0) AS tier
                SET n.tier = tier")
            .ExecuteAsync();
    }

    // --- Labeling ---

    public async Task LabelEmbeddableNodesAsync()
    {
        await driver
            .ExecutableQuery($"MATCH (n) WHERE n:{NodeLabels.Class} OR n:{NodeLabels.Interface} OR n:{NodeLabels.Method} OR n:{NodeLabels.Enum} SET n:{NodeLabels.Embeddable}")
            .ExecuteAsync();
    }

    public async Task<EntryPointResult> LabelEntryPointsAsync()
    {
        await driver
            .ExecutableQuery($"""
                MATCH (m:{NodeLabels.Method})
                WHERE m.isExtensionMethod = true
                  AND (m.extendedType CONTAINS 'IServiceCollection'
                    OR m.extendedType CONTAINS 'IHostBuilder'
                    OR m.extendedType CONTAINS 'IApplicationBuilder'
                    OR m.extendedType CONTAINS 'IEndpointRouteBuilder')
                SET m:{NodeLabels.EntryPoint}
                """)
            .ExecuteAsync();

        var (linkRecords, _, _) = await driver
            .ExecutableQuery(@"
                MATCH (iMethod:Method)-[:DEFINED_BY]->(iface:Interface)<-[:IMPLEMENTS]-(cls:Class)<-[:DEFINED_BY]-(cMethod:Method)
                WHERE iMethod.name = cMethod.name
                  AND size(split(iMethod.parameters, ',')) = size(split(cMethod.parameters, ','))
                MERGE (cMethod)-[:IMPLEMENTS_METHOD]->(iMethod)
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
        foreach (var (label, display) in new[] { (NodeLabels.Interface, "interfaces"), (NodeLabels.Class, "classes"), (NodeLabels.Enum, "enums") })
        {
            string query;
            object parameters;

            if (nugetProjects != null)
            {
                query = "MATCH (node:" + label + ")-[:BELONGS_TO_NAMESPACE]->(ns:Namespace)-[:BELONGS_TO_PROJECT]->(p:Project) " +
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
                MATCH (m:{NodeLabels.Method})-[:DEFINED_BY]->(parent:{NodeLabels.PublicApi})
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