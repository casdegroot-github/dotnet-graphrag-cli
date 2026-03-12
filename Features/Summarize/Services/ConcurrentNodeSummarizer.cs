using System.Diagnostics;
using GraphRagCli.Shared.Progress;

namespace GraphRagCli.Features.Summarize.Services;

public class ConcurrentNodeSummarizer(INodeProcessor processor, int maxConcurrency = 2) : INodeSummarizer
{
    public async Task<List<string>> SummarizeNodesAsync(Neo4jSummarizeRepository repo, List<EmbeddableNode> nodes, bool sample)
    {
        var changedNames = new List<string>();

        if (sample)
        {
            var types = new[] { "Class", "Interface", "Method", "Enum" };
            nodes = types
                .Select(t => nodes.FirstOrDefault(n => n.Labels.Contains(t)))
                .Where(n => n != null)
                .ToList()!;
        }

        Console.WriteLine($"Found {nodes.Count} nodes to process.");

        if (nodes.Count == 0)
        {
            Console.WriteLine("Nothing to do.");
            return changedNames;
        }

        var sw = Stopwatch.StartNew();
        var completed = 0;
        var failed = 0;
        var total = nodes.Count;
        var barWidth = 30;

        if (sample)
        {
            foreach (var node in nodes)
            {
                var type = node.Labels.FirstOrDefault(l => l is "Class" or "Interface" or "Method" or "Enum") ?? "?";
                Console.WriteLine($"\n{"=== " + type + ": " + node.FullName + " " + new string('=', 40)}");
                Console.WriteLine($"PROMPT:\n{node.Prompt[..Math.Min(node.Prompt.Length, 500)]}");
                if (node.Prompt.Length > 500) Console.WriteLine("  ...(truncated)");
                Console.WriteLine();

                try
                {
                    var result = await processor.ProcessAsync(node);
                    Console.WriteLine($"SUMMARY:\n{result.Summary}");
                    Console.WriteLine($"SEARCH TEXT: {result.SearchText}");
                    Console.WriteLine($"TAGS: {(result.Tags.Length > 0 ? string.Join(", ", result.Tags) : "(none)")}");
                    await repo.SetSummariesBatchAsync([(node.ElementId, result.Summary, result.SearchText, result.Tags)]);
                    changedNames.Add(node.FullName);
                    completed++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"FAILED: {ex.Message}");
                    failed++;
                }
            }
        }
        else
        {
            var semaphore = new SemaphoreSlim(maxConcurrency);
            var tasks = nodes.Select(async node =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var result = await processor.ProcessAsync(node);
                    await repo.SetSummariesBatchAsync([(node.ElementId, result.Summary, result.SearchText, result.Tags)]);
                    var count = Interlocked.Increment(ref completed);

                    lock (changedNames)
                    {
                        changedNames.Add(node.FullName);
                        ProgressHelper.Render(count, total, sw.Elapsed, barWidth, node.FullName);
                    }
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref completed);
                    Interlocked.Increment(ref failed);
                    lock (changedNames)
                    {
                        ProgressHelper.Render(completed, total, sw.Elapsed, barWidth, $"FAILED: {node.FullName}");
                        Console.Error.WriteLine($"\n  Error: {ex.Message}");
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            await Task.WhenAll(tasks);
            ProgressHelper.ClearLine();
        }

        if (failed > 0) Console.WriteLine($"\n{failed} nodes failed.");
        Console.WriteLine($"Done! Summarized {completed - failed}/{total} nodes in {sw.Elapsed:mm\\:ss}.");
        return changedNames;
    }
}
