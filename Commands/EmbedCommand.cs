using System.CommandLine;
using System.CommandLine.Parsing;
using GraphRagCli.Helpers;

namespace GraphRagCli.Commands;

public static class EmbedCommand
{
    static readonly Option<Provider> s_provider = new("--provider") { Description = "Summary provider", DefaultValueFactory = _ => Provider.Ollama };
    static readonly Option<string?> s_model = new("--model") { Description = "Summary model (default: qwen2.5-coder:7b for Ollama, claude-haiku-4-5-20251001 for Claude)" };
    static readonly Option<bool> s_force = new("--force") { Description = "Re-embed all nodes, not just missing/changed" };
    static readonly Option<int?> s_limit = new("--limit") { Description = "Only process first N nodes (for testing)" };
    static readonly Option<bool> s_sample = new("--sample") { Description = "Test with 1 node per type (Class, Interface, Method, Enum)" };
    static readonly Option<bool> s_batch = new("--batch") { Description = "Use Claude Batch API (50% cheaper, async processing)" };
    static readonly Option<int?> s_pass = new("--pass") { Description = "Run only pass 1 (leaf), 2 (contextual), 3 (namespace), 4 (project), or 5 (solution). Default: all" };

    public static Command Build()
    {
        var command = new Command("embed", "Generate summaries and embeddings for code graph nodes");
        command.Add(s_provider);
        command.Add(s_model);
        command.Add(s_force);
        command.Add(s_limit);
        command.Add(s_sample);
        command.Add(s_batch);
        command.Add(s_pass);
        GlobalOptions.AddAllOptions(command);

        command.Validators.Add(result =>
        {
            var passVal = result.GetValue(s_pass);
            if (passVal.HasValue && passVal.Value is not (1 or 2 or 3 or 4 or 5))
                result.AddError("--pass must be 1, 2, 3, or 4");

            var batchVal = result.GetValue(s_batch);
            var providerVal = result.GetValue(s_provider);
            if (batchVal && providerVal == Provider.Ollama)
                result.AddError("--batch is only supported with --provider Claude");
        });

        command.SetAction(async (parseResult, _) => await ExecuteAsync(parseResult));
        return command;
    }

