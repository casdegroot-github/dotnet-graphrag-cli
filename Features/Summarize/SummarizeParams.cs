using Albatross.CommandLine.Annotations;
using GraphRagCli.Shared.Ai;
using GraphRagCli.Shared.Options;

namespace GraphRagCli.Features.Summarize;

[Verb<SummarizeCommandHandler>("summarize", Description = "Generate LLM summaries for code graph nodes")]
public record SummarizeParams
{
    [Option(Description = "Summary model (default: from models.json)")]
    public string? Model { get; init; }

    [Option(Description = "Re-summarize all nodes, not just changed")]
    public bool Force { get; init; }

    [Option(Description = "Concurrent summarization calls")]
    public int? Parallel { get; init; }

    [Option(Description = "Use Claude Batch API (50% cheaper, async processing)")]
    public bool Batch { get; init; }

    [Option(Description = "Test with 1 node per type")]
    public bool Sample { get; init; }

    [Option(Description = "Only process specific tiers (can specify multiple)")]
    public int[]? Tier { get; init; }

    [Option(Description = "List tier breakdown and exit")]
    public bool ListTiers { get; init; }

    [UseOption<DatabaseOption>]
    public string? Database { get; init; }
}

public sealed partial class SummarizeCommand
{
    partial void Initialize()
    {
        this.Validators.Add(result =>
        {
            var config = ModelConfigLoader.Load();
            var error = config.ValidateSummarizeOptions(
                result.GetValue(this.Option_Model),
                result.GetValue(this.Option_Batch),
                result.GetValue(this.Option_Parallel));

            if (error is not null)
                result.AddError(error);
        });
    }
}
