using System.Text.RegularExpressions;
using GraphRagCli.Features.Summarize.Prompts;
using GraphRagCli.Features.Summarize.Summarizers;
using GraphRagCli.Shared.Ai;
using GraphRagCli.Shared.GraphDb;

namespace GraphRagCli.Features.Summarize;

public partial class SummarizeService(
    Neo4jSessionFactory sessionFactory,
    KernelFactory kernelFactory,
    ModelsConfig modelsConfig,
    IPromptBuilder promptBuilder)
{
    public async Task<SummarizeResult> RunAsync(SummarizeParams parameters, CancellationToken ct = default)
    {
        var resolvedModel = modelsConfig.ResolveSummarizeModelName(parameters.Model);
        var config = modelsConfig.GetSummarizeModel(parameters.Model);

        await using var driver = await sessionFactory.CreateDriverAsync(parameters.Database);
        var repo = new Neo4jSummarizeRepository(driver);

        if (parameters.ListTiers)
        {
            await PrintTierBreakdown(repo);
            return SummarizeResult.Empty;
        }

        var summarizer = kernelFactory.GetSummarizer(config, resolvedModel);
        var concurrency = parameters.Parallel ?? 1;

        ClaudeBatchSummarizer? claudeBatchService = null;
        INodeSummarizer nodeSummarizer;
        if (parameters.Batch)
        {
            claudeBatchService = new ClaudeBatchSummarizer(resolvedModel);
            nodeSummarizer = claudeBatchService;
        }
        else
        {
            nodeSummarizer = new ConcurrentNodeSummarizer(summarizer, concurrency);
        }

        Console.WriteLine($"Using {config.Provider} for summaries (model: {resolvedModel}, concurrency: {concurrency})");

        var maxDepth = await repo.GetMaxDepthAsync();
        var tiers = Enumerable.Range(0, maxDepth + 1);
        if (parameters.Tier?.Length > 0)
        {
            tiers = tiers.Where(t => parameters.Tier.Contains(t));
        }

        foreach (var tier in tiers)
        {
            var limit = parameters.Sample ? 1 : (int?)null;
            var nodes = await repo.GetTierNodesAsync(tier, parameters.Force, limit);

            Console.WriteLine($"\n=== Tier {tier}: {nodes.Count} nodes ===");

            if (nodes.Count == 0) continue;

            foreach (var node in nodes.Where(n => n.MissingChildSummaries > 0))
                Console.WriteLine($"  Warning: {node.FullName} has {node.MissingChildSummaries} children without summaries");

            var prompts = promptBuilder.BuildPrompts(nodes, config);
            var results = await nodeSummarizer.SummarizeAsync(prompts);

            if (config.SearchTextStrategy == SearchTextStrategy.FirstTwoSentences)
                DeriveSearchText(results);

            if (parameters.Sample)
                PrintSampleResults(results);

            var batch = results
                .Select(r => (r.Node.ElementId, r.Result.Summary, r.Result.SearchText, r.Result.Tags))
                .ToList();
            await repo.SetSummariesBatchAsync(batch);
        }

        return new SummarizeResult(
            ResolvedModel: resolvedModel,
            Concurrency: concurrency,
            ClaudeBatchSummarizer: claudeBatchService);
    }

    private static void DeriveSearchText(List<NodeSummaryResult> results)
    {
        for (var i = 0; i < results.Count; i++)
        {
            var r = results[i];
            if (!string.IsNullOrWhiteSpace(r.Result.SearchText)) continue;
            var searchText = ExtractFirstTwoSentences(r.Result.Summary);
            results[i] = r with { Result = r.Result with { SearchText = searchText } };
        }
    }

    private static string ExtractFirstTwoSentences(string text)
    {
        var matches = SentenceEnd().Matches(text);
        if (matches.Count >= 2)
            return text[..(matches[1].Index + matches[1].Length)].Trim();
        return text;
    }

    [GeneratedRegex(@"[.!?](?=\s|$)")]
    private static partial Regex SentenceEnd();

    private static void PrintSampleResults(List<NodeSummaryResult> results)
    {
        foreach (var r in results)
        {
            Console.WriteLine($"\n--- {r.Node.FullName} ---");
            Console.WriteLine($"SUMMARY: {r.Result.Summary}");
            Console.WriteLine($"SEARCH:  {r.Result.SearchText}");
            Console.WriteLine($"TAGS: {string.Join(", ", r.Result.Tags)}");
        }
    }

    private static async Task PrintTierBreakdown(Neo4jSummarizeRepository repo)
    {
        var breakdown = await repo.GetTierBreakdownAsync();
        if (breakdown.Count == 0)
        {
            Console.WriteLine("No tiers computed. Run ingest first.");
            return;
        }

        var grouped = breakdown.GroupBy(b => b.Tier).OrderBy(g => g.Key);
        foreach (var group in grouped)
        {
            var parts = group.Select(b => $"{b.Total} {b.Label}{(b.Label.EndsWith("s") ? "es" : "s")}").ToList();
            var pending = group.Sum(b => b.Pending);
            Console.WriteLine($"Tier {group.Key}: {string.Join(", ", parts)} ({pending} pending)");
        }
    }
}

public record SummarizeResult(
    string ResolvedModel,
    int Concurrency,
    ClaudeBatchSummarizer? ClaudeBatchSummarizer)
{
    public static readonly SummarizeResult Empty = new("", 0, null);
    public bool IsEmpty => ResolvedModel == "";
}
