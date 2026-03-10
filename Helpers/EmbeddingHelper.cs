using System.Diagnostics;

namespace GraphRagCli.Helpers;

public record EmbedSettings(
    bool OnlyChanged, int? Limit, bool Sample,
    bool RunPass1, bool RunPass2, bool RunPass3, bool RunPass4, bool RunPass5,
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
        var textToEmbed = result.SearchText ?? result.Summary;
        var emb = await ollama.EmbedDocumentAsync(textToEmbed);
        return (result.Summary, result.SearchText, result.Tags, emb);
    }

    public static async Task<List<string>> EmbedNodes(
        Neo4jService neo4j, OllamaService ollama, Func<string, Task<OllamaService.SummaryResult>> summarize,
        List<Neo4jService.EmbeddableNode> nodes, bool sample)
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
                    await neo4j.SetEmbeddingsBatchAsync([(node.ElementId, summary, searchText, tags, embedding, node.ContentHash)]);
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
                    await neo4j.SetEmbeddingsBatchAsync([(node.ElementId, summary, searchText, tags, embedding, node.ContentHash)]);
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

    public static async Task EmbedProjectsAsync(
        Neo4jService neo4j, OllamaService ollama,
        Func<string, Task<OllamaService.SummaryResult>> summarize, ProviderConfig config)
    {
        Console.WriteLine("\n=== Pass 4: Project summaries ===");
        var projects = await neo4j.GetProjectSummariesAsync();
        Console.WriteLine($"Found {projects.Count} projects with namespace summaries.");

        var nodes = projects.Select(p =>
        {
            var membersText = string.Join("\n- ", p.NamespaceSummaries);
            var prompt = $"""
                You are a senior .NET engineer. Given these namespaces in project {p.Project}, describe the project's purpose and architecture. Be concise but thorough — cover what this project does, its key concerns, and how its namespaces collaborate.
                Do NOT restate the project name. Focus on what this project provides.

                Namespaces:
                - {membersText}
                """;
            return new Neo4jService.EmbeddableNode(p.ElementId, p.Project, prompt, ["Project"], Neo4jService.ComputeHash(prompt));
        }).ToList();

        await EmbedNodes(neo4j, ollama, summarize, nodes, sample: false);
    }

    public static async Task EmbedSolutionAsync(
        Neo4jService neo4j, OllamaService ollama,
        Func<string, Task<OllamaService.SummaryResult>> summarize, ProviderConfig config)
    {
        Console.WriteLine("\n=== Pass 5: Solution summary ===");
        var solutions = await neo4j.GetSolutionSummariesAsync();

        if (solutions.Count == 0)
        {
            Console.WriteLine("No Solution nodes with project summaries found.");
            return;
        }

        var nodes = solutions.Select(s =>
        {
            var membersText = string.Join("\n- ", s.ProjectSummaries);
            var prompt = $"""
                You are a senior .NET engineer. Given these projects in solution {s.SolutionName}, write a concise 1-2 sentence description of what this solution/codebase is about. Focus on its purpose, domain, and key capabilities. This will be used as a quick reference for an LLM to decide whether to search this codebase.

                Projects:
                - {membersText}
                """;
            return new Neo4jService.EmbeddableNode(s.ElementId, s.SolutionName, prompt, ["Solution"], Neo4jService.ComputeHash(prompt));
        }).ToList();

        await EmbedNodes(neo4j, ollama, summarize, nodes, sample: false);
    }

    public static async Task EmbedNamespacesAsync(
        Neo4jService neo4j, OllamaService ollama,
        Func<string, Task<OllamaService.SummaryResult>> summarize, ProviderConfig config)
    {
        Console.WriteLine("\n=== Pass 3: Namespace summaries ===");
        var namespaces = await neo4j.GetNamespaceSummariesAsync();
        Console.WriteLine($"Found {namespaces.Count} namespaces with summaries.");

        var nodes = namespaces.Select(ns =>
        {
            var truncatedMembers = ns.MemberSummaries.Take(config.MaxNamespaceMembers).ToList();
            var membersText = string.Join("\n- ", truncatedMembers);
            var prompt = $"""
                You are a senior .NET engineer. Given these components in namespace {ns.Namespace}, describe the namespace's responsibility. Be concise but thorough — cover the key concerns this namespace addresses and how its components collaborate.
                Do NOT restate the namespace name. Focus on what concerns this namespace addresses.

                Components:
                - {membersText}
                """;
            return new Neo4jService.EmbeddableNode(ns.ElementId, ns.Namespace, prompt, ["Namespace"], Neo4jService.ComputeHash(prompt));
        }).ToList();

        await EmbedNodes(neo4j, ollama, summarize, nodes, sample: false);
    }

    public static async Task RunBatchEmbed(
        Neo4jService neo4j, OllamaService ollama, ClaudeService claude, EmbedSettings settings)
    {
        // Pass 1: single batch for leaf nodes
        if (settings.RunPass1)
        {
            Console.WriteLine("\n=== Batch Pass 1: Leaf nodes ===");
            var leafNodes = await neo4j.GetLeafNodesForSummarizationAsync(
                settings.OnlyChanged, settings.Config.MaxSourceLength);
            var pass1Nodes = leafNodes.Select(n =>
                new Neo4jService.EmbeddableNode(n.ElementId, n.FullName, n.Prompt, n.Labels, n.ContentHash)).ToList();
            if (settings.Limit.HasValue) pass1Nodes = pass1Nodes.Take(settings.Limit.Value).ToList();

            await RunBatchForNodes(neo4j, ollama, claude, pass1Nodes);
        }

        // Pass 2: one batch per tier
        if (settings.RunPass2)
        {
            Console.WriteLine("\n=== Batch Pass 2: Contextual nodes (tiered) ===");
            var tiers = await neo4j.GetPass2TiersAsync(settings.OnlyChanged);

            if (tiers.Count == 0)
            {
                Console.WriteLine("No Pass 2 nodes to process.");
            }
            else
            {
                foreach (var (tier, names) in tiers.OrderBy(t => t.Key))
                {
                    Console.WriteLine($"\n--- Batch tier {tier + 1}/{tiers.Count}: {names.Count} nodes ---");
                    var contextNodes = await neo4j.GetContextualNodesForSummarizationAsync(
                        settings.OnlyChanged, settings.Config.MaxContextChars, settings.Config.MaxSourceLength,
                        filterNames: names);
                    var tierNodes = contextNodes.Select(n =>
                        new Neo4jService.EmbeddableNode(n.ElementId, n.FullName, n.Prompt, n.Labels, n.ContentHash)).ToList();
                    if (settings.Limit.HasValue) tierNodes = tierNodes.Take(settings.Limit.Value).ToList();

                    await RunBatchForNodes(neo4j, ollama, claude, tierNodes);
                }
            }
        }

        if (settings.RunPass3)
            await EmbedNamespacesAsync(neo4j, ollama, claude.GenerateSummaryAsync, settings.Config);

        if (settings.RunPass4)
            await EmbedProjectsAsync(neo4j, ollama, claude.GenerateSummaryAsync, settings.Config);

        if (settings.RunPass5)
            await EmbedSolutionAsync(neo4j, ollama, claude.GenerateSummaryAsync, settings.Config);
    }

    private static async Task RunBatchForNodes(
        Neo4jService neo4j, OllamaService ollama, ClaudeService claude,
        List<Neo4jService.EmbeddableNode> allNodes)
    {
        var templateNodes = allNodes.Where(n => n.Prompt.StartsWith("__TEMPLATE__")).ToList();
        var llmNodes = allNodes.Where(n => !n.Prompt.StartsWith("__TEMPLATE__")).ToList();

        Console.WriteLine($"Total nodes: {allNodes.Count} ({templateNodes.Count} templates, {llmNodes.Count} need LLM)");

        foreach (var node in templateNodes)
        {
            var parts = node.Prompt["__TEMPLATE__".Length..].Split("||", 2);
            var summary = parts[0];
            var tags = parts.Length > 1 ? parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
            var embedding = await ollama.EmbedDocumentAsync(summary);
            await neo4j.SetEmbeddingsBatchAsync([(node.ElementId, summary, summary, tags, embedding, node.ContentHash)]);
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
                var textToEmbed = result.SearchText ?? result.Summary;
                var embedding = await ollama.EmbedDocumentAsync(textToEmbed);
                await neo4j.SetEmbeddingsBatchAsync([(node.ElementId, result.Summary, result.SearchText, result.Tags, embedding, node.ContentHash)]);
                stored++;
                if (stored % 50 == 0)
                    Console.WriteLine($"  Stored {stored}/{results.Count} embeddings...");
            }
            Console.WriteLine($"Stored {stored} embeddings.");
        }
    }
}
