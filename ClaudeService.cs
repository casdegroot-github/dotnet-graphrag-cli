using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic;
using Anthropic.Models.Messages;
using Anthropic.Models.Messages.Batches;
using BatchParams = Anthropic.Models.Messages.Batches.Params;
using BatchRequest = Anthropic.Models.Messages.Batches.Request;

namespace CodeGraphIndexer;

public class ClaudeService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly Dictionary<string, JsonElement> SummarySchemaDict;

    static ClaudeService()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "docstring": {
                        "type": "string",
                        "description": "A thorough and concise summary of the code's purpose and role in the system. Cover architectural role, composition patterns, and usage context where relevant."
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
                "required": ["docstring", "searchText", "tags"],
                "additionalProperties": false
            }
            """;
        var doc = JsonDocument.Parse(schemaJson);
        SummarySchemaDict = new Dictionary<string, JsonElement>();
        foreach (var prop in doc.RootElement.EnumerateObject())
            SummarySchemaDict[prop.Name] = prop.Value.Clone();
    }

    private readonly string _model;
    private readonly AnthropicClient _client = new();
    private long _totalInputTokens;
    private long _totalOutputTokens;

    public ClaudeService(string model = "claude-haiku-4-5-20251001")
    {
        _model = model;
    }

    public long TotalInputTokens => _totalInputTokens;
    public long TotalOutputTokens => _totalOutputTokens;

    private OutputConfig BuildOutputConfig() => new()
    {
        Format = new JsonOutputFormat { Schema = SummarySchemaDict }
    };

    public async Task<OllamaService.SummaryResult> GenerateSummaryAsync(string prompt)
    {
        var parameters = new MessageCreateParams
        {
            Messages = [new MessageParam { Role = Role.User, Content = prompt }],
            MaxTokens = 1024,
            Model = _model,
            OutputConfig = BuildOutputConfig()
        };

        var response = await _client.Messages.Create(parameters);

        Interlocked.Add(ref _totalInputTokens, response.Usage.InputTokens);
        Interlocked.Add(ref _totalOutputTokens, response.Usage.OutputTokens);

        var text = ExtractText(response);
        return ParseResponse(text);
    }

    /// <summary>
    /// Submit all prompts as a batch for 50% cost savings. Returns a batch ID to poll.
    /// </summary>
    public async Task<(string BatchId, Dictionary<string, string> IdMap)> SubmitBatchAsync(List<(string Id, string Prompt)> items)
    {
        // Batch API requires custom_id matching ^[a-zA-Z0-9_-]{1,64}$
        // Map index-based IDs back to full names
        var idMap = new Dictionary<string, string>();
        var requests = items.Select((item, idx) =>
        {
            var safeId = $"node_{idx}";
            idMap[safeId] = item.Id;
            return new BatchRequest
            {
                CustomID = safeId,
                Params = new BatchParams
                {
                    Messages = [new MessageParam { Role = Role.User, Content = item.Prompt }],
                    MaxTokens = 1024,
                    Model = _model,
                    OutputConfig = BuildOutputConfig()
                }
            };
        }).ToList();

        var batch = await _client.Messages.Batches.Create(new BatchCreateParams { Requests = requests });
        Console.WriteLine($"  Batch submitted: {batch.ID} ({items.Count} requests)");
        return (batch.ID, idMap);
    }

    /// <summary>
    /// Poll until batch completes, then return results keyed by custom_id.
    /// </summary>
    public async Task<Dictionary<string, OllamaService.SummaryResult>> WaitForBatchAsync(string batchId, Dictionary<string, string> idMap)
    {
        Console.Write("  Waiting for batch");
        while (true)
        {
            var status = await _client.Messages.Batches.Retrieve(new BatchRetrieveParams { MessageBatchID = batchId });

            if (status.ProcessingStatus == ProcessingStatus.Ended)
            {
                Console.WriteLine(" done!");
                break;
            }

            if (status.ProcessingStatus == ProcessingStatus.Canceling)
                throw new InvalidOperationException($"Batch {batchId} is being canceled");

            Console.Write(".");
            await Task.Delay(TimeSpan.FromSeconds(10));
        }

        var results = new Dictionary<string, OllamaService.SummaryResult>();
        await foreach (var line in _client.Messages.Batches.ResultsStreaming(new BatchResultsParams { MessageBatchID = batchId }))
        {
            if (!line.Result.TryPickSucceeded(out var succeeded))
            {
                var resultType = line.Result.Type.ToString();
                var resultJson = line.Result.Json.ToString();
                Console.WriteLine($"  Warning: non-succeeded result for {line.CustomID}, type={resultType}, json={resultJson[..Math.Min(200, resultJson.Length)]}");
                continue;
            }

            var usage = succeeded.Message.Usage;
            Interlocked.Add(ref _totalInputTokens, usage.InputTokens);
            Interlocked.Add(ref _totalOutputTokens, usage.OutputTokens);

            var text = ExtractText(succeeded.Message);
            try
            {
                var originalId = idMap.GetValueOrDefault(line.CustomID, line.CustomID);
                results[originalId] = ParseResponse(text);
            }
            catch
            {
                Console.WriteLine($"  Warning: failed to parse result for {line.CustomID}");
            }
        }

        return results;
    }

    private static string ExtractText(Message message)
    {
        foreach (var block in message.Content)
        {
            if (block.TryPickText(out var textBlock))
                return textBlock.Text;
        }
        return "{}";
    }

    private static OllamaService.SummaryResult ParseResponse(string responseText)
    {
        var result = JsonSerializer.Deserialize<OllamaService.SummaryResult>(responseText, JsonOptions)
            ?? new OllamaService.SummaryResult("", []);

        if (string.IsNullOrWhiteSpace(result.Docstring))
            throw new InvalidOperationException($"Claude returned empty docstring. Raw: {responseText}");

        return result with { Tags = result.Tags.Select(t => t.ToUpperInvariant()).ToArray() };
    }

    public decimal EstimateCostUsd(bool isBatch = false)
    {
        var (inputPricePerM, outputPricePerM) = _model switch
        {
            var m when m.Contains("haiku") => (0.80m, 4.00m),
            var m when m.Contains("sonnet") => (3.00m, 15.00m),
            var m when m.Contains("opus") => (15.00m, 75.00m),
            _ => (3.00m, 15.00m)
        };

        // Batch API gives 50% discount
        if (isBatch)
        {
            inputPricePerM /= 2;
            outputPricePerM /= 2;
        }

        return (_totalInputTokens / 1_000_000m * inputPricePerM)
             + (_totalOutputTokens / 1_000_000m * outputPricePerM);
    }
}
