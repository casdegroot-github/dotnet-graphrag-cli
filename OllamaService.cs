using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeGraphIndexer;

public class OllamaService(string endpoint = "http://localhost:11434", string summaryModel = "qwen2.5-coder:7b")
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonElement SummarySchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "docstring": {
                    "type": "string",
                    "description": "A thorough and concise 3-4 sentence summary of the code's purpose and role in the system. Cover architectural role, composition patterns, and usage context where relevant."
                },
                "searchText": {
                    "type": "string",
                    "description": "Explain what this code does as if telling a colleague casually. 2-3 plain sentences, max 40 words. Use simple verbs like 'adds', 'configures', 'reads', 'writes', 'checks'. Start with 'This method/class/interface...'. Mention specific technologies it uses or integrates with (e.g. Kafka, MongoDB, SQL Server, DI container)."
                },
                "tags": {
                    "type": "array",
                    "items": { "type": "string" },
                    "description": "1-3 tags describing the code's role"
                }
            },
            "required": ["docstring", "searchText", "tags"]
        }
        """).RootElement;

    private readonly HttpClient _http = new() { BaseAddress = new Uri(endpoint), Timeout = TimeSpan.FromMinutes(5) };

    public record SummaryResult(string Docstring, string[] Tags, string? SearchText = null);

    public async Task<SummaryResult> GenerateSummaryAsync(string prompt)
    {
        var request = new { model = summaryModel, prompt, stream = false, format = SummarySchema, options = new { num_predict = 500 } };
        var response = await _http.PostAsJsonAsync("/api/generate", request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var responseText = json.GetProperty("response").GetString() ?? "{}";

        var result = JsonSerializer.Deserialize<SummaryResult>(responseText, JsonOptions)
            ?? new SummaryResult("", []);

        if (string.IsNullOrWhiteSpace(result.Docstring))
            throw new InvalidOperationException($"LLM returned empty docstring. Raw response: {responseText}");

        return result with { Tags = result.Tags.Select(t => t.ToUpperInvariant()).ToArray() };
    }

    public static string BuildPrompt(string codeBlock, string nodeType, string? contextSuffix, bool isEntryPoint = false)
    {
        var typeInstruction = nodeType switch
        {
            "Method" when isEntryPoint =>
                "This is a DI/hosting registration method that shows how to compose and configure system components. " +
                "Describe what it registers, what interfaces/contracts it requires, and how a developer would use it to set up the subsystem. " +
                "Treat the source code as a usage guide — highlight validation, required interfaces, and configuration patterns.",
            "Method" => "Describe what this method does and why it exists in the system.",
            "Class" => "Describe this class's responsibility and how it collaborates with other components.",
            "Interface" => "Describe the contract this interface defines and what concerns it abstracts.",
            "Enum" => "Describe what domain concept this enum represents. List all member values and briefly explain what each represents. Mention where this enum is typically used.",
            _ => "Describe the purpose and role of this code in the system."
        };

        return $"""
            You are a senior .NET engineer with expertise in code analysis and documentation.

            Analyze this C# code snippet and:
            1. {typeInstruction}
               Be thorough and concise. Focus on architectural role and relationships to other components.
               If related to database access, messaging, configuration, or dependency injection, mention this.
               Do NOT restate the class/method name. Do NOT start with "This method/class..."
            2. Assign 1-3 tags from this list that best describe its purpose:
               DATABASE, API, CONFIGURATION, UTILITY, PRODUCER, CONSUMER, EXTERNAL_SERVICE,
               DI_REGISTRATION, PIPELINE, MAPPING, VALIDATION, MESSAGING, CACHING, LOGGING,
               SERIALIZATION, AUTH, TESTING

            ```csharp
            {codeBlock}
            ```
            {contextSuffix ?? ""}
            """;
    }

    public static SummaryResult? TrySmallNodeSummary(string nodeType, string name, string fullName, string? sourceText, string? members)
    {
        var lineCount = (sourceText ?? "").Split('\n').Length;

        if (nodeType == "Interface" && lineCount <= 5 && string.IsNullOrWhiteSpace(sourceText?.Replace("{", "").Replace("}", "").Trim()))
            return new SummaryResult($"Marker interface for {name}", ["UTILITY"]);

        if (nodeType == "Interface" && lineCount <= 5)
        {
            var memberLines = (sourceText ?? "").Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("//") && !l.StartsWith("{") && !l.StartsWith("}") && !l.StartsWith("public") && !l.StartsWith("interface"))
                .ToList();
            if (memberLines.Count == 0)
                return new SummaryResult($"Marker interface for {name}", ["UTILITY"]);
        }

        // Enums always go through LLM for richer consumption-aware summaries
        return null;
    }

    public async Task<float[]> EmbedDocumentAsync(string text)
    {
        return await EmbedAsync($"search_document: {text}");
    }

    public async Task<float[]> EmbedQueryAsync(string text)
    {
        return await EmbedAsync($"search_query: {text}");
    }

    private async Task<float[]> EmbedAsync(string prompt)
    {
        var request = new { model = "snowflake-arctic-embed2", prompt };
        var response = await _http.PostAsJsonAsync("/api/embeddings", request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var arr = json.GetProperty("embedding");
        var embedding = new float[arr.GetArrayLength()];
        int i = 0;
        foreach (var val in arr.EnumerateArray())
            embedding[i++] = val.GetSingle();
        return embedding;
    }
}
