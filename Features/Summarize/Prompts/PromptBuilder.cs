using GraphRagCli.Shared;
using GraphRagCli.Shared.Ai;

namespace GraphRagCli.Features.Summarize.Prompts;

public class PromptBuilder : IPromptBuilder
{
    public List<EmbeddableNode> BuildPrompts(List<ReadyNodeData> nodes, SummarizeModelConfig config, string? customPrompt = null) =>
        nodes.Select(n => BuildPrompt(n, config, customPrompt)).ToList();

    private static EmbeddableNode BuildPrompt(ReadyNodeData node, SummarizeModelConfig config, string? customPrompt)
    {
        var nodeType = GetNodeType(node);
        var instruction = customPrompt ?? GetInstruction(config.SearchTextStrategy);
        var content = BuildContent(node, nodeType);

        var fullPrompt = $"""
            {instruction}

            {content}
            """;

        var prompt = Truncate(fullPrompt, config.MaxPromptChars);

        if (prompt.Length < fullPrompt.Length)
            Console.WriteLine($"  Warning: truncated {node.FullName} ({fullPrompt.Length:N0} → {config.MaxPromptChars:N0} chars, {fullPrompt.Length - config.MaxPromptChars:N0} chars lost)");

        return new EmbeddableNode(node.ElementId, node.FullName, prompt, node.Labels);
    }

    private static readonly string[] KnownTypes =
        [NodeLabels.Method, NodeLabels.Interface, NodeLabels.Enum, NodeLabels.Class,
         NodeLabels.Namespace, NodeLabels.Project, NodeLabels.Solution];

    private static string GetNodeType(ReadyNodeData node) =>
        node.Labels.FirstOrDefault(KnownTypes.Contains) ?? "Unknown";

    private static string GetInstruction(SearchTextStrategy searchTextStrategy)
    {
        var frontLoad = searchTextStrategy == SearchTextStrategy.FirstTwoSentences;
        var tags = "DATABASE, API, CONFIGURATION, UTILITY, PRODUCER, CONSUMER, EXTERNAL_SERVICE, DI_REGISTRATION, PIPELINE, MAPPING, VALIDATION, MESSAGING, CACHING, LOGGING, SERIALIZATION, AUTH, TESTING";

        var searchTextRule = frontLoad
            ? "- Start with 1-2 keyword-dense sentences covering what it does, technologies used, and problem solved. Then elaborate freely."
            : "";

        return $"""
            Analyze this C# code for a code intelligence graph.

            Start with a 2-sentence summary that captures what it does, the technologies used, and the problem solved. Then explain clearly and in detail:
            - How does the logic flow from start to end?
            - What are the key algorithms, data structures, or patterns used?
            - How do the main components or methods interact?
            - Note any non-obvious behavior, edge cases, or important design decisions.

            Rules:
            - Lead with the business purpose, not the code element name
            - Never start with "This method...", "This class...", "The `Foo`..."
            - Describe behavior and data flow, not structure
            {searchTextRule}
            - Assign 1-3 tags: {tags}
            """;
    }

    private static string BuildContent(ReadyNodeData node, string nodeType)
    {
        var children = BuildChildSummariesText(node.Children);
        return nodeType switch
        {
            NodeLabels.Method => $"```csharp\n{node.ReturnType ?? "void"} {node.FullName}({node.Parameters ?? ""})\n{node.SourceText}\n```{children}",
            NodeLabels.Enum => $"```csharp\nenum {node.FullName} {{ {node.Members ?? ""} }}\n```{children}",
            NodeLabels.Class or NodeLabels.Interface => $"```csharp\n{node.SourceText ?? node.Name}\n```{children}",
            NodeLabels.Namespace => $"{node.FullName}\n\nComponents:\n{children}",
            NodeLabels.Project => $"{node.FullName}\n\nNamespaces:\n{children}",
            NodeLabels.Solution => $"{node.FullName}\n\nProjects:\n{children}",
            _ => node.SourceText ?? node.FullName
        };
    }

    private static string Truncate(string text, int maxChars) =>
        maxChars > 0 && text.Length > maxChars ? text[..maxChars] : text;

    private static string BuildChildSummariesText(List<ChildData> children)
    {
        var items = children.Where(c => c.Summary != null).ToList();
        if (items.Count == 0) return "";
        return "\n- " + string.Join("\n- ", items.Select(c => $"{c.FullName}: {c.Summary}"));
    }

}
