namespace GraphRagCli.Features.List;

public record SolutionInfo(string Name, string? Summary);
public record ProjectInfo(string Name, string? Summary, long MemberCount);
public record DatabaseInfo(
    List<SolutionInfo> Solutions,
    Dictionary<string, long> NodeCounts,
    List<ProjectInfo> Projects,
    long TotalEmbeddable,
    long Embedded);
