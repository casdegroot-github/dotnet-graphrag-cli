using System.CommandLine;
using Albatross.CommandLine;
using GraphRagCli.Shared.Ai;
using GraphRagCli.Shared.GraphDb;

namespace GraphRagCli.Features.Search;

public class SearchCommandHandler(
    Neo4jSessionFactory sessionFactory,
    KernelFactory kernelFactory,
    ParseResult result,
    SearchParams parameters) : BaseHandler<SearchParams>(result, parameters)
{
    public override async Task<int> InvokeAsync(CancellationToken ct)
    {
        try
        {
            await using var driver = await sessionFactory.CreateDriverAsync(parameters.Database);
            var repo = new Neo4jSearchRepository(driver);

            var kernel = kernelFactory.Create();
            var embedder = kernelFactory.GetTextEmbedder(kernel);
            var service = new SearchService(repo, embedder);

            var results = await service.SearchAsync(
                parameters.Query, parameters.Mode, parameters.Top, parameters.Type);

            PrintResults(results, parameters.Query, parameters.Mode);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    static void PrintResults(List<SearchResult> results, string query, SearchMode mode)
    {
        Console.WriteLine($"Searching for: \"{query}\" [{mode}]");

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