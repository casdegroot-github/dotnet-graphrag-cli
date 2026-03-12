namespace GraphRagCli.Features.Summarize.Services;

public interface INodeSummarizer
{
    Task<List<string>> SummarizeNodesAsync(Neo4jSummarizeRepository repo, List<EmbeddableNode> nodes, bool sample);
}
