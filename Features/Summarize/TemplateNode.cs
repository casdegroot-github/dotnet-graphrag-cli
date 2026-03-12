namespace GraphRagCli.Features.Summarize;

public static class TemplateNode
{
    const string Prefix = "__TEMPLATE__";
    const string Separator = "||";

    public static bool IsTemplate(string prompt) =>
        prompt.StartsWith(Prefix);

    public static string CreateTemplatePrompt(string summary, string[] tags) =>
        $"{Prefix}{summary}{Separator}{string.Join(",", tags)}";

    public static (string Summary, string[] Tags) Parse(string prompt)
    {
        var content = prompt[Prefix.Length..];
        var parts = content.Split(Separator, 2);
        var summary = parts[0];
        var tags = parts.Length > 1
            ? parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries)
            : [];
        return (summary, tags);
    }
}
