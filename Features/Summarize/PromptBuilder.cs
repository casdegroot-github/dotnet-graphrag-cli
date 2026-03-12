namespace GraphRagCli.Features.Summarize;

public static class PromptBuilder
{
    public static string BuildPrompt(string codeBlock, string nodeType, string? contextSuffix, bool isEntryPoint = false)
    {
        var typeInstruction = nodeType switch
        {
            "Method" when isEntryPoint =>
                "This is a DI/hosting registration method. " +
                "Explain what subsystem it wires up, what services and interfaces it registers, and what configuration it applies.",
            "Method" =>
                "Explain the business problem this method solves, what data flows in and out, " +
                "and what decisions or transformations it performs. Focus on WHY it exists, not just WHAT it does.",
            "Class" =>
                "Explain what business problem this class solves, what data flows through it, " +
                "and what key decisions it makes. Mention the orchestration pattern if it coordinates multiple collaborators.",
            "Interface" =>
                "Explain what capability this interface abstracts and why that abstraction boundary exists. " +
                "What can implementations vary?",
            "Enum" =>
                "Explain the domain concept this enum models. List all members with brief explanations. " +
                "Mention how consumers use these values to drive behavior.",
            _ => "Explain the purpose and architectural role of this code."
        };

        return $"""
            Analyze this C# code and write a summary for a code intelligence graph.

            {typeInstruction}

            Rules:
            - Lead with the business purpose, not the class/method name
            - Never start with "This method...", "This class...", "The `Foo`..."
            - Describe behavior and data flow, not structure
            - Be concise: 2-4 sentences max
            - Assign 1-3 tags: DATABASE, API, CONFIGURATION, UTILITY, PRODUCER, CONSUMER, EXTERNAL_SERVICE,
              DI_REGISTRATION, PIPELINE, MAPPING, VALIDATION, MESSAGING, CACHING, LOGGING, SERIALIZATION, AUTH, TESTING

            ```csharp
            {codeBlock}
            ```
            {contextSuffix ?? ""}
            """;
    }

    public static SummaryResult? TrySmallNodeSummary(string nodeType, string name, string fullName, string? sourceText, string? members)
    {
        var lineCount = (sourceText ?? "").Split('\n').Length;

        if (nodeType == "Interface" && lineCount <= 5 && string.IsNullOrWhiteSpace(sourceText?.Replace("{", "").Replace("}", "").Trim()))
            return new SummaryResult($"Marker interface for {name}", ["UTILITY"]);

        if (nodeType == "Interface" && lineCount <= 5)
        {
            var memberLines = (sourceText ?? "").Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("//") && !l.StartsWith("{") && !l.StartsWith("}") && !l.StartsWith("public") && !l.StartsWith("interface"))
                .ToList();
            if (memberLines.Count == 0)
                return new SummaryResult($"Marker interface for {name}", ["UTILITY"]);
        }

        return null;
    }
}
