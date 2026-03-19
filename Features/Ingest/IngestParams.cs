using Albatross.CommandLine.Annotations;
using GraphRagCli.Shared.Options;

namespace GraphRagCli.Features.Ingest;

[Verb<IngestCommandHandler>("ingest", Description = "Analyze C# solution and ingest code graph into Neo4j")]
public record IngestParams
{
    [Argument(Description = "Path to solution file or directory")]
    public required string SolutionPath { get; init; }

    [Option(Description = "Skip projects containing 'Test' or 'Tests'")]
    public bool SkipTests { get; init; }

    [Option(Description = "Skip projects containing 'Sample', 'Example', or 'Playground'")]
    public bool SkipSamples { get; init; }

    [UseOption<DatabaseOption>]
    public string? Database { get; init; }
}
