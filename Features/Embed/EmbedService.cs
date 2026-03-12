using System.Diagnostics;
using GraphRagCli.Shared.Ai;
using GraphRagCli.Shared.Progress;
using Neo4j.Driver;

namespace GraphRagCli.Features.Embed;

public class EmbedService
{
    public async Task<EmbedResult> EmbedAsync(
        IDriver driver, ITextEmbedder embedder, EmbedParams parameters)
    {
        var repo = new Neo4jEmbedRepository(driver);
        var nodes = await repo.GetNodesWithSummariesAsync(parameters.Force);

        if (nodes.Count == 0)
            return EmbedResult.Empty;

        var concurrency = parameters.MaxConcurrency is > 0 ? parameters.MaxConcurrency.Value : 4;
        var (embedded, failed) = await EmbedNodesAsync(repo, embedder, nodes, concurrency);
        var centralityComputed = await ComputeCentralityAsync(repo);

        return new EmbedResult(nodes.Count, embedded, failed, centralityComputed);
    }

    private static async Task<(int Embedded, int Failed)> EmbedNodesAsync(
        Neo4jEmbedRepository repo, ITextEmbedder embedder,
        List<EmbeddableNode> nodes, int concurrency)
    {
        var sw = Stopwatch.StartNew();
        var completed = 0;
        var failed = 0;
        var total = nodes.Count;
        var barWidth = 30;

        var semaphore = new SemaphoreSlim(concurrency);
        var tasks = nodes.Select(async node =>
        {
            await semaphore.WaitAsync();
            try
            {
                var textToEmbed = node.SearchText ?? node.Summary;
                var embedding = await embedder.EmbedDocumentAsync(textToEmbed);
                await repo.SetEmbeddingsBatchAsync([(node.ElementId, embedding)]);
                var count = Interlocked.Increment(ref completed);
                ProgressHelper.Render(count, total, sw.Elapsed, barWidth, node.FullName);
            }
            catch
            {
                Interlocked.Increment(ref completed);
                Interlocked.Increment(ref failed);
                ProgressHelper.Render(completed, total, sw.Elapsed, barWidth, $"FAILED: {node.FullName}");
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);
        ProgressHelper.ClearLine();

        if (failed > 0) Console.WriteLine($"\n{failed} nodes failed.");
        Console.WriteLine($"Done! Embedded {completed - failed}/{total} nodes in {sw.Elapsed:mm\\:ss}.");

        return (completed - failed, failed);
    }

    private static async Task<bool> ComputeCentralityAsync(Neo4jEmbedRepository repo)
    {
        Console.WriteLine("\n=== Centrality: GDS PageRank + degree ===");
        try
        {
            await repo.ComputeCentralityAsync();
            Console.WriteLine("Centrality scores computed.");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"GDS centrality failed (is GDS plugin installed?): {ex.Message}");
            return false;
        }
    }
}
