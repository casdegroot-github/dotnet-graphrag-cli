using System.Diagnostics;
using GraphRagCli.Features.Summarize.Prompts;
using GraphRagCli.Shared.Progress;

namespace GraphRagCli.Features.Summarize.Summarizers;

public class ConcurrentNodeSummarizer(Summarizer summarizer, int maxConcurrency = 2) : INodeSummarizer
{
    private const int MaxRetries = 2;

    public async Task<List<NodeSummaryResult>> SummarizeAsync(List<EmbeddableNode> nodes)
    {
        var results = new List<NodeSummaryResult>();
        if (nodes.Count == 0) return results;

        var sw = Stopwatch.StartNew();
        var completed = 0;
        var failed = 0;
        var total = nodes.Count;

        var semaphore = new SemaphoreSlim(maxConcurrency);
        var tasks = nodes.Select(async node =>
        {
            await semaphore.WaitAsync();
            try
            {
                var result = await SummarizeWithRetryAsync(node);
                var count = Interlocked.Increment(ref completed);

                lock (results)
                {
                    results.Add(new NodeSummaryResult(node, result));
                    ProgressHelper.Render(count, total, sw.Elapsed, 30, node.FullName);
                }
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref completed);
                Interlocked.Increment(ref failed);
                lock (results)
                {
                    ProgressHelper.Render(completed, total, sw.Elapsed, 30, $"FAILED: {node.FullName}");
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

        if (failed > 0) Console.WriteLine($"\n{failed} nodes failed after {MaxRetries} retries each.");
        Console.WriteLine($"Done! Summarized {completed - failed}/{total} nodes in {sw.Elapsed:mm\\:ss}.");
        return results;
    }

    private async Task<SummaryResult> SummarizeWithRetryAsync(EmbeddableNode node)
    {
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                return await summarizer.SummarizeAsync(node.Prompt);
            }
            catch when (attempt < MaxRetries)
            {
                var delay = TimeSpan.FromSeconds(2 * (attempt + 1));
                Console.Error.WriteLine($"\n  Retry {attempt + 1}/{MaxRetries} for {node.FullName} in {delay.TotalSeconds}s...");
                await Task.Delay(delay);
            }
        }

        throw new InvalidOperationException("Unreachable");
    }
}
