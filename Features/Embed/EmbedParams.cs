using Albatross.CommandLine.Annotations;
using GraphRagCli.Shared.Options;

namespace GraphRagCli.Features.Embed;

[Verb<EmbedCommandHandler>("embed", Description = "Generate embeddings from existing summaries and compute centrality")]
public record EmbedParams
{
    [Option(Description = "Re-embed all nodes, not just those needing embedding")]
    public bool Force { get; init; }

    [Option(Description = "Max concurrent embedding calls (default: 4)")]
    public int? MaxConcurrency { get; init; }

    [UseOption<DatabaseOption>]
    public string? Database { get; init; }
}
