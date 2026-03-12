using GraphRagCli.Shared.GraphDb;
using Neo4j.Driver;

namespace GraphRagCli.Features.Embed;

public class Neo4jEmbedRepository(IDriver driver)
{
    public async Task<List<EmbeddableNode>> GetNodesWithSummariesAsync(bool force)
    {
        var nodes = await driver
            .ExecutableQuery(@"
                MATCH (n:Embeddable)
                WHERE n.summary IS NOT NULL AND n.summary <> ''
                  AND (n.needsEmbedding = true OR $force)
                RETURN elementId(n) AS ElementId,
                       n.fullName AS FullName,
                       n.summary AS Summary,
                       n.searchText AS SearchText,
                       n.tags AS Tags")
            .WithParameters(new { force })
            .ExecuteAsync()
            .MapAsync<EmbeddableNode>();

        return nodes.ToList();
    }

    public async Task SetEmbeddingsBatchAsync(List<(string ElementId, float[] Embedding)> batch)
    {
        foreach (var chunk in batch.Chunk(50))
        {
            await driver
                .ExecutableQuery(@"
                    UNWIND $batch AS item
                    MATCH (n) WHERE elementId(n) = item.elementId
                    SET n:Embeddable,
                        n.embedding = item.embedding,
                        n.embeddingHash = n.bodyHash,
                        n.needsEmbedding = false")
                .WithParameters(new
                {
                    batch = chunk.Select(x => new
                    {
                        elementId = x.ElementId,
                        embedding = x.Embedding.Select(f => (double)f).ToList()
                    }).ToList()
                })
                .ExecuteAsync();
        }
    }

    public async Task ComputeCentralityAsync()
    {
        try
        {
            await driver.ExecutableQuery("CALL gds.graph.drop('code-graph', false)").ExecuteAsync();
        }
        catch { }

        await driver.ExecutableQuery(@"
            CALL gds.graph.project(
                'code-graph',
                'Embeddable',
                '*'
            )").ExecuteAsync();

        await driver.ExecutableQuery(@"
            CALL gds.pageRank.write('code-graph', {writeProperty: 'pageRank'})").ExecuteAsync();

        await driver.ExecutableQuery(@"
            CALL gds.degree.write('code-graph', {writeProperty: 'inDegree', orientation: 'REVERSE'})").ExecuteAsync();

        await driver.ExecutableQuery("CALL gds.graph.drop('code-graph')").ExecuteAsync();
    }
}