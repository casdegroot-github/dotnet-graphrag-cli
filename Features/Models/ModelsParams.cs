using Albatross.CommandLine.Annotations;

namespace GraphRagCli.Features.Models;

[Verb("models", Description = "Manage model configurations for embedding and summarization")]
public record ModelsParams;

[Verb<ModelsListHandler>("models list", Description = "List all configured models")]
public record ModelsListParams;

[Verb<ModelsAddHandler>("models add", Description = "Add a model configuration")]
public record ModelsAddParams
{
    [Argument(Description = "Model type: embedding or summarize")]
    public required string Type { get; init; }

    [Argument(Description = "Model name (e.g. nomic-embed-text, qwen3-coder)")]
    public required string Name { get; init; }

    [Option(Description = "AI provider (e.g. ollama, claude)")]
    public required string Provider { get; init; }

    [Option(Description = "Embedding dimensions (required for embedding models)")]
    public int? Dimensions { get; init; }

    [Option(Description = "Document prefix for embedding")]
    public string? DocumentPrefix { get; init; }

    [Option(Description = "Query prefix for embedding")]
    public string? QueryPrefix { get; init; }

    [Option(Description = "Max prompt characters (required for summarize models)")]
    public int? MaxPromptChars { get; init; }

    [Option(DefaultToInitializer = true, Description = "Max concurrency for summarization")]
    public int Concurrency { get; init; } = 1;
}

[Verb<ModelsRemoveHandler>("models remove", Description = "Remove a model")]
public record ModelsRemoveParams
{
    [Argument(Description = "Model name to remove")]
    public required string Name { get; init; }
}

[Verb<ModelsDefaultHandler>("models default", Description = "Set the default model for a type")]
public record ModelsDefaultParams
{
    [Argument(Description = "Model type: embedding or summarize")]
    public required string Type { get; init; }

    [Argument(Description = "Model name")]
    public required string Name { get; init; }
}
