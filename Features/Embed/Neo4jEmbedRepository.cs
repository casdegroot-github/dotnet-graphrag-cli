using GraphRagCli.Shared;
using Neo4j.Driver;
using Neo4j.Driver.Mapping;

namespace GraphRagCli.Features.Embed;

public class Neo4jEmbedRepository(IDriver driver)
{
    public async Task<List<EmbeddableNode>> GetNodesWithSummariesAsync(bool force)
    {
        var nodes = await driver
            .ExecutableQuery($"""
                MATCH (n:{NodeLabels.Embeddable})
                WHERE n.summary IS NOT NULL AND n.summary <> ''
                  AND (n.needsEmbedding = true OR $force)
                RETURN elementId(n) AS ElementId,
                       n.fullName AS FullName,
                       n.summary AS Summary,
                       n.searchText AS SearchText,
                       n.tags AS Tags
                """)
            .WithParameters(new { force })
            .ExecuteAsync()
            .AsObjectsAsync<EmbeddableNode>();

        return nodes.ToList();
    }

    public async Task SetEmbeddingsBatchAsync(List<(string ElementId, float[] Embedding)> batch)
    {
        foreach (var chunk in batch.Chunk(50))
        {
            await driver
                .ExecutableQuery($"""
                    UNWIND $batch AS item
                    MATCH (n) WHERE elementId(n) = item.elementId
                    SET n:{NodeLabels.Embeddable},
                        n.embedding = item.embedding,
                        n.embeddingHash = n.bodyHash,
                        n.needsEmbedding = false
                    """)
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

    public async Task<GraphMeta?> GetGraphMetaAsync()
    {
        var (records, _, _) = await driver
            .ExecutableQuery("""
                OPTIONAL MATCH (m:_GraphMeta)
                RETURN m.embeddingModel AS EmbeddingModel,
                       m.embeddingDimensions AS EmbeddingDimensions
                """)
            .ExecuteAsync();

        var record = records.SingleOrDefault();
        var model = record?["EmbeddingModel"].As<string?>();
        if (model is null)
            return null;

        return new GraphMeta(model, record!["EmbeddingDimensions"].As<int?>());
    }

    public async Task SetGraphMetaAsync(string model, int dimensions)
    {
        await driver
            .ExecutableQuery("""
                MERGE (m:_GraphMeta)
                SET m.embeddingModel = $model,
                    m.embeddingDimensions = $dimensions
                """)
            .WithParameters(new { model, dimensions })
            .ExecuteAsync();
    }

    public async Task EnsureVectorIndexAsync(int dimensions)
    {
        var (indexRecords, _, _) = await driver
            .ExecutableQuery("SHOW INDEXES WHERE name = 'code_embeddings'")
            .ExecuteAsync();

        var existing = indexRecords.FirstOrDefault();
        if (existing is not null)
        {
            var meta = await GetGraphMetaAsync();
            if (meta?.EmbeddingDimensions == dimensions)
                return;

            Console.WriteLine($"Embedding dimensions changed ({meta?.EmbeddingDimensions} → {dimensions}). Dropping index and clearing embeddings...");
            await driver.ExecutableQuery("DROP INDEX code_embeddings IF EXISTS").ExecuteAsync();
            await driver.ExecutableQuery($"MATCH (n:{NodeLabels.Embeddable}) REMOVE n.embedding, n.embeddingHash SET n.needsEmbedding = true").ExecuteAsync();
        }

        await driver.ExecutableQuery(
            $"CREATE VECTOR INDEX code_embeddings IF NOT EXISTS " +
            $"FOR (n:{NodeLabels.Embeddable}) ON (n.embedding) " +
            $"OPTIONS {{indexConfig: {{`vector.dimensions`: {dimensions}, `vector.similarity_function`: 'cosine'}}}}").ExecuteAsync();
    }

    public async Task ComputeCentralityAsync()
    {
        try
        {
            await driver.ExecutableQuery("CALL gds.graph.drop('code-graph', false)").ExecuteAsync();
        }
        catch { }

        await driver.ExecutableQuery($"""
            CALL gds.graph.project(
                'code-graph',
                '{NodeLabels.Embeddable}',
                '*'
            )
            """).ExecuteAsync();

        await driver.ExecutableQuery("""
            CALL gds.pageRank.write('code-graph', {writeProperty: 'pageRank'})
            """).ExecuteAsync();

        await driver.ExecutableQuery("""
            CALL gds.degree.write('code-graph', {writeProperty: 'inDegree', orientation: 'REVERSE'})
            """).ExecuteAsync();

        await driver.ExecutableQuery("CALL gds.graph.drop('code-graph')").ExecuteAsync();
    }
}
