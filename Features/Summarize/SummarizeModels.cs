namespace GraphRagCli.Features.Summarize;

public record ReadyNodeData(
    string ElementId, string FullName, List<string> Labels,
    string? SourceText, string? ReturnType,
    string? Parameters, string? Members, string? BodyHash,
    List<ChildData> Children, int MissingChildSummaries = 0)
{
    public string Name => FullName.Contains('.') ? FullName[(FullName.LastIndexOf('.') + 1)..] : FullName;
}

public record ChildData(
    string Name, string FullName, string? Summary, string? SourceText, List<string> Labels);

public record EmbeddableNode(string ElementId, string FullName, string Prompt, List<string> Labels);