    static async Task ExecuteAsync(ParseResult parseResult)
    {
        var providerVal = parseResult.GetValue(s_provider);
        var resolvedModel = parseResult.GetValue(s_model)
            ?? (providerVal == Provider.Claude ? "claude-haiku-4-5-20251001" : "qwen2.5-coder:7b");
        var passVal = parseResult.GetValue(s_pass);
        var conn = GlobalOptions.Parse(parseResult);
        var config = ProviderConfig.For(providerVal);

        var settings = new EmbedSettings(
            OnlyChanged: !parseResult.GetValue(s_force),
            Limit: parseResult.GetValue(s_limit),
            Sample: parseResult.GetValue(s_sample),
            RunPass1: passVal is null or 1,
            RunPass2: passVal is null or 2,
            RunPass3: passVal is null or 3,
            RunPass4: passVal is null or 4,
            RunPass5: passVal is null or 5,
            Config: config);

        var isBatch = parseResult.GetValue(s_batch) && providerVal == Provider.Claude;

        PrintBanner(conn, providerVal, resolvedModel, settings, passVal);

        try
        {
            await using var neo4j = await GlobalOptions.ConnectNeo4jAsync(conn);
            if (neo4j == null) return;

            await neo4j.InitializeVectorSchemaAsync();
            await neo4j.InitializeFulltextIndexAsync();

            var ollama = new OllamaService(conn.OllamaUrl, resolvedModel);
            var (summarize, claude) = CreateSummarizer(providerVal, resolvedModel, ollama);

            if (isBatch)
            {
                Console.WriteLine("\n=== Batch Mode: collecting all prompts ===");
                await EmbeddingHelper.RunBatchEmbed(neo4j, ollama, claude!, settings);
            }
            else
            {
                await RunInteractiveEmbed(neo4j, ollama, summarize, settings);
            }

            await ComputeCentrality(neo4j);
            PrintClaudeUsageReport(claude, resolvedModel, isBatch);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    static void PrintBanner(ConnectionInfo conn, Provider provider, string model, EmbedSettings settings, int? passVal)
    {
        Console.WriteLine("GraphRagCli - Embed");
        Console.WriteLine($"  Neo4j:      {conn.Neo4jUri}");
        Console.WriteLine($"  Provider:   {provider}");
        if (provider == Provider.Ollama) Console.WriteLine($"  Ollama:     {conn.OllamaUrl}");
        Console.WriteLine($"  Model:      {model}");
        Console.WriteLine($"  Force:      {!settings.OnlyChanged}");
        Console.WriteLine($"  Pass:       {passVal?.ToString() ?? "all"}");
        if (settings.Limit.HasValue) Console.WriteLine($"  Limit:      {settings.Limit}");
        if (settings.Sample) Console.WriteLine($"  Sample:     1 per type");
        Console.WriteLine();
    }

    static (Func<string, Task<OllamaService.SummaryResult>> Summarize, ClaudeService? Claude) CreateSummarizer(
        Provider provider, string model, OllamaService ollama)
    {
        if (provider == Provider.Claude)
        {
            var claude = new ClaudeService(model);
            Console.WriteLine($"Using Claude API for summaries (model: {model})");
            return (claude.GenerateSummaryAsync, claude);
        }

        Console.WriteLine($"Using Ollama for summaries (model: {model})");
        return (prompt => ollama.GenerateSummaryAsync(prompt), null);
    }

    static async Task RunInteractiveEmbed(
        Neo4jService neo4j, OllamaService ollama,
        Func<string, Task<OllamaService.SummaryResult>> summarize, EmbedSettings settings)
    {
        var changedNames = settings.RunPass1
            ? await RunLeafPass(neo4j, ollama, summarize, settings)
            : [];

        if (settings.RunPass2)
            await RunContextualPass(neo4j, ollama, summarize, settings);

        if (settings.RunPass3)
            await EmbeddingHelper.EmbedNamespacesAsync(neo4j, ollama, summarize, settings.Config);

        if (settings.RunPass4)
            await EmbeddingHelper.EmbedProjectsAsync(neo4j, ollama, summarize, settings.Config);

        if (settings.RunPass5)
            await EmbeddingHelper.EmbedSolutionAsync(neo4j, ollama, summarize, settings.Config);
    }

    static async Task<List<string>> RunLeafPass(
        Neo4jService neo4j, OllamaService ollama,
        Func<string, Task<OllamaService.SummaryResult>> summarize, EmbedSettings settings)
    {
        Console.WriteLine("\n=== Pass 1: Leaf nodes (Methods without CALLS, Enums) ===");
        var leafNodes = await neo4j.GetLeafNodesForSummarizationAsync(
            settings.OnlyChanged, settings.Config.MaxSourceLength);
        var embeddable = leafNodes.Select(n =>
            new Neo4jService.EmbeddableNode(n.ElementId, n.FullName, n.Prompt, n.Labels, n.ContentHash)).ToList();
        if (settings.Limit.HasValue) embeddable = embeddable.Take(settings.Limit.Value).ToList();

        var changed = await EmbeddingHelper.EmbedNodes(neo4j, ollama, summarize, embeddable, settings.Sample);

        if (changed.Count > 0)
        {
            var staleCount = await neo4j.MarkStaleDependentsAsync(changed);
            Console.WriteLine($"Marked {staleCount} dependents as stale for pass 2.");
        }

        return changed;
    }

    static async Task RunContextualPass(
        Neo4jService neo4j, OllamaService ollama,
        Func<string, Task<OllamaService.SummaryResult>> summarize, EmbedSettings settings)
    {
        Console.WriteLine("\n=== Pass 2: Contextual nodes (Methods with CALLS, Classes, Interfaces) ===");
        var tiers = await neo4j.GetPass2TiersAsync(settings.OnlyChanged);

        if (tiers.Count == 0)
        {
            Console.WriteLine("No Pass 2 nodes to process.");
            return;
        }

        foreach (var (tier, names) in tiers.OrderBy(t => t.Key))
        {
            Console.WriteLine($"\n--- Pass 2 tier {tier + 1}/{tiers.Count}: {names.Count} nodes ---");
            var contextNodes = await neo4j.GetContextualNodesForSummarizationAsync(
                settings.OnlyChanged, settings.Config.MaxContextChars, settings.Config.MaxSourceLength, filterNames: names);
            var embeddable = contextNodes.Select(n =>
                new Neo4jService.EmbeddableNode(n.ElementId, n.FullName, n.Prompt, n.Labels, n.ContentHash)).ToList();
            if (settings.Limit.HasValue) embeddable = embeddable.Take(settings.Limit.Value).ToList();

            await EmbeddingHelper.EmbedNodes(neo4j, ollama, summarize, embeddable, settings.Sample);
        }
    }

    static async Task ComputeCentrality(Neo4jService neo4j)
    {
        Console.WriteLine("\n=== Computing GDS Centrality ===");
        try
        {
            await neo4j.ComputeCentralityAsync();
            Console.WriteLine("Centrality scores computed.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"GDS centrality failed (is GDS plugin installed?): {ex.Message}");
        }
    }

    static void PrintClaudeUsageReport(ClaudeService? claude, string model, bool isBatch)
    {
        if (claude == null) return;

        Console.WriteLine();
        Console.WriteLine("=== Claude API Usage Report ===");
        Console.WriteLine($"  Input tokens:   {claude.TotalInputTokens:N0}");
        Console.WriteLine($"  Output tokens:  {claude.TotalOutputTokens:N0}");
        Console.WriteLine($"  Total tokens:   {claude.TotalInputTokens + claude.TotalOutputTokens:N0}");
        Console.WriteLine($"  Estimated cost: ${claude.EstimateCostUsd(isBatch):F4}");
        Console.WriteLine($"  Model:          {model}");
        if (isBatch) Console.WriteLine("  Mode:           batch (50% discount applied)");
    }
}
