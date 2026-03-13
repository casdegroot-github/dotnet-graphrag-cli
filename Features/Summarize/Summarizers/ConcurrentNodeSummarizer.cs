using System.Diagnostics;
using GraphRagCli.Shared.Progress;

namespace GraphRagCli.Features.Summarize.Summarizers;

public class ConcurrentNodeSummarizer(Summarizer summarizer, int maxConcurrency = 2) : INodeSummarizer
{
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
                var result = await summarizer.SummarizeAsync(node.Prompt);
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

        if (failed > 0) Console.WriteLine($"\n{failed} nodes failed.");
        Console.WriteLine($"Done! Summarized {completed - failed}/{total} nodes in {sw.Elapsed:mm\\:ss}.");
        return results;
    }
}
