using System.CommandLine;
using System.CommandLine.Parsing;

namespace GraphRagCli.Commands;

public static class ReembedCommand
{
    static readonly Option<Provider> s_provider = new("--provider") { Description = "Which summaries to re-embed", DefaultValueFactory = _ => Provider.Ollama };

    public static Command Build()
    {
        var command = new Command("reembed", "Re-embed existing summaries with new embedding model (no LLM calls)");
        command.Add(s_provider);
        GlobalOptions.AddAllOptions(command);

        command.SetAction(async (parseResult, _) => await ExecuteAsync(parseResult));
        return command;
    }

    static async Task ExecuteAsync(ParseResult parseResult)
    {
        var providerVal = parseResult.GetValue(s_provider);
        var conn = GlobalOptions.Parse(parseResult);
        var prefix = providerVal == Provider.Claude ? "claude_" : "";

        Console.WriteLine("GraphRagCli - Reembed (no LLM calls, embedding only)");
        Console.WriteLine($"  Neo4j:      {conn.Neo4jUri}");
        Console.WriteLine($"  Ollama:     {conn.OllamaUrl}");
        Console.WriteLine($"  Provider:   {providerVal} (fields: {prefix}summary, {prefix}embedding)");
        Console.WriteLine();

        try
        {
            await using var neo4j = await GlobalOptions.ConnectNeo4jAsync(conn);
            if (neo4j == null) return;

            await neo4j.InitializeVectorSchemaAsync();
            var ollama = new OllamaService(conn.OllamaUrl);

            await ReembedNodesAsync(neo4j, ollama, prefix);
            await ReembedNamespacesAsync(neo4j, ollama, prefix);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    static async Task ReembedNodesAsync(Neo4jService neo4j, OllamaService ollama, string prefix)
    {
        var nodes = await neo4j.GetNodesWithSummariesAsync(prefix);
        Console.WriteLine($"Found {nodes.Count} nodes with existing summaries to re-embed.");

        if (nodes.Count == 0)
        {
            Console.WriteLine("Nothing to do.");
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var completed = 0;
        var semaphore = new SemaphoreSlim(4);

        var tasks = nodes.Select(async node =>
        {
            await semaphore.WaitAsync();
            try
            {
                var textToEmbed = node.SearchText ?? node.Summary;
                var embedding = await ollama.EmbedDocumentAsync(textToEmbed);
                await neo4j.SetEmbeddingsBatchAsync([(node.FullName, node.Summary, node.SearchText, node.Tags, embedding, node.ContentHash)], prefix);
                var count = Interlocked.Increment(ref completed);
                if (count % 25 == 0 || count == nodes.Count)
                    Console.WriteLine($"  {count}/{nodes.Count} embedded ({sw.Elapsed:mm\\:ss})");
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);
        Console.WriteLine($"\nDone! Re-embedded {completed}/{nodes.Count} nodes in {sw.Elapsed:mm\\:ss}.");
    }

    static async Task ReembedNamespacesAsync(Neo4jService neo4j, OllamaService ollama, string prefix)
    {
        var namespaces = await neo4j.GetNamespaceNodesWithSummariesAsync(prefix);
        if (namespaces.Count == 0) return;

        Console.WriteLine($"\nRe-embedding {namespaces.Count} namespace summaries...");
        foreach (var (ns, summary) in namespaces)
        {
            var embedding = await ollama.EmbedDocumentAsync(summary);
            await neo4j.StoreNamespaceSummaryAsync(ns, summary, embedding, prefix);
        }
        Console.WriteLine("Namespace summaries re-embedded.");
    }
}
