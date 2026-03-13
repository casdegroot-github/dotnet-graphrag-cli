using System.ComponentModel;
using System.Text.Json.Serialization;

namespace GraphRagCli.Features.Summarize.Prompts;

public record SummaryResult(
    [property: JsonPropertyName("summary")]
    [property: Description("Detailed summary as instructed in the prompt.")]
    string Summary,

    [property: JsonPropertyName("tags")]
    [property: Description("1-3 tags describing the code's role")]
    string[] Tags,

    [property: JsonPropertyName("searchText")]
    [property: Description("1-2 sentences optimized for vector search retrieval. Pack with relevant keywords, technologies, patterns, and domain terms that a developer might search for. Cover multiple angles — what it does, what technologies it uses, what problem it solves.")]
    string? SearchText = null);
