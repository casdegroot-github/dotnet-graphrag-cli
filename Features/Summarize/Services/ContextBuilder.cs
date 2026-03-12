namespace GraphRagCli.Features.Summarize.Services;

public class ContextBuilder : IContextBuilder
{
    public EmbeddableNode BuildEmbeddableNode(RawNodeData raw, string? contextSuffix, int maxSourceLength)
    {
        var (codeBlock, nodeType, members) = BuildCodeBlock(raw, maxSourceLength);

        var smallSummary = PromptBuilder.TrySmallNodeSummary(nodeType, raw.Name, raw.FullName, raw.SourceText, members);
        if (smallSummary != null)
        {
            var templatePrompt = TemplateNode.CreateTemplatePrompt(smallSummary.Summary, smallSummary.Tags);
            return new EmbeddableNode(raw.ElementId, raw.FullName, templatePrompt, raw.Labels);
        }

        var isEntryPoint = raw.Labels.Contains("EntryPoint");
        var prompt = PromptBuilder.BuildPrompt(codeBlock, nodeType, contextSuffix, isEntryPoint);
        return new EmbeddableNode(raw.ElementId, raw.FullName, prompt, raw.Labels);
    }

    public EmbeddableNode BuildContextualEmbeddableNode(RawContextualNodeData raw, int maxContextChars, int maxSourceLength)
    {
        var consumerBudget = (int)(maxContextChars * 0.6);
        var neighborBudget = (int)(maxContextChars * 0.4);

        var isEnum = raw.Node.Labels.Contains("Enum");
        var isClassOrInterface = raw.Node.Labels.Contains("Class") || raw.Node.Labels.Contains("Interface");

        // Separate neighbors into summary neighbors and consumer neighbors
        var summaryNeighbors = raw.Neighbors;
        List<NeighborData>? consumers = null;

        if (isEnum)
        {
            consumers = raw.Neighbors
                .Where(n => n.Labels.Contains("Method") && n.SourceText != null)
                .ToList();
        }
        else if (isClassOrInterface)
        {
            consumers = raw.Neighbors
                .Where(n => n.Labels.Contains("Method") && n.SourceText != null
                    && !n.Labels.Contains("Class") && !n.Labels.Contains("Interface"))
                .ToList();
        }

        var hasConsumers = consumers is { Count: > 0 };
        var effectiveNeighborBudget = hasConsumers ? neighborBudget : maxContextChars;

        var context = BuildPrioritizedContext(summaryNeighbors, maxChars: effectiveNeighborBudget);

        if (isEnum && hasConsumers)
        {
            var enumConsumerContext = BuildEnumConsumerContext(consumers!, consumerBudget, raw.Node.Name, raw.Node.Members);
            if (enumConsumerContext != null)
                context = (context ?? "") + enumConsumerContext;
        }
        else if (isClassOrInterface && hasConsumers)
        {
            var consumerSourceContext = BuildConsumerSourceContext(consumers!, consumerBudget);
            if (consumerSourceContext != null)
                context = (context ?? "") + consumerSourceContext;
        }

        return BuildEmbeddableNode(raw.Node, context, maxSourceLength);
    }

    private static (string CodeBlock, string NodeType, string? Members) BuildCodeBlock(RawNodeData raw, int maxSourceLength)
    {
        var sourceText = raw.SourceText;
        if (maxSourceLength > 0 && sourceText != null && sourceText.Length > maxSourceLength)
            sourceText = sourceText[..maxSourceLength];

        var nodeType = raw.Labels.Contains("Method") ? "Method"
            : raw.Labels.Contains("Interface") ? "Interface"
            : raw.Labels.Contains("Enum") ? "Enum"
            : "Class";

        string? members = null;
        string codeBlock;

        if (nodeType == "Method")
        {
            var returnType = raw.ReturnType ?? "void";
            var parameters = raw.Parameters ?? "";
            var body = sourceText ?? "";
            codeBlock = $"{returnType} {raw.FullName}({parameters})\n{body}";
        }
        else if (nodeType == "Interface")
        {
            codeBlock = sourceText ?? raw.Name;
        }
        else if (nodeType == "Enum")
        {
            members = raw.Members ?? "";
            codeBlock = $"enum {raw.FullName} {{ {members} }}";
        }
        else
        {
            codeBlock = sourceText ?? raw.Name;
        }

        return (codeBlock, nodeType, members);
    }

