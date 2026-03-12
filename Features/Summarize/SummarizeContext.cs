using GraphRagCli.Features.Summarize.Services;

namespace GraphRagCli.Features.Summarize;

public record SummarizeContext(
    Neo4jSummarizeRepository Repo,
    INodeSummarizer NodeSummarizer,
    INodeSummarizer AggregationSummarizer,
    ProviderConfig Config,
    bool Force,
    int? Limit,
    bool Sample);
