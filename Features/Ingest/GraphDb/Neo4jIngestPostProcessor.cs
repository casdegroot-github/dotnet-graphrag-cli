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
            .ExecutableQuery(@"
                MATCH (n)
                WHERE n.lastIngestedAt = $runTimestamp
                  AND n.embeddingHash IS NOT NULL
                  AND n.bodyHash <> n.embeddingHash
                WITH collect(n) AS changed
                UNWIND changed AS c
                MATCH (c)<-[:CALLS|DEFINES|IMPLEMENTS|REFERENCES|EXTENDS|INHERITS_FROM*1..2]-(dep:Embeddable)
                WHERE dep.bodyHash = dep.embeddingHash
                SET dep.stale = true")
            .WithParameters(new { runTimestamp })
            .ExecuteAsync();
    }

    // --- Labeling ---

    public async Task LabelEmbeddableNodesAsync()
    {
        await driver
            .ExecutableQuery("MATCH (n) WHERE n:Class OR n:Interface OR n:Method OR n:Enum SET n:Embeddable")
            .ExecuteAsync();
    }

    public async Task<EntryPointResult> LabelEntryPointsAsync()
    {
        await driver
            .ExecutableQuery(@"
                MATCH (m:Method)
                WHERE m.isExtensionMethod = true
                  AND (m.extendedType CONTAINS 'IServiceCollection'
                    OR m.extendedType CONTAINS 'IHostBuilder'
                    OR m.extendedType CONTAINS 'IApplicationBuilder'
                    OR m.extendedType CONTAINS 'IEndpointRouteBuilder')
                SET m:EntryPoint")
            .ExecuteAsync();

        var (linkRecords, _, _) = await driver
            .ExecutableQuery(@"
                MATCH (iMethod:Method)<-[:DEFINES]-(iface:Interface)<-[:IMPLEMENTS]-(cls:Class)-[:DEFINES]->(cMethod:Method)
                WHERE iMethod.name = cMethod.name
                  AND size(split(iMethod.parameters, ',')) = size(split(cMethod.parameters, ','))
                MERGE (cMethod)-[:IMPLEMENTS_METHOD]->(iMethod)
                RETURN count(*) AS count")
            .ExecuteAsync();

        var linkedImplementations = linkRecords.Single()["count"].As<long>();

        var (epRecords, _, _) = await driver
            .ExecutableQuery("MATCH (e:EntryPoint) RETURN count(e) AS count")
            .ExecuteAsync();

        var entryPoints = epRecords.Single()["count"].As<long>();

        return new EntryPointResult(linkedImplementations, entryPoints);
    }

    public async Task<PublicApiResult> LabelPublicApiAsync(List<string>? nugetProjects)
    {
        var typeCounts = new Dictionary<string, long>();
        foreach (var (label, display) in new[] { ("Interface", "interfaces"), ("Class", "classes"), ("Enum", "enums") })
        {
            string query;
            object parameters;

            if (nugetProjects != null)
            {
                query = "MATCH (node:" + label + ")-[:BELONGS_TO_NAMESPACE]->(ns:Namespace)-[:BELONGS_TO_PROJECT]->(p:Project) " +
                        "WHERE node.visibility = 'public' AND p.fullName IN $projects " +
                        "SET node:PublicApi RETURN count(DISTINCT node) AS count";
                parameters = new { projects = nugetProjects };
            }
            else
            {
                query = "MATCH (node:" + label + ") " +
                        "WHERE node.visibility = 'public' " +
                        "SET node:PublicApi RETURN count(node) AS count";
                parameters = new { };
            }

            var (records, _, _) = await driver
                .ExecutableQuery(query)
                .WithParameters(parameters)
                .ExecuteAsync();

            typeCounts[display] = records.Single()["count"].As<long>();
        }

        var (methodRecords, _, _) = await driver
            .ExecutableQuery(@"
                MATCH (parent:PublicApi)-[:DEFINES]->(m:Method)
                WHERE m.visibility = 'public'
                SET m:PublicApi
                RETURN count(m) AS count")
            .ExecuteAsync();

        var methodCount = methodRecords.Single()["count"].As<long>();

        var (totalRecords, _, _) = await driver
            .ExecutableQuery("MATCH (n:PublicApi) RETURN count(n) AS total")
            .ExecuteAsync();

        var total = totalRecords.Single()["total"].As<long>();

        return new PublicApiResult(typeCounts, methodCount, total);
    }
}