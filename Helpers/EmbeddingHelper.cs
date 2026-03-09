using System.Diagnostics;

namespace GraphRagCli.Helpers;

public record EmbedSettings(
    bool OnlyChanged, int? Limit, bool Sample,
    bool RunPass1, bool RunPass2, bool RunPass3,
    ProviderConfig Config);

public static class EmbeddingHelper
{
    public static async Task<(string Summary, string? SearchText, string[] Tags, float[] Embedding)> ProcessNode(
        OllamaService ollama, Func<string, Task<OllamaService.SummaryResult>> summarize, Neo4jService.EmbeddableNode node)
    {
        if (node.Prompt.StartsWith("__TEMPLATE__"))
        {
            var parts = node.Prompt["__TEMPLATE__".Length..].Split("||", 2);
            var summary = parts[0];
            var tags = parts.Length > 1 ? parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries) : [];
            var embedding = await ollama.EmbedDocumentAsync(summary);
            return (summary, summary, tags, embedding);
        }

        var result = await summarize(node.Prompt);
        var textToEmbed = result.SearchText ?? result.Docstring;
        var emb = await ollama.EmbedDocumentAsync(textToEmbed);
        return (result.Docstring, result.SearchText, result.Tags, emb);
    }

    public static async Task<List<string>> EmbedNodes(
        Neo4jService neo4j, OllamaService ollama, Func<string, Task<OllamaService.SummaryResult>> summarize,
        List<Neo4jService.EmbeddableNode> nodes, bool sample, string fieldPrefix = "")
    {
        var changedNames = new List<string>();

        if (sample)
        {
            var types = new[] { "Class", "Interface", "Method", "Enum" };
            nodes = types
                .Select(t => nodes.FirstOrDefault(n => n.Labels.Contains(t)))
                .Where(n => n != null)
                .ToList()!;
        }

        Console.WriteLine($"Found {nodes.Count} nodes to process.");

        if (nodes.Count == 0)
        {
            Console.WriteLine("Nothing to do.");
            return changedNames;
        }

        var sw = Stopwatch.StartNew();
        var completed = 0;
        var failed = 0;
        var total = nodes.Count;
        var barWidth = 30;

        if (sample)
        {
            foreach (var node in nodes)
            {
                var type = node.Labels.FirstOrDefault(l => l is "Class" or "Interface" or "Method" or "Enum") ?? "?";
                Console.WriteLine($"\n{"=== " + type + ": " + node.FullName + " " + new string('=', 40)}");
                Console.WriteLine($"PROMPT:\n{node.Prompt[..Math.Min(node.Prompt.Length, 500)]}");
                if (node.Prompt.Length > 500) Console.WriteLine("  ...(truncated)");
                Console.WriteLine();

                try
                {
                    var (summary, searchText, tags, embedding) = await ProcessNode(ollama, summarize, node);
                    Console.WriteLine($"SUMMARY:\n{summary}");
                    Console.WriteLine($"SEARCH TEXT: {searchText}");
                    Console.WriteLine($"TAGS: {(tags.Length > 0 ? string.Join(", ", tags) : "(none)")}");
                    Console.WriteLine($"\nEMBEDDING: [{embedding.Length} dimensions] [{embedding[0]:F4}, {embedding[1]:F4}, {embedding[2]:F4}, ...]");
                    await neo4j.SetEmbeddingsBatchAsync([(node.FullName, summary, searchText, tags, embedding, node.ContentHash)], fieldPrefix);
                    changedNames.Add(node.FullName);
                    completed++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"FAILED: {ex.Message}");
                    failed++;
                }
            }
        }
        else
        {
            var semaphore = new SemaphoreSlim(2);
            var tasks = nodes.Select(async node =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var (summary, searchText, tags, embedding) = await ProcessNode(ollama, summarize, node);
                    await neo4j.SetEmbeddingsBatchAsync([(node.FullName, summary, searchText, tags, embedding, node.ContentHash)], fieldPrefix);
                    var count = Interlocked.Increment(ref completed);

                    lock (changedNames)
                    {
                        changedNames.Add(node.FullName);
                        ProgressHelper.WriteProgress(count, total, sw.Elapsed, barWidth, node.FullName);
                    }
                }
                catch
                {
                    Interlocked.Increment(ref completed);
                    Interlocked.Increment(ref failed);
                    lock (changedNames)
                    {
                        ProgressHelper.WriteProgress(completed, total, sw.Elapsed, barWidth, $"FAILED: {node.FullName}");
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            await Task.WhenAll(tasks);
            try { Console.Write("\r" + new string(' ', Console.WindowWidth - 1) + "\r"); } catch { Console.WriteLine(); }
        }

        if (failed > 0) Console.WriteLine($"\n{failed} nodes failed.");

        Console.WriteLine($"Done! Embedded {completed - failed}/{total} nodes in {sw.Elapsed:mm\\:ss}.");
        return changedNames;
    }

    public static async Task EmbedNamespacesAsync(
        Neo4jService neo4j, OllamaService ollama,
        Func<string, Task<OllamaService.SummaryResult>> summarize, ProviderConfig config)
    {
        Console.WriteLine("\n=== Pass 3: Namespace summaries ===");
        var namespaces = await neo4j.GetNamespaceSummariesAsync(config.FieldPrefix);
        Console.WriteLine($"Found {namespaces.Count} namespaces with summaries.");

        foreach (var (ns, memberSummaries) in namespaces)
        {
            var truncatedMembers = memberSummaries.Take(config.MaxNamespaceMembers).ToList();
            var membersText = string.Join("\n- ", truncatedMembers);
            var prompt = $"""
                You are a senior .NET engineer. Given these components in namespace {ns}, describe the namespace's responsibility. Be concise but thorough — cover the key concerns this namespace addresses and how its components collaborate.
                Do NOT restate the namespace name. Focus on what concerns this namespace addresses.

                Components:
                - {membersText}
                """;

            try
            {
                var result = await summarize(prompt);
                var embedding = await ollama.EmbedDocumentAsync(result.Docstring);
                await neo4j.StoreNamespaceSummaryAsync(ns, result.Docstring, embedding, config.FieldPrefix);
                Console.WriteLine($"  {ns}: {result.Docstring[..Math.Min(result.Docstring.Length, 80)]}...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  {ns}: FAILED - {ex.Message}");
            }
        }
    }

    public static async Task RunBatchEmbed(
        Neo4jService neo4j, OllamaService ollama, ClaudeService claude, EmbedSettings settings)
    {
        var allNodes = new List<Neo4jService.EmbeddableNode>();

        if (settings.RunPass1)
        {
            var leafNodes = await neo4j.GetLeafNodesForSummarizationAsync(
                settings.OnlyChanged, settings.Config.MaxSourceLength, settings.Config.FieldPrefix);
            allNodes.AddRange(leafNodes.Select(n =>
                new Neo4jService.EmbeddableNode(n.FullName, n.Prompt, n.Labels, n.ContentHash)));
        }

        if (settings.RunPass2)
        {
            var contextNodes = await neo4j.GetContextualNodesForSummarizationAsync(
                settings.OnlyChanged, settings.Config.MaxContextChars, settings.Config.MaxSourceLength, settings.Config.FieldPrefix);
            allNodes.AddRange(contextNodes.Select(n =>
                new Neo4jService.EmbeddableNode(n.FullName, n.Prompt, n.Labels, n.ContentHash)));
        }

        if (settings.Limit.HasValue) allNodes = allNodes.Take(settings.Limit.Value).ToList();

        var templateNodes = allNodes.Where(n => n.Prompt.StartsWith("__TEMPLATE__")).ToList();
        var llmNodes = allNodes.Where(n => !n.Prompt.StartsWith("__TEMPLATE__")).ToList();

        Console.WriteLine($"Total nodes: {allNodes.Count} ({templateNodes.Count} templates, {llmNodes.Count} need LLM)");

        foreach (var node in templateNodes)
        {
            var parts = node.Prompt["__TEMPLATE__".Length..].Split("||", 2);
            var summary = parts[0];
            var tags = parts.Length > 1 ? parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
            var embedding = await ollama.EmbedDocumentAsync(summary);
            await neo4j.SetEmbeddingsBatchAsync([(node.FullName, summary, summary, tags, embedding, node.ContentHash)], settings.Config.FieldPrefix);
        }
        if (templateNodes.Count > 0)
            Console.WriteLine($"Processed {templateNodes.Count} template nodes locally.");

        if (llmNodes.Count > 0)
        {
            var batchItems = llmNodes.Select(n => (n.FullName, n.Prompt)).ToList();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var (batchId, idMap) = await claude.SubmitBatchAsync(batchItems);

            var results = await claude.WaitForBatchAsync(batchId, idMap);
            Console.WriteLine($"Batch completed in {sw.Elapsed:mm\\:ss}. Got {results.Count}/{llmNodes.Count} results.");

            var stored = 0;
            foreach (var node in llmNodes)
            {
                if (!results.TryGetValue(node.FullName, out var result)) continue;
                var textToEmbed = result.SearchText ?? result.Docstring;
                var embedding = await ollama.EmbedDocumentAsync(textToEmbed);
                await neo4j.SetEmbeddingsBatchAsync([(node.FullName, result.Docstring, result.SearchText, result.Tags, embedding, node.ContentHash)], settings.Config.FieldPrefix);
                stored++;
                if (stored % 50 == 0)
                    Console.WriteLine($"  Stored {stored}/{results.Count} embeddings...");
            }
            Console.WriteLine($"Stored {stored} embeddings.");
        }

        if (settings.RunPass3)
            await EmbedNamespacesAsync(neo4j, ollama, claude.GenerateSummaryAsync, settings.Config);
    }
}
