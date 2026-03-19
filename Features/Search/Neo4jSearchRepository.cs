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
                       node.tier AS Tier,
                       labels(node) AS Labels,
                       node.parameters AS Parameters, node.returnType AS ReturnType,
                       node.searchText AS SearchText
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
        var escapedQuery = EscapeLuceneQuery(query);
        var (records, _, _) = await driver
            .ExecutableQuery(@"
                CALL db.index.fulltext.queryNodes('embeddable_fulltext', $query)
                YIELD node, score
                RETURN node.fullName AS FullName, node.name AS Name, node.summary AS Summary,
                       node.namespace AS Namespace, node.filePath AS FilePath, score AS Score,
                       [l IN labels(node) WHERE l IN ['Class','Interface','Method','Enum','Namespace','Project']][0] AS Type,
                       node.pageRank AS PageRank,
                       node.tier AS Tier,
                       labels(node) AS Labels,
                       node.parameters AS Parameters, node.returnType AS ReturnType,
                       node.searchText AS SearchText
                ORDER BY score DESC LIMIT $topK")
            .WithParameters(new { query = escapedQuery, topK })
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
                MATCH (n {fullName: fn})
                OPTIONAL MATCH (n)-[r]-(neighbor)
                RETURN fn AS sourceFullName,
                       collect(DISTINCT {
                           name: neighbor.name, 
                           summary: neighbor.summary, 
                           relationship: type(r), 
                           neighborFullName: neighbor.fullName,
                           method: r.method
                       }) AS neighbors
                """)
            .WithParameters(new { fullNames })
            .ExecuteAsync(ct);

        return records.ToDictionary(
            r => r["sourceFullName"].As<string>(),
            r => r["neighbors"].As<List<IDictionary<string, object>>>()
                .Where(n => n["name"] != null)
                .Select(n =>
                {
                    var relType = n["relationship"]?.ToString() ?? "RELATED";
                    var method = n["method"]?.ToString();
                    if (!string.IsNullOrEmpty(method))
                        relType = $"{relType}({method})";

                    return new NeighborInfo(
                        n["name"]!.ToString()!,
                        n["summary"]?.ToString(),
                        relType,
                        n["neighborFullName"]?.ToString());
                })
                .ToList());
    }

    private static string EscapeLuceneQuery(string query)
    {
        const string specialChars = @"+-&|!(){}[]^""~*?:\/";
        var escaped = new System.Text.StringBuilder();
        foreach (var c in query)
        {
            if (specialChars.Contains(c))
                escaped.Append('\\');
            escaped.Append(c);
        }
        return escaped.ToString();
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
        Tier: r["Tier"].As<int?>(),
        Labels: r["Labels"].As<List<string>?>(),
        Parameters: r["Parameters"].As<string?>(),
        ReturnType: r["ReturnType"].As<string?>(),
        SearchText: r["SearchText"].As<string?>());
}
