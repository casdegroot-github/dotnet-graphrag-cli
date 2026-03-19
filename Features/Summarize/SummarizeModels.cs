namespace GraphRagCli.Features.Summarize;

public record EmbeddableNode(string ElementId, string FullName, string Prompt, List<string> Labels)
{
    public string SafeFullName => string.IsNullOrEmpty(FullName) ? ElementId : FullName;
}

public record CrossChildReference(string From, string To, int Refs);
