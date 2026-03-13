namespace GraphRagCli.Features.Search;

public record NeighborInfo(string Name, string? Summary, string Relationship, string? FullName = null);

public record SearchResult(
    string FullName,
    string Name,
    string? Summary,
    string? Namespace,
    string? FilePath,
    double Score,
    string? Type,
    double? PageRank = null,
    List<string>? Labels = null,
    string? Parameters = null,
    string? ReturnType = null,
    List<NeighborInfo>? Neighbors = null);
