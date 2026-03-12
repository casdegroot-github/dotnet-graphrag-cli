namespace GraphRagCli.Features.Summarize.Services;

public interface INodeProcessor
{
    Task<SummaryResult> ProcessAsync(EmbeddableNode node);
}
