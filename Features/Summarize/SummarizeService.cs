using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GraphRagCli.Features.Summarize.Prompts;
using GraphRagCli.Features.Summarize.Summarizers;
using GraphRagCli.Shared;
using GraphRagCli.Shared.Ai;
using GraphRagCli.Shared.GraphDb;
using Microsoft.Extensions.AI;

namespace GraphRagCli.Features.Summarize;

public partial class SummarizeService(
    Neo4jSessionFactory sessionFactory,
    KernelFactory kernelFactory,
    ModelsConfig modelsConfig,
    IPromptBuilder promptBuilder)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    public async Task<SummarizeResult> RunAsync(SummarizeParams parameters, CancellationToken ct = default)
    {
        var resolvedModel = modelsConfig.ResolveSummarizeModelName(parameters.Model);
        var config = modelsConfig.GetSummarizeModel(parameters.Model);

        await using var driver = await sessionFactory.CreateDriverAsync(parameters.Database);
        var repo = new Neo4jSummarizeRepository(driver);

        if (parameters.ListTiers)
        {
            await PrintTierBreakdown(repo);
            return SummarizeResult.Empty;
        }

        if (parameters.ConsolidateTags)
        {
            await ConsolidateTagsAsync(repo, kernelFactory);
            return SummarizeResult.Empty;
        }

        var summarizer = kernelFactory.GetSummarizer(config, resolvedModel);
        var concurrency = config.Concurrency;

        var concurrentSummarizer = new ConcurrentNodeSummarizer(summarizer, concurrency);
        ClaudeBatchSummarizer? claudeBatchService = parameters.Batch ? new ClaudeBatchSummarizer(resolvedModel) : null;

        if (!string.IsNullOrEmpty(parameters.Id))
        {
            await CompareNodeAsync(parameters.Id, config, parameters, concurrentSummarizer, repo);
            return new SummarizeResult(resolvedModel, concurrency, null);
        }

        Console.WriteLine($"Using {config.Provider} for summaries (model: {resolvedModel}, concurrency: {concurrency})");

        if (parameters.Force)
        {
            var marked = await repo.MarkAllNeedsSummaryAsync();
            Console.WriteLine($"Force mode: marked {marked} nodes for re-summarization");
        }

        var maxDepth = await repo.GetMaxDepthAsync();
        var tiers = Enumerable.Range(0, maxDepth + 1);
        if (parameters.Tier?.Length > 0)
        {
            tiers = tiers.Where(t => parameters.Tier.Contains(t));
        }

        foreach (var tier in tiers)
        {
            var limit = parameters.Sample ? 1 : (int?)null;
            var nodes = await repo.GetTierNodesAsync(tier, limit);

            Console.WriteLine($"\n=== Tier {tier}: {nodes.Count} nodes ===");

            if (nodes.Count == 0) continue;

            var normalNodes = new List<IGraphNode>();
            var oversizedResults = new List<NodeSummaryResult>();

            foreach (var node in nodes)
            {
                var promptSize = PromptBuilder.BuildContentText(node).Length;
                if (config.MaxPromptChars > 0 && promptSize > config.MaxPromptChars)
                {
                    Console.WriteLine($"  Node '{node.FullName}' is oversized ({promptSize:N0} chars). Processing in Map-Reduce mode...");
                    var finalResult = await ProcessOversizedNodeAsync(node, config, parameters, concurrentSummarizer);
                    oversizedResults.Add(finalResult);
                }
                else
                {
                    normalNodes.Add(node);
                }
            }

            var prompts = promptBuilder.BuildPrompts(normalNodes, config, parameters.Prompt);
            var useBatch = claudeBatchService != null && prompts.Count >= 100;
            INodeSummarizer activeSummarizer = useBatch ? claudeBatchService! : concurrentSummarizer;
            if (useBatch) Console.WriteLine("  Using batch API for normal nodes");

            var results = await activeSummarizer.SummarizeAsync(prompts);
            results.AddRange(oversizedResults);

            if (config.SearchTextStrategy == SearchTextStrategy.FirstTwoSentences)
                DeriveSearchText(results);

            if (parameters.Sample)
                PrintSampleResults(results);

            var batch = results
                .Select(r => (r.Node.ElementId, r.Result.Summary, r.Result.SearchText, r.Result.Tags))
                .ToList();
            await repo.SetSummariesBatchAsync(batch);
        }

        if (!parameters.Sample)
            await ConsolidateTagsAsync(repo, kernelFactory);

        return new SummarizeResult(
            ResolvedModel: resolvedModel,
            Concurrency: concurrency,
            ClaudeBatchSummarizer: claudeBatchService);
    }

    private async Task CompareNodeAsync(string id, SummarizeModelConfig config, SummarizeParams parameters, INodeSummarizer nodeSummarizer, Neo4jSummarizeRepository repo)
    {
        var node = await repo.GetNodeByIdAsync(id);
        if (node == null)
        {
            Console.WriteLine($"Node {id} not found.");
            return;
        }

        var promptNode = promptBuilder.BuildPrompts([node], config, parameters.Prompt).First();

        Console.WriteLine("\n================================================================================");
        Console.WriteLine($"COMPARISON FOR: {node.FullName} ({id})");
        Console.WriteLine("================================================================================\n");

        Console.WriteLine("--- OLD SUMMARY ---");
        Console.WriteLine(node.Summary ?? "(none)");
        Console.WriteLine("\n--- PROMPT SENT TO LLM ---");
        Console.WriteLine(promptNode.Prompt);

        Console.WriteLine("\n--- GENERATING NEW SUMMARY... ---");
        var result = await nodeSummarizer.SummarizeAsync([promptNode]);
        var newSummary = result.First().Result;

        Console.WriteLine("\n--- NEW SUMMARY ---");
        Console.WriteLine(newSummary.Summary);
        Console.WriteLine($"\nTAGS: {string.Join(", ", newSummary.Tags)}");
        Console.WriteLine("================================================================================\n");
    }

    private async Task<NodeSummaryResult> ProcessOversizedNodeAsync(
        IGraphNode node, SummarizeModelConfig config, SummarizeParams parameters, INodeSummarizer nodeSummarizer)
    {
        var chunks = ChunkNode(node, config.MaxPromptChars);
        var chunkPrompts = chunks.Select((c, i) => new EmbeddableNode(
            $"{node.Id.Value}_chunk_{i}",
            $"{node.FullName} (Part {i + 1}/{chunks.Count})",
            $"{(parameters.Prompt != null ? parameters.Prompt + "\n\n" : "")}Summarize this portion of the code. Content:\n{c}",
            node.Labels)).ToList();

        Console.WriteLine($"    Mapping {chunks.Count} chunks...");
        var chunkResults = await nodeSummarizer.SummarizeAsync(chunkPrompts);

        // Build a synthetic node with chunk summaries as children
        var chunkChildren = chunkResults
            .Select(r => new RelatedNode(new NodeId(r.Node.ElementId), r.Node.FullName, r.Result.Summary))
            .ToList();

        // Create a synthetic namespace-like node for reduction
        var syntheticNode = new NamespaceNode(
            node.Id, node.FullName, node.Labels,
            node.Summary, node.SearchText, node.BodyHash,
            Types: chunkChildren,
            Project: null);

        var finalPrompt = ((PromptBuilder)promptBuilder).BuildPrompt(syntheticNode, config, parameters.Prompt);

        Console.WriteLine($"    Reducing {chunks.Count} summaries into final architectural overview...");
        var finalResults = await nodeSummarizer.SummarizeAsync([finalPrompt]);
        return finalResults.First();
    }

    private static List<string> ChunkNode(IGraphNode node, int maxChars)
    {
        var content = PromptBuilder.BuildContentText(node);
        var chunks = new List<string>();
        var current = new List<string>();
        var currentSize = 0;

        foreach (var line in content.Split('\n'))
        {
            if (currentSize + line.Length > maxChars && current.Count > 0)
            {
                chunks.Add(string.Join("\n", current));
                current.Clear();
                currentSize = 0;
            }
            current.Add(line);
            currentSize += line.Length + 1;
        }

        if (current.Count > 0)
            chunks.Add(string.Join("\n", current));

        return chunks;
    }

    private static void DeriveSearchText(List<NodeSummaryResult> results)
    {
        for (var i = 0; i < results.Count; i++)
        {
            var r = results[i];
            if (!string.IsNullOrWhiteSpace(r.Result.SearchText)) continue;
            var searchText = ExtractFirstTwoSentences(r.Result.Summary);
            results[i] = r with { Result = r.Result with { SearchText = searchText } };
        }
    }

    private static string ExtractFirstTwoSentences(string text)
    {
        var matches = SentenceEnd().Matches(text);
        if (matches.Count >= 2)
            return text[..(matches[1].Index + matches[1].Length)].Trim();
        return text;
    }

    [GeneratedRegex(@"[.!?](?=\s|$)")]
    private static partial Regex SentenceEnd();

    public async Task ConsolidateTagsAsync(Neo4jSummarizeRepository repo, KernelFactory kernelFactory)
    {
        var tagCounts = await repo.GetAllTagsWithCountsAsync();
        if (tagCounts.Count <= 1) return;

        Console.WriteLine($"\nConsolidating {tagCounts.Count} distinct tags...");

        var tagList = string.Join("\n", tagCounts
            .OrderByDescending(kv => kv.Value)
            .Select(kv => $"  {kv.Key} ({kv.Value} nodes)"));

        var prompt = $"""
            <ROLE>
            You are normalizing tags across a code intelligence graph into a clean taxonomy.
            </ROLE>

            <CONTEXT>
            All tags currently assigned to nodes, with usage counts:
            {tagList}
            </CONTEXT>

            <INSTRUCTIONS>
            Do this in two passes:

            Pass 1 — Merge synonyms and near-duplicates (e.g., GRAPH-PROCESSING + GRAPH_PROCESSING, AI + AI_INTEGRATION + AI_SERVICES, CONFIG + CONFIGURATION).

            Pass 2 — Look at the result. Any canonical tag that would have fewer than 5 total nodes: merge it into the closest larger tag. Be aggressive — the goal is 10-20 final tags, not 50.

            Rules:
            - Keep tags SHORT (1-2 words), UPPERCASE, underscores for multi-word
            - Preserve high-count tags as canonical targets
            - Every original tag must appear in the mapping

            Return the final mapping of every old tag to its canonical tag.
            </INSTRUCTIONS>
            """;

        var chatClient = kernelFactory.CreateChatClient("claude");
        var options = new ChatOptions
        {
            ResponseFormat = ChatResponseFormat.ForJsonSchema<TagConsolidationResult>(),
            Temperature = 0f,
            MaxOutputTokens = 8192
        };
        var response = await chatClient.GetResponseAsync(prompt, options);
        var text = response.Text ?? "{}";

        var result = JsonSerializer.Deserialize<TagConsolidationResult>(text, JsonOptions);
        if (result?.Mappings == null || result.Mappings.Length == 0)
        {
            Console.WriteLine("  Empty tag mapping. Skipping consolidation.");
            return;
        }

        var remaps = result.Mappings
            .Where(m => m.From != m.To)
            .ToDictionary(m => m.From, m => m.To);

        if (remaps.Count == 0)
        {
            Console.WriteLine("  No tags to consolidate — taxonomy is clean.");
            return;
        }

        Console.WriteLine($"  Remapping {remaps.Count} tags:");
        foreach (var (old, @new) in remaps.OrderBy(kv => kv.Key))
            Console.WriteLine($"    {old} → {@new}");

        var updated = await repo.RemapTagsAsync(remaps);
        Console.WriteLine($"  Updated {updated} node tag assignments.");
    }

    private static void PrintSampleResults(List<NodeSummaryResult> results)
    {
        foreach (var r in results)
        {
            Console.WriteLine($"\n--- {r.Node.FullName} ---");
            Console.WriteLine($"SUMMARY: {r.Result.Summary}");
            Console.WriteLine($"SEARCH:  {r.Result.SearchText}");
            Console.WriteLine($"TAGS: {string.Join(", ", r.Result.Tags)}");
        }
    }

    private static async Task PrintTierBreakdown(Neo4jSummarizeRepository repo)
    {
        var breakdown = await repo.GetTierBreakdownAsync();
        if (breakdown.Count == 0)
        {
            Console.WriteLine("No tiers computed. Run ingest first.");
            return;
        }

        var grouped = breakdown.GroupBy(b => b.Tier).OrderBy(g => g.Key);
        foreach (var group in grouped)
        {
            var parts = group.Select(b => $"{b.Total} {b.Label}{(b.Label.EndsWith("s") ? "es" : "s")}").ToList();
            var pending = group.Sum(b => b.Pending);
            Console.WriteLine($"Tier {group.Key}: {string.Join(", ", parts)} ({pending} pending)");
        }
    }
}

public record SummarizeResult(
    string ResolvedModel,
    int Concurrency,
    ClaudeBatchSummarizer? ClaudeBatchSummarizer)
{
    public static readonly SummarizeResult Empty = new("", 0, null);
    public bool IsEmpty => ResolvedModel == "";
}

public record TagMapping(
    [property: JsonPropertyName("from")]
    [property: Description("Original tag name")]
    string From,

    [property: JsonPropertyName("to")]
    [property: Description("Canonical tag to map to (same as 'from' if unchanged)")]
    string To);

public record TagConsolidationResult(
    [property: JsonPropertyName("mappings")]
    [property: Description("Array of tag mappings from old to new canonical tags")]
    TagMapping[] Mappings);
