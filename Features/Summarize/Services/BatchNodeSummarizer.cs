using System.Diagnostics;

namespace GraphRagCli.Features.Summarize.Services;

public class BatchNodeSummarizer(ClaudeBatchService claude) : INodeSummarizer
{
    public async Task<List<string>> SummarizeNodesAsync(Neo4jSummarizeRepository repo, List<EmbeddableNode> nodes, bool sample)
    {
        var changedNames = new List<string>();

        var templateNodes = nodes.Where(n => TemplateNode.IsTemplate(n.Prompt)).ToList();
        var llmNodes = nodes.Where(n => !TemplateNode.IsTemplate(n.Prompt)).ToList();

        Console.WriteLine($"Total nodes: {nodes.Count} ({templateNodes.Count} templates, {llmNodes.Count} need LLM)");

        foreach (var node in templateNodes)
        {
            var (summary, tags) = TemplateNode.Parse(node.Prompt);
            await repo.SetSummariesBatchAsync([(node.ElementId, summary, summary, tags)]);
            changedNames.Add(node.FullName);
        }
        if (templateNodes.Count > 0)
            Console.WriteLine($"Processed {templateNodes.Count} template nodes locally.");

        if (llmNodes.Count > 0)
        {
            var batchItems = llmNodes.Select(n => (n.FullName, n.Prompt)).ToList();
            var sw = Stopwatch.StartNew();
            var (batchId, idMap) = await claude.SubmitBatchAsync(batchItems);

            var results = await claude.WaitForBatchAsync(batchId, idMap);
            Console.WriteLine($"Batch completed in {sw.Elapsed:mm\\:ss}. Got {results.Count}/{llmNodes.Count} results.");

            var stored = 0;
            foreach (var node in llmNodes)
            {
                if (!results.TryGetValue(node.FullName, out var result)) continue;
                await repo.SetSummariesBatchAsync([(node.ElementId, result.Summary, result.SearchText, result.Tags)]);
                changedNames.Add(node.FullName);
                stored++;
                if (stored % 50 == 0)
                    Console.WriteLine($"  Stored {stored}/{results.Count} summaries...");
            }
            Console.WriteLine($"Stored {stored} summaries.");
        }

        return changedNames;
    }
}
