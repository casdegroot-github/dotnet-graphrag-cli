namespace GraphRagCli.Features.Summarize.Services;

public interface IAggregationPromptBuilder
{
    List<EmbeddableNode> BuildNamespaceNodes(List<AggregationData> data, ProviderConfig config);
    List<EmbeddableNode> BuildProjectNodes(List<AggregationData> data);
    List<EmbeddableNode> BuildSolutionNodes(List<AggregationData> data);
}
