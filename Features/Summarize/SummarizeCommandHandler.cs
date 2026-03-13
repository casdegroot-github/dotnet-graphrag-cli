using System.CommandLine;
using Albatross.CommandLine;
using GraphRagCli.Features.Summarize.Summarizers;
using GraphRagCli.Shared.Ai;

namespace GraphRagCli.Features.Summarize;

public class SummarizeCommandHandler(
    SummarizeService service,
    ModelsConfig modelsConfig,
    ParseResult result,
    SummarizeParams parameters) : BaseHandler<SummarizeParams>(result, parameters)
{
    public override async Task<int> InvokeAsync(CancellationToken ct)
    {
        PrintBanner(parameters, modelsConfig);

        try
        {
            var summarizeResult = await service.RunAsync(parameters, ct);

            if (summarizeResult.IsEmpty)
                return 0;

            if (summarizeResult.ClaudeBatchSummarizer != null)
                PrintClaudeUsageReport(summarizeResult.ClaudeBatchSummarizer, summarizeResult.ResolvedModel);

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    static void PrintBanner(SummarizeParams p, ModelsConfig config)
    {
        var modelName = config.ResolveSummarizeModelName(p.Model);
        var modelConfig = config.GetSummarizeModel(p.Model);

        Console.WriteLine("GraphRagCli - Summarize");
        Console.WriteLine($"  Database:   {p.Database ?? "(auto-detect)"}");
        Console.WriteLine($"  Model:      {modelName} ({modelConfig.Provider})");
        Console.WriteLine($"  Force:      {p.Force}");
        if (p.Parallel.HasValue) Console.WriteLine($"  Parallel:   {p.Parallel}");
        if (p.Batch) Console.WriteLine($"  Batch:      true");
        if (p.Tier is { Length: > 0 }) Console.WriteLine($"  Tiers:      {string.Join(", ", p.Tier)}");
        if (p.Sample) Console.WriteLine($"  Sample:     1 per type");
        Console.WriteLine();
    }

    static void PrintClaudeUsageReport(ClaudeBatchSummarizer claude, string model)
    {
        Console.WriteLine();
        Console.WriteLine("=== Claude API Usage Report ===");
        Console.WriteLine($"  Input tokens:   {claude.TotalInputTokens:N0}");
        Console.WriteLine($"  Output tokens:  {claude.TotalOutputTokens:N0}");
        Console.WriteLine($"  Total tokens:   {claude.TotalInputTokens + claude.TotalOutputTokens:N0}");
        Console.WriteLine($"  Estimated cost: ${claude.EstimateCostUsd(isBatch: true):F4}");
        Console.WriteLine($"  Model:          {model}");
        Console.WriteLine("  Mode:           batch (50% discount applied)");
    }
}
