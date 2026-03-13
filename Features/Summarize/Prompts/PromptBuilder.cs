using GraphRagCli.Shared;
using GraphRagCli.Shared.Ai;

namespace GraphRagCli.Features.Summarize.Prompts;

public class PromptBuilder : IPromptBuilder
{
    public List<EmbeddableNode> BuildPrompts(List<ReadyNodeData> nodes, SummarizeModelConfig config) =>
        nodes.Select(n => BuildPrompt(n, config)).ToList();

    private static EmbeddableNode BuildPrompt(ReadyNodeData node, SummarizeModelConfig config)
    {
        var nodeType = GetNodeType(node);
        var instruction = GetInstruction(nodeType, node.Labels.Contains(NodeLabels.EntryPoint), config.SearchTextStrategy);
        var content = BuildContent(node, nodeType);

        var prompt = Truncate($"""
            {instruction}

            {content}
            """, config.MaxPromptChars);

        return new EmbeddableNode(node.ElementId, node.FullName, prompt, node.Labels);
    }

    private static readonly string[] KnownTypes =
        [NodeLabels.Method, NodeLabels.Interface, NodeLabels.Enum, NodeLabels.Class,
         NodeLabels.Namespace, NodeLabels.Project, NodeLabels.Solution];

    private static string GetNodeType(ReadyNodeData node) =>
        node.Labels.FirstOrDefault(KnownTypes.Contains) ?? "Unknown";

    private static string GetInstruction(string nodeType, bool isEntryPoint, SearchTextStrategy searchTextStrategy)
    {
        var frontLoad = searchTextStrategy == SearchTextStrategy.FirstTwoSentences;
        var tags = "DATABASE, API, CONFIGURATION, UTILITY, PRODUCER, CONSUMER, EXTERNAL_SERVICE, DI_REGISTRATION, PIPELINE, MAPPING, VALIDATION, MESSAGING, CACHING, LOGGING, SERIALIZATION, AUTH, TESTING";

        var searchTextRule = frontLoad
            ? "- Start with 1-2 keyword-dense sentences covering what it does, technologies used, and problem solved. Then elaborate freely."
            : "";

        return nodeType switch
        {
            NodeLabels.Method when isEntryPoint =>
                $"""
                Analyze this C# DI/hosting registration method for a code intelligence graph.
                Explain what subsystem it wires up, what services and interfaces it registers, and what configuration it applies.

                Rules:
                - Lead with the business purpose, not the class/method name
                - Never start with "This method...", "This class...", "The `Foo`..."
                - Describe behavior and data flow, not structure
                - Be concise: 2-4 sentences max
                {searchTextRule}
                - Assign 1-3 tags: {tags}
                """,
            NodeLabels.Method =>
                $"""
                Analyze this C# method for a code intelligence graph.
                Explain the business problem it solves, what data flows in and out, and what decisions or transformations it performs. Focus on WHY it exists, not just WHAT it does.

                Rules:
                - Lead with the business purpose, not the class/method name
                - Never start with "This method...", "This class...", "The `Foo`..."
                - Describe behavior and data flow, not structure
                - Be concise: 2-4 sentences max
                {searchTextRule}
                - Assign 1-3 tags: {tags}
                """,
            NodeLabels.Class =>
                $"""
                Analyze this C# class for a code intelligence graph.
                Explain what business problem it solves, what data flows through it, and what key decisions it makes. Mention the orchestration pattern if it coordinates multiple collaborators.

                Rules:
                - Lead with the business purpose, not the class/method name
                - Never start with "This method...", "This class...", "The `Foo`..."
                - Describe behavior and data flow, not structure
                - Be concise: 2-4 sentences max
                {searchTextRule}
                - Assign 1-3 tags: {tags}
                """,
            NodeLabels.Interface =>
                $"""
                Analyze this C# interface for a code intelligence graph.
                Explain what capability it abstracts and why that abstraction boundary exists. What can implementations vary?

                Rules:
                - Lead with the business purpose, not the class/method name
                - Never start with "This method...", "This class...", "The `Foo`..."
                - Describe behavior and data flow, not structure
                - Be concise: 2-4 sentences max
                {searchTextRule}
                - Assign 1-3 tags: {tags}
                """,
            NodeLabels.Enum =>
                $"""
                Analyze this C# enum for a code intelligence graph.
                Explain the domain concept it models. List all members with brief explanations. Mention how consumers use these values to drive behavior.

                Rules:
                - Lead with the business purpose, not the class/method name
                - Never start with "This method...", "This class...", "The `Foo`..."
                - Describe behavior and data flow, not structure
                - Be concise: 2-4 sentences max
                {searchTextRule}
                - Assign 1-3 tags: {tags}
                """,
            NodeLabels.Namespace =>
                $"""
                Summarize this namespace for a code intelligence graph.
                Given the components and their summaries, explain:
                - What business capability does this namespace provide?
                - What is the key data flow or processing pipeline?
                - How do the components collaborate to deliver that capability?

                Rules:
                - Lead with the business purpose, not "This namespace..."
                - Describe behavior and data flow, not list classes
                - 3-5 sentences max
                {searchTextRule}
                """,
            NodeLabels.Project =>
                $"""
                Summarize this project for a code intelligence graph.
                Given the namespaces and their summaries, explain:
                - What is this project's core purpose?
                - What are the main workflows or pipelines?
                - How do the namespaces layer together?

                Rules:
                - Lead with the business purpose
                - Describe the architecture and key data flows
                - 3-5 sentences max
                {searchTextRule}
                """,
            NodeLabels.Solution =>
                """
                Write a 1-2 sentence elevator pitch for this solution.
                This will be used by an LLM to decide whether to search this codebase. Focus on: what domain it serves, what it does, and what makes it distinctive.
                """,
            _ => "Explain the purpose and architectural role of this code."
        };
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
