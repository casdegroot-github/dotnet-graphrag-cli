using Albatross.CommandLine.Annotations;
using GraphRagCli.Shared.Options;

namespace GraphRagCli.Features.List;

[Verb<ListCommandHandler>("list", Description = "Show database contents: projects, node counts, and embedding coverage")]
public record ListParams
{
    [UseOption<DatabaseOption>]
    public string? Database { get; init; }
}
