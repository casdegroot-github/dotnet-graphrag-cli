using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic;
using GraphRagCli.Features.Summarize.Prompts;
using Anthropic.Models.Messages;
using Anthropic.Models.Messages.Batches;
using BatchParams = Anthropic.Models.Messages.Batches.Params;
using BatchRequest = Anthropic.Models.Messages.Batches.Request;

namespace GraphRagCli.Features.Summarize.Summarizers;

/// <summary>
/// Claude Batch API summarizer at 50% cost.
/// SK has no batch support, so this uses the Anthropic SDK directly.
/// </summary>
public class ClaudeBatchSummarizer : INodeSummarizer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly Dictionary<string, JsonElement> SummarySchemaDict;

    static ClaudeBatchSummarizer()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "summary": {
                        "type": "string",
                        "description": "A thorough and concise summary of the code's purpose and role in the system. Cover architectural role, composition patterns, and usage context where relevant."
                    },
                    "searchText": {
                        "type": "string",
                        "description": "1-2 sentences optimized for vector search retrieval. Pack with relevant keywords, technologies, patterns, and domain terms that a developer might search for. Cover multiple angles — what it does, what technologies it uses, what problem it solves."
                    },
                    "tags": {
                        "type": "array",
                        "items": { "type": "string" },
                        "description": "1-3 tags describing the code's role"
                    }
                },
                "required": ["summary", "searchText", "tags"],
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

    public ClaudeBatchSummarizer(string model)
    {
        _model = model;
    }

    public long TotalInputTokens => _totalInputTokens;
    public long TotalOutputTokens => _totalOutputTokens;

    private const int MaxBatchSize = 1000;

    public async Task<List<NodeSummaryResult>> SummarizeAsync(List<EmbeddableNode> nodes)
    {
        if (nodes.Count == 0) return [];

        var allResults = new Dictionary<string, SummaryResult>();
        var sw = Stopwatch.StartNew();

        var chunks = nodes
            .Select(n => (Id: n.SafeFullName, n.Prompt))
            .Chunk(MaxBatchSize)
            .ToArray();

        for (var i = 0; i < chunks.Length; i++)
        {
            var chunk = chunks[i].ToList();
            if (chunks.Length > 1)
                Console.WriteLine($"  Chunk {i + 1}/{chunks.Length} ({chunk.Count} requests)");

            var (batchId, idMap) = await SubmitBatchAsync(chunk);
            var chunkResults = await WaitForBatchAsync(batchId, idMap);

            foreach (var kv in chunkResults)
            {
                if (kv.Key != null)
                    allResults[kv.Key] = kv.Value;
            }
        }

        Console.WriteLine($"Batch completed in {sw.Elapsed:mm\\:ss}. Got {allResults.Count}/{nodes.Count} results.");

        return nodes
            .Where(n => allResults.ContainsKey(n.SafeFullName))
            .Select(n => new NodeSummaryResult(n, allResults[n.SafeFullName]))
            .ToList();
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

        if (isBatch)
        {
            inputPricePerM /= 2;
            outputPricePerM /= 2;
        }

        return (_totalInputTokens / 1_000_000m * inputPricePerM)
             + (_totalOutputTokens / 1_000_000m * outputPricePerM);
    }

    private OutputConfig BuildOutputConfig() => new()
    {
        Format = new JsonOutputFormat { Schema = SummarySchemaDict }
    };

    private async Task<(string BatchId, Dictionary<string, string> IdMap)> SubmitBatchAsync(List<(string Id, string Prompt)> items)
    {
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

    private async Task<Dictionary<string, SummaryResult>> WaitForBatchAsync(string batchId, Dictionary<string, string> idMap)
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

        var results = new Dictionary<string, SummaryResult>();
        try
        {
            await foreach (var line in _client.Messages.Batches.ResultsStreaming(new BatchResultsParams { MessageBatchID = batchId }))
            {
                if (line?.CustomID == null) continue;

                if (!line.Result.TryPickSucceeded(out var succeeded))
                {
                    var resultType = line.Result.Type.ToString();
                    var errorMsg = "Unknown error";
                    
                    if (line.Result.TryPickErrored(out var error))
                    {
                        // Fallback to ToString() if specific properties are unknown
                        errorMsg = error.Error?.ToString() ?? "Unknown error object";
                    }
                    var originalId = idMap.GetValueOrDefault(line.CustomID, line.CustomID);
                    Console.WriteLine($"  Warning: node '{originalId}' failed with {resultType}. {errorMsg}");
                    continue;
                }

                var usage = succeeded.Message.Usage;
                Interlocked.Add(ref _totalInputTokens, usage.InputTokens);
                Interlocked.Add(ref _totalOutputTokens, usage.OutputTokens);

                var text = ExtractText(succeeded.Message);
                try
                {
                    var originalId = idMap.GetValueOrDefault(line.CustomID, line.CustomID);
                    if (originalId != null)
                    {
                        results[originalId] = ParseResponse(text);
                    }
                }
                catch
                {
                    var originalId = idMap.GetValueOrDefault(line.CustomID, line.CustomID);
                    Console.WriteLine($"  Warning: failed to parse result for '{originalId}'");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Error while streaming batch results: {ex.Message}");
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

    private static SummaryResult ParseResponse(string responseText)
    {
        var result = JsonSerializer.Deserialize<SummaryResult>(responseText, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to parse summary: {responseText}");

        if (string.IsNullOrWhiteSpace(result.Summary))
            throw new InvalidOperationException($"Claude returned empty summary. Raw: {responseText}");

        return result with { Tags = result.Tags.Select(t => t.ToUpperInvariant()).ToArray() };
    }
}
