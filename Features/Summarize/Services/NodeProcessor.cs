namespace GraphRagCli.Features.Summarize.Services;

public class NodeProcessor(Summarizer summarizer) : INodeProcessor
{
    public async Task<SummaryResult> ProcessAsync(EmbeddableNode node)
    {
        if (TemplateNode.IsTemplate(node.Prompt))
        {
            var (summary, tags) = TemplateNode.Parse(node.Prompt);
            return new SummaryResult(summary, tags, summary);
        }

        return await summarizer.SummarizeAsync(node.Prompt);
    }
}
