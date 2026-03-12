using System.CommandLine;
using Albatross.CommandLine;

namespace GraphRagCli.Features.Summarize;

public class SummarizeCommandHandler(
    SummarizeService service,
    ParseResult result,
    SummarizeParams parameters) : BaseHandler<SummarizeParams>(result, parameters)
{
    public override async Task<int> InvokeAsync(CancellationToken ct)
    {
        PrintBanner(parameters);

        try
        {
            var summarizeResult = await service.RunAsync(parameters, ct);

            if (summarizeResult.IsEmpty)
                return 0;

            if (summarizeResult.ClaudeBatchService != null)
                PrintClaudeUsageReport(summarizeResult.ClaudeBatchService, summarizeResult.ResolvedModel);

            return summarizeResult.HasFailures ? 1 : 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    static void PrintBanner(SummarizeParams p)
    {
        Console.WriteLine("GraphRagCli - Summarize");
        Console.WriteLine($"  Database:   {p.Database ?? "(auto-detect)"}");
        Console.WriteLine($"  Provider:   {p.Provider}");
        Console.WriteLine($"  Model:      {p.Model ?? "(default)"}");
        Console.WriteLine($"  Force:      {p.Force}");
        if (p.Parallel.HasValue) Console.WriteLine($"  Parallel:   {p.Parallel}");
        if (p.Batch) Console.WriteLine($"  Batch:      true");
        if (p.Step is { Length: > 0 }) Console.WriteLine($"  Steps:      {string.Join(", ", p.Step)}");
        if (p.Skip is { Length: > 0 }) Console.WriteLine($"  Skip:       {string.Join(", ", p.Skip)}");
        if (p.Limit.HasValue) Console.WriteLine($"  Limit:      {p.Limit}");
        if (p.Sample) Console.WriteLine($"  Sample:     1 per type");
        Console.WriteLine();
    }

    static void PrintClaudeUsageReport(ClaudeBatchService claude, string model)
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