namespace GraphRagCli.Features.Summarize;

public record RawNodeData(
    string ElementId, string FullName, List<string> Labels,
    string? SourceText, string? ReturnType,
    string? Parameters, string? Members, string BodyHash)
{
    public string Name => FullName.Contains('.') ? FullName[(FullName.LastIndexOf('.') + 1)..] : FullName;
}

public record NeighborData(
    string Name, string FullName, string Summary, string? SourceText,
    string Rel, List<string> Labels, bool IsEntryPoint);

public record RawContextualNodeData(
    RawNodeData Node,
    List<NeighborData> Neighbors);

public record EmbeddableNode(string ElementId, string FullName, string Prompt, List<string> Labels);

public record AggregationData(string ElementId, string FullName, List<string> ChildSummaries);
