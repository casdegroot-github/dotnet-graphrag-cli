using Albatross.CommandLine.Annotations;
using GraphRagCli.Shared.Options;

namespace GraphRagCli.Features.Search;

[Verb<SearchCommandHandler>("search", Description = "Search the code graph using semantic and graph-augmented queries")]
public record SearchParams
{
    [Argument(Description = "Search query")]
    public required string Query { get; init; }

    [Option(DefaultToInitializer = true, Description = "Number of results")]
    public int Top { get; init; } = 10;

    [Option(Description = "Filter by type: Class, Interface, Method, Enum")]
    public string? Type { get; init; }

    [Option(DefaultToInitializer = true, Description = "Search mode: Hybrid (fulltext+vector) or Vector")]
    public SearchMode Mode { get; init; } = SearchMode.Hybrid;

    [UseOption<DatabaseOption>]
    public string? Database { get; init; }
}
