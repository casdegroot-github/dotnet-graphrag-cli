using GraphRagCli.Features.Summarize.Prompts;

namespace GraphRagCli.Features.Summarize.Summarizers;

public interface INodeSummarizer
{
    Task<List<NodeSummaryResult>> SummarizeAsync(List<EmbeddableNode> nodes);
}

public record NodeSummaryResult(EmbeddableNode Node, SummaryResult Result);
