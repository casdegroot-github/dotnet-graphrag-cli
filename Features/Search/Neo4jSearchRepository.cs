using GraphRagCli.Shared;
using Neo4j.Driver;
using Neo4j.Driver.Mapping;

namespace GraphRagCli.Features.Search;

public class Neo4jSearchRepository(IDriver driver) : ISearchRepository
{
    public async Task<List<SearchResult>> SemanticSearchAsync(
        float[] queryVector, int topK, string? labelFilter, CancellationToken ct = default)
    {
        var (records, _, _) = await driver
            .ExecutableQuery(@"
                CALL db.index.vector.queryNodes('code_embeddings', $topK, $queryVector)
                YIELD node, score
                WHERE $labelFilter IS NULL OR $labelFilter IN labels(node)
                RETURN node.fullName AS FullName, node.name AS Name, node.summary AS Summary,
                       node.namespace AS Namespace, node.filePath AS FilePath, score AS Score,
                       [l IN labels(node) WHERE l IN ['Class','Interface','Method','Enum','Namespace','Project']][0] AS Type,
                       node.pageRank AS PageRank,
                       labels(node) AS Labels,
                       node.parameters AS Parameters, node.returnType AS ReturnType
                ORDER BY score DESC LIMIT $topK")
            .WithParameters(new
            {
                queryVector = queryVector.Select(f => (double)f).ToList(),
                topK,
                labelFilter
            })
            .ExecuteAsync(ct);

        return records.Select(MapSearchResult).ToList();
    }

    public async Task<List<SearchResult>> FulltextSearchAsync(
        string query, int topK, CancellationToken ct = default)
    {
        var (records, _, _) = await driver
            .ExecutableQuery(@"
                CALL db.index.fulltext.queryNodes('embeddable_fulltext', $query)
                YIELD node, score
                RETURN node.fullName AS FullName, node.name AS Name, node.summary AS Summary,
                       node.namespace AS Namespace, node.filePath AS FilePath, score AS Score,
                       [l IN labels(node) WHERE l IN ['Class','Interface','Method','Enum','Namespace','Project']][0] AS Type,
                       node.pageRank AS PageRank,
                       labels(node) AS Labels,
                       node.parameters AS Parameters, node.returnType AS ReturnType
                ORDER BY score DESC LIMIT $topK")
            .WithParameters(new { query, topK })
            .ExecuteAsync(ct);

        return records.Select(MapSearchResult).ToList();
    }

    public async Task<Dictionary<string, List<NeighborInfo>>> GetNeighborsAsync(
        List<string> fullNames, CancellationToken ct = default)
    {
        if (fullNames.Count == 0) return new();

        var (records, _, _) = await driver
            .ExecutableQuery($$"""
                UNWIND $fullNames AS fn
                MATCH (n:{{NodeLabels.Embeddable}} {fullName: fn})
                OPTIONAL MATCH (n)-[r]-(neighbor:{{NodeLabels.Embeddable}})
                RETURN fn AS sourceFullName,
                       collect(DISTINCT {name: neighbor.name, summary: neighbor.summary, relationship: type(r), neighborFullName: neighbor.fullName}) AS neighbors
                """)
            .WithParameters(new { fullNames })
            .ExecuteAsync(ct);

        return records.ToDictionary(
            r => r["sourceFullName"].As<string>(),
            r => r["neighbors"].As<List<IDictionary<string, object>>>()
                .Where(n => n["name"] != null)
                .Select(n => new NeighborInfo(
                    n["name"]!.ToString()!,
                    n["summary"]?.ToString(),
                    n["relationship"]?.ToString() ?? "RELATED",
                    n["neighborFullName"]?.ToString()))
                .ToList());
    }

    private static SearchResult MapSearchResult(IRecord r) => new(
        FullName: r["FullName"].As<string>(),
        Name: r["Name"].As<string>(),
        Summary: r["Summary"].As<string?>(),
        Namespace: r["Namespace"].As<string?>(),
        FilePath: r["FilePath"].As<string?>(),
        Score: r["Score"].As<double>(),
        Type: r["Type"].As<string?>(),
        PageRank: r["PageRank"].As<double?>(),
        Labels: r["Labels"].As<List<string>?>(),
        Parameters: r["Parameters"].As<string?>(),
        ReturnType: r["ReturnType"].As<string?>());
}
