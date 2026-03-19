namespace GraphRagCli.Features.Search;

public record SearchResult(
    string FullName,
    string Name,
    string? Summary,
    string? Namespace,
    string? FilePath,
    double Score,
    string? Type = null,
    double? PageRank = null,
    int? Tier = null,
    List<string>? Labels = null,
    string? Parameters = null,
    string? ReturnType = null,
    string? SearchText = null)
{
    public List<NeighborInfo> Neighbors { get; init; } = new();
}

public record NeighborInfo(string Name, string? Summary, string Relationship, string? FullName);
