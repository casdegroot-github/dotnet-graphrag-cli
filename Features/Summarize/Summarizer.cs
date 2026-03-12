using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace GraphRagCli.Features.Summarize;

/// <summary>
/// Executes summarization prompts via IChatClient with structured JSON output.
/// Provider-agnostic — works with any IChatClient (Ollama, Claude, etc.).
/// </summary>
public class Summarizer(IChatClient chatClient, string? model = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ChatOptions _options = new()
    {
        ResponseFormat = ChatResponseFormat.ForJsonSchema<SummaryResult>(),
        Temperature = 0f,
        ModelId = model
    };

    public async Task<SummaryResult> SummarizeAsync(string prompt)
    {
        var response = await chatClient.GetResponseAsync(prompt, _options);
        var text = response.Text ?? "{}";

        var result = JsonSerializer.Deserialize<SummaryResult>(text, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to parse summary: {text}");

        if (string.IsNullOrWhiteSpace(result.Summary))
            throw new InvalidOperationException($"LLM returned empty summary. Raw: {text}");

        return result with { Tags = result.Tags.Select(t => t.ToUpperInvariant()).ToArray() };
    }
}
