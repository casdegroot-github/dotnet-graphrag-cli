using Albatross.CommandLine.Annotations;
using GraphRagCli.Shared.Options;

namespace GraphRagCli.Features.Database;

[Verb("database", Description = "Manage Neo4j database instances")]
public record DatabaseParams;

[Verb<InitDatabaseHandler>("database init", Description = "Spin up a new Neo4j Docker container")]
public record InitDatabaseParams
{
    [Option(Description = "Database name (default: derived from current directory)")]
    public string? Name { get; init; }

    [Option(Description = "Bolt port (default: auto-find free port starting at 7687)")]
    public int? Port { get; init; }

    [Option(DefaultToInitializer = true, Description = "Neo4j password")]
    public string Password { get; init; } = "password123";
}

[Verb<ListDatabasesHandler>("database list", Description = "List all GraphRagCli Neo4j containers")]
public record ListDatabasesParams;

[Verb<AdoptDatabaseHandler>("database adopt", Description = "Adopt an existing Docker container into the GraphRagCli group")]
public record AdoptDatabaseParams
{
    [Argument(Description = "Name of the existing Docker container to adopt")]
    public required string Container { get; init; }
}
