using System.CommandLine;
using System.CommandLine.Parsing;

namespace CodeGraphIndexer.Commands;

public static class SearchCommand
{
    static readonly Argument<string> s_queryArg = new("query") { Description = "Search query" };
    static readonly Option<int> s_top = new("--top") { Description = "Number of results", DefaultValueFactory = _ => 10 };
    static readonly Option<string?> s_type = new("--type") { Description = "Filter by type: Class, Interface, Method, Enum" };
    static readonly Option<SearchMode> s_mode = new("--mode") { Description = "Search mode: Hybrid (fulltext+vector) or Vector" };
    static readonly Option<bool> s_claude = new("--claude") { Description = "Use Claude embeddings/summaries instead of Ollama" };

    public static Command Build()
    {
        var command = new Command("search", "Search the code graph using semantic and graph-augmented queries");
        command.Add(s_queryArg);
        command.Add(s_top);
        command.Add(s_type);
        command.Add(s_mode);
        command.Add(s_claude);
        GlobalOptions.AddAllOptions(command);

        command.SetAction(async (parseResult, _) => await ExecuteAsync(parseResult));
        return command;
    }

    static async Task ExecuteAsync(ParseResult parseResult)
    {
        var query = parseResult.GetValue(s_queryArg)!;
        var topK = parseResult.GetValue(s_top);
        var typeFilter = parseResult.GetValue(s_type);
        var searchMode = parseResult.GetValue(s_mode);
        var useClaude = parseResult.GetValue(s_claude);
        var conn = GlobalOptions.Parse(parseResult);
        var fieldPrefix = useClaude ? "claude_" : "";

        try
        {
            await using var neo4j = await GlobalOptions.ConnectNeo4jAsync(conn);
            if (neo4j == null) return;

            var ollama = new OllamaService(conn.OllamaUrl);

            var (results, modeLabel) = await ExecuteSearch(neo4j, ollama, query, searchMode, topK, typeFilter, fieldPrefix);

            var providerLabel = useClaude ? "claude" : "ollama";
            PrintResults(results, query, modeLabel, providerLabel);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    static async Task<(List<Neo4jService.SearchResult> Results, string ModeLabel)> ExecuteSearch(
        Neo4jService neo4j, OllamaService ollama, string query,
        SearchMode searchMode, int topK, string? typeFilter, string fieldPrefix)
    {
        var vector = await ollama.EmbedQueryAsync(query);

        List<Neo4jService.SearchResult> candidates;
        string modeLabel;

        if (searchMode == SearchMode.Vector)
        {
            candidates = await neo4j.SemanticSearchAsync(vector, topK * 2, typeFilter, fieldPrefix);
            modeLabel = "vector";
        }
        else
        {
            var routedMode = Neo4jService.RouteQuery(query);
            var (ftWeight, vecWeight) = neo4j.GetWeightsForMode(routedMode);
            var routeLabel = routedMode switch
            {
                QueryMode.Name => "hybrid:name",
                QueryMode.Semantic => "hybrid:semantic",
                _ => "hybrid"
            };
            candidates = await neo4j.HybridSearchAsync(query, vector, topK * 2, ftWeight, vecWeight, fieldPrefix);
            modeLabel = $"{routeLabel} (ft={ftWeight:F1}, vec={vecWeight:F1})";
        }
        var results = await neo4j.GraphExpandAndRerankAsync(candidates, topK, fieldPrefix);
        return (results, modeLabel);
    }

    static void PrintResults(List<Neo4jService.SearchResult> results, string query, string modeLabel, string providerLabel)
    {
        Console.WriteLine($"Searching for: \"{query}\" [{modeLabel}, {providerLabel}]");

        if (results.Count == 0)
        {
            Console.WriteLine("No results found.");
            return;
        }

        Console.WriteLine();
        Console.WriteLine($"{"Score",-8} {"Type",-12} {"Name",-50} Summary");
        Console.WriteLine(new string('-', 120));

        foreach (var r in results)
        {
            var summary = r.Summary ?? "";
            if (summary.Length > 60) summary = summary[..57] + "...";
            Console.WriteLine($"{r.Score:F4}  {r.Type,-12} {r.FullName,-50} {summary}");

            if (r.Type == "Method" && r.ReturnType != null)
            {
                var sig = $"{r.ReturnType} {r.Name}({r.Parameters ?? ""})";
                Console.WriteLine($"         sig: {sig}");
            }

            if (r.Neighbors is { Count: > 0 })
            {
                foreach (var n in r.Neighbors.Take(3))
                {
                    var nSummary = n.Summary ?? "";
                    if (nSummary.Length > 50) nSummary = nSummary[..47] + "...";
                    Console.WriteLine($"           └─ {n.Relationship,-14} {n.Name,-40} {nSummary}");
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{results.Count} results returned.");
    }
}
