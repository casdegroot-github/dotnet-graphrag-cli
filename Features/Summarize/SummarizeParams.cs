using Albatross.CommandLine.Annotations;
using GraphRagCli.Shared.Ai;
using GraphRagCli.Shared.Options;

namespace GraphRagCli.Features.Summarize;

[Verb<SummarizeCommandHandler>("summarize", Description = "Generate LLM summaries for code graph nodes")]
public record SummarizeParams
{
    [Option(DefaultToInitializer = true, Description = "Summary provider")]
    public Provider Provider { get; init; } = Provider.Ollama;

    [Option(Description = "Summary model (default: qwen2.5-coder:7b for Ollama, claude-haiku-4-5-20251001 for Claude)")]
    public string? Model { get; init; }

    [Option(Description = "Re-summarize all nodes, not just changed")]
    public bool Force { get; init; }

    [Option(Description = "Concurrent summarization calls (max: 4 for Ollama, 8 for Claude)")]
    public int? Parallel { get; init; }

    [Option(Description = "Use Claude Batch API (50% cheaper, async processing)")]
    public bool Batch { get; init; }

    [Option(Description = "Only process first N nodes (for testing)")]
    public int? Limit { get; init; }

    [Option(Description = "Test with 1 node per type")]
    public bool Sample { get; init; }

    [UseOption<StepOption>]
    public string[]? Step { get; init; }

    [UseOption<SkipOption>]
    public string[]? Skip { get; init; }

    [UseOption<ListStepsOption>]
    public bool ListSteps { get; init; }

    [UseOption<DatabaseOption>]
    public string? Database { get; init; }
}

public sealed partial class SummarizeCommand
{
    partial void Initialize()
    {
        this.Validators.Add(result =>
        {
            if (result.GetValue(this.Option_Batch) && result.GetValue(this.Option_Provider) == Provider.Ollama)
                result.AddError("--batch is only supported with --provider Claude");
            if (result.GetValue(this.Option_Batch) && result.GetValue(this.Option_Parallel) is > 0)
                result.AddError("--batch and --parallel are mutually exclusive");

            var parallel = result.GetValue(this.Option_Parallel);
            if (parallel.HasValue)
            {
                var config = ProviderConfig.For(result.GetValue(this.Option_Provider));
                if (parallel.Value > config.MaxConcurrency)
                    result.AddError($"--parallel {parallel.Value} exceeds max concurrency for {config.Provider} ({config.MaxConcurrency})");
                if (parallel.Value < 1)
                    result.AddError("--parallel must be at least 1");
            }
        });
    }
}
