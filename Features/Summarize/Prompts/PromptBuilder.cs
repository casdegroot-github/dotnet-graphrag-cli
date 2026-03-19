using System.Text;
using GraphRagCli.Shared;
using GraphRagCli.Shared.Ai;

namespace GraphRagCli.Features.Summarize.Prompts;

public class PromptBuilder : IPromptBuilder
{
    public List<EmbeddableNode> BuildPrompts(
        List<IGraphNode> nodes,
        SummarizeModelConfig config,
        string? customPrompt = null) =>
        nodes.Select(n => BuildPrompt(n, config, customPrompt)).ToList();

    public EmbeddableNode BuildPrompt(
        IGraphNode node,
        SummarizeModelConfig config,
        string? customPrompt)
    {
        var nodeType = node.Labels.FirstOrDefault(NodeType.All.Contains) ?? "Unknown";
        var content = BuildContent(node);
        var instruction = customPrompt ?? BuildInstruction(nodeType, config.SearchTextStrategy);

        return new EmbeddableNode(node.Id.Value, node.FullName, $"{content}\n\n{instruction}", node.Labels);
    }

    public static string BuildContentText(IGraphNode node) => BuildContent(node);

    private static string BuildContent(IGraphNode node)
    {
        var content = node switch
        {
            MethodNode m => BuildMethodContent(m),
            ClassNode c => BuildClassContent(c),
            InterfaceNode i => BuildInterfaceContent(i),
            EnumNode e => BuildEnumContent(e),
            NamespaceNode ns => BuildNamespaceContent(ns),
            ProjectNode p => BuildProjectContent(p),
            PackageNode pkg => BuildPackageContent(pkg),
            SolutionNode s => BuildSolutionContent(s),
            _ => throw new InvalidOperationException($"Unhandled node type: {node.GetType().Name}")
        };

        return $"<CONTEXT>\n{content}\n</CONTEXT>";
    }