    private static string? BuildPrioritizedContext(
        List<NeighborData> neighbors, int maxChars)
    {
        if (neighbors.Count == 0) return null;

        var priorityOrder = new[] { "IMPLEMENTS", "CALLS", "DEFINES", "REFERENCES", "EXTENDS", "INHERITS_FROM" };
        var sorted = neighbors
            .Where(n => !string.IsNullOrEmpty(n.Summary))
            .OrderBy(n => Array.IndexOf(priorityOrder, n.Rel) is var idx && idx < 0 ? 99 : idx)
            .ToList();

        var lines = new List<string>();
        var totalLen = 0;

        foreach (var n in sorted)
        {
            var truncSummary = n.Summary.Length > 120 ? n.Summary[..117] + "..." : n.Summary;
            var line = $"{n.Rel}: {n.Name} ({truncSummary})";

            if (totalLen + line.Length > maxChars && lines.Count > 0)
                break;

            lines.Add(line);
            totalLen += line.Length + 3;
        }

        if (lines.Count == 0) return null;
        return $"\n\nContext from the codebase graph:\n- {string.Join("\n- ", lines)}";
    }

    private static string? BuildEnumConsumerContext(List<NeighborData> consumers, int maxChars, string? enumName, string? members)
    {
        if (enumName == null) return null;

        var memberNames = (members ?? "").Split(',', StringSplitOptions.TrimEntries)
            .Where(m => m.Length > 0).ToList();
        var searchTerms = new List<string> { enumName };
        searchTerms.AddRange(memberNames);

        var snippets = new List<string>();
        var totalLen = 0;

        foreach (var c in consumers)
        {
            if (string.IsNullOrEmpty(c.SourceText)) continue;

            var relevantLines = c.SourceText.Split('\n')
                .Where(line => searchTerms.Any(term => line.Contains(term, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (relevantLines.Count == 0) continue;

            var snippet = $"ConsumerCode: {c.FullName}:\n{string.Join("\n", relevantLines)}";
            if (totalLen + snippet.Length > maxChars && snippets.Count > 0) break;
            snippets.Add(snippet);
            totalLen += snippet.Length;
        }

        if (snippets.Count == 0) return null;
        return $"\n\nEnum usage in consumers:\n{string.Join("\n\n", snippets)}";
    }

    private static string? BuildConsumerSourceContext(List<NeighborData> consumers, int maxChars)
    {
        var validConsumers = consumers
            .Where(c => !string.IsNullOrEmpty(c.SourceText))
            .OrderByDescending(c => c.IsEntryPoint)
            .ToList();

        if (validConsumers.Count == 0) return null;

        var snippets = new List<string>();
        var totalLen = 0;
        var maxPerSnippet = maxChars / Math.Max(validConsumers.Count, 1);

        foreach (var c in validConsumers)
        {
            var truncated = c.SourceText!.Length > maxPerSnippet ? c.SourceText[..maxPerSnippet] : c.SourceText;
            var label = c.IsEntryPoint ? "EntryPoint" : "Consumer";
            var snippet = $"{label}: {c.FullName}:\n{truncated}";
            if (totalLen + snippet.Length > maxChars && snippets.Count > 0) break;
            snippets.Add(snippet);
            totalLen += snippet.Length;
        }

        if (snippets.Count == 0) return null;
        return $"\n\nKey consumer implementations:\n{string.Join("\n\n", snippets)}";
    }
}