    private static string BuildMethodContent(MethodNode node)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"```csharp\n{node.ReturnType ?? "void"} {node.FullName}({node.Parameters ?? ""})\n{node.SourceText}\n```");
        AppendRelated(sb, "Calls", node.Calls);
        AppendRelated(sb, "Called by", node.CalledBy);
        AppendRelated(sb, "Implements", node.Implements);
        AppendRelated(sb, "References", node.References);
        AppendRelated(sb, "Registers", node.Registers);
        AppendRelated(sb, "Extends", node.Extends);
        return sb.ToString();
    }

    private static string BuildClassContent(ClassNode node)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"```csharp\n{node.SourceText ?? node.FullName}\n```");
        AppendRelated(sb, null, node.Members);
        AppendRelated(sb, "Implements", node.Implements);
        AppendRelated(sb, "Referenced by", node.ReferencedBy);
        AppendRelated(sb, "Registered by", node.RegisteredBy);
        AppendRelated(sb, "Extended by", node.ExtendedBy);
        return sb.ToString();
    }

    private static string BuildInterfaceContent(InterfaceNode node)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"```csharp\n{node.SourceText ?? node.FullName}\n```");
        AppendRelated(sb, null, node.Members);
        AppendRelated(sb, "Implemented by", node.ImplementedBy);
        AppendRelated(sb, "Referenced by", node.ReferencedBy);
        AppendRelated(sb, "Registered by", node.RegisteredBy);
        return sb.ToString();
    }

    private static string BuildEnumContent(EnumNode node)
    {
        var sb = new StringBuilder();
        var code = node.SourceText ?? $"enum {node.FullName} {{ {node.Members ?? ""} }}";
        sb.AppendLine($"```csharp\n{code}\n```");
        AppendRelated(sb, "Referenced by", node.ReferencedBy);
        return sb.ToString();
    }

    private static string BuildNamespaceContent(NamespaceNode node)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{node.FullName}\n\nComponents:");
        AppendRelated(sb, null, node.Types);
        return sb.ToString();
    }

    private static string BuildProjectContent(ProjectNode node)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{node.FullName}\n\nNamespaces:");
        AppendRelated(sb, null, node.Namespaces);
        return sb.ToString();
    }

    private static string BuildPackageContent(PackageNode node)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{node.FullName}\n\nProjects:");
        AppendRelated(sb, null, node.Projects);
        return sb.ToString();
    }

    private static string BuildSolutionContent(SolutionNode node)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{node.FullName}\n\nProjects:");
        AppendRelated(sb, null, node.Projects);
        return sb.ToString();
    }

    private static void AppendRelated(StringBuilder sb, string? label, List<RelatedNode> nodes)
    {
        if (nodes.Count == 0) return;
        foreach (var n in nodes)
        {
            var prefix = label != null ? $"{label}: " : "";
            var summary = n.Summary != null ? $": {n.Summary}" : "";
            sb.AppendLine($"- {prefix}{n.FullName}{summary}");
        }
    }

    // --- Per-type instructions ---

    private static string BuildInstruction(string nodeType, SearchTextStrategy searchTextStrategy)
    {
        var typeInstruction = GetInstruction(nodeType);

        var searchTextRule = searchTextStrategy == SearchTextStrategy.FirstTwoSentences
            ? "\n- Start with 1-2 keyword-dense sentences covering what it does, technologies used, and problem solved. Then elaborate freely."
            : "";

        return $"""
            <ROLE>
            You are an expert software architect analyzing a C# codebase for a code intelligence graph.
            </ROLE>

            <INSTRUCTIONS>
            {typeInstruction}

            Rules:
            - Be specific and direct — no filler, but don't omit important details either.
            - Use literal values (e.g., header names like "X-Api-Key", specific URLs, or scheme names) found in the code or child summaries.{searchTextRule}
            - Mention non-obvious behavior, gotchas, or important design trade-offs when present in the code.
            - Assign 1-3 short UPPERCASE tags describing this component's role in the system (e.g., DATABASE, CLI, SEARCH, AUTH, MIDDLEWARE — whatever fits the code).
            - Only include information directly supported by the provided source text or child summaries. If you cannot determine the purpose from the provided context, say so — do not invent.
            </INSTRUCTIONS>

            <CONSTRAINTS>
            NEVER:
            - Restate the name as the description ("AuthService handles authentication")
            - Use filler: "comprehensive", "facilitating", "enabling", "establishing", "It is important to note", "In order to", "This class provides", "responsible for"
            - Open with "This class...", "This method...", "This interface...", "Defines...", or "The `Foo` handles..."
            - Describe syntax ("takes a string parameter and returns a bool")
            - List all members/methods — focus on what matters
            - Write more than 2 sentences for boilerplate code (DTOs, constants, simple mappings, DI registration)
            </CONSTRAINTS>
            """;
    }

    private static string GetInstruction(string nodeType) => nodeType switch
    {
        NodeType.Method => MethodInstruction,
        NodeType.Enum => EnumInstruction,
        NodeType.Class => ClassInstruction,
        NodeType.Interface => InterfaceInstruction,
        NodeType.Namespace => NamespaceInstruction,
        NodeType.Project => ProjectInstruction,
        NodeType.Package => PackageInstruction,
        NodeType.Solution => SolutionInstruction,
        _ => DefaultInstruction
    };

    private const string MethodInstruction = """
        Focus on:
        - What does this method DO concretely? What problem does it solve?
        - Key implementation details: algorithm, pattern, or technology used
        - What data flows in and out? What side effects does it have?
        - If it calls other methods in context: how does it orchestrate them?
        - If boilerplate (getter, DI registration, simple mapping): say so in one sentence
        """;

    private const string EnumInstruction = """
        Focus on:
        - What domain concept does this enum represent?
        - How are the values used? (flags, state machine, lookup keys?)
        - If simple (2-3 obvious values): one sentence, no elaboration
        """;

    private const string ClassInstruction = """
        Focus on:
        - What is this class's unique responsibility?
        - How does it collaborate with its dependencies? (trace data flow through injected services)
        - What's the primary usage pattern? (how do consumers call this?)
        - If it implements an interface: what specific behavior does THIS implementation add?
        - Key design decisions visible in the code (patterns, error handling, concurrency)
        """;

    private const string InterfaceInstruction = """
        Focus on:
        - What contract does this interface define and why does it exist?
        - What are the key behavioral expectations on implementors?
        - If it has implementors in context: how do they differ?
        - What design pattern does this abstraction enable? (strategy, repository, decorator, etc.)
        """;

    private const string NamespaceInstruction = """
        Focus on:
        - Synthesize children into a cohesive narrative — DO NOT list children individually
        - What is the overall purpose of this area?
        - How do the children collaborate to achieve that purpose?
        - What are the key data flows or request paths through this area?
        - What architectural decisions are visible in the structure?
        """;

    private const string ProjectInstruction = """
        Focus on:
        - Synthesize namespaces into a cohesive narrative — DO NOT list namespaces individually
        - What is the overall purpose of this project?
        - What are the key data flows or request paths through this project?
        - What architectural decisions are visible in the structure?
        - What external systems or protocols does it integrate with?
        """;

    private const string PackageInstruction = """
        Focus on:
        - Synthesize projects into a cohesive narrative — DO NOT list projects individually
        - What is the overall purpose of this package?
        - What are the key data flows or request paths through this area?
        - What architectural decisions are visible in the structure?
        """;

    private const string SolutionInstruction = """
        Focus on:
        - Synthesize projects into a cohesive narrative — DO NOT list projects individually
        - What is the overall purpose of this solution?
        - What are the key data flows or request paths end-to-end?
        - What architectural decisions are visible in the structure?
        - What external systems or protocols does it integrate with?
        """;

    private const string DefaultInstruction = """
        Focus on:
        - What is the overall purpose of this component?
        - How does it collaborate with its dependencies?
        """;
}
