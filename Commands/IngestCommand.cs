using System.CommandLine;
using System.CommandLine.Parsing;
using GraphRagCli.Helpers;

namespace GraphRagCli.Commands;

public static class IngestCommand
{
    static readonly Argument<string> s_solutionPathArg = new("solution-path") { Description = "Path to solution file or directory" };
    static readonly Option<bool> s_skipTests = new("--skip-tests") { Description = "Skip projects containing 'Test' or 'Tests'" };
    static readonly Option<bool> s_skipSamples = new("--skip-samples") { Description = "Skip projects containing 'Sample', 'Example', or 'Playground'" };
    static readonly Option<string?> s_nugetSlnf = new("--nuget-slnf") { Description = "Path to .slnf for NuGet project filtering" };

    public static Command Build()
    {
        var command = new Command("ingest", "Analyze C# solution and ingest code graph into Neo4j");
        command.Add(s_solutionPathArg);
        command.Add(s_skipTests);
        command.Add(s_skipSamples);
        command.Add(s_nugetSlnf);
        GlobalOptions.AddNeo4jOptions(command);

        command.SetAction(async (parseResult, _) => await ExecuteAsync(parseResult));
        return command;
    }

    static async Task ExecuteAsync(ParseResult parseResult)
    {
        var inputPath = parseResult.GetValue(s_solutionPathArg)!;
        var skipTestsVal = parseResult.GetValue(s_skipTests);
        var skipSamplesVal = parseResult.GetValue(s_skipSamples);
        var nugetSlnfPath = parseResult.GetValue(s_nugetSlnf);
        var conn = GlobalOptions.ParseNeo4jOnly(parseResult);

        var solutionPath = SolutionHelper.ResolveSolutionPath(inputPath);
        if (solutionPath == null)
        {
            Console.WriteLine($"Error: No .sln, .slnx, or .csproj file found at or under: {inputPath}");
            Environment.ExitCode = 1;
            return;
        }

        Console.WriteLine("GraphRagCli - Ingest");
        Console.WriteLine($"  Solution:     {solutionPath}");
        Console.WriteLine($"  Neo4j:        {conn.Neo4jUri}");
        Console.WriteLine($"  Skip tests:   {skipTestsVal}");
        Console.WriteLine($"  Skip samples: {skipSamplesVal}");
        Console.WriteLine();

        var allResults = await AnalyzeAsync(solutionPath, skipTestsVal, skipSamplesVal);
        if (allResults == null) return;

        await IngestAsync(conn, allResults, nugetSlnfPath, solutionPath);
    }

    static async Task<Dictionary<string, CodeAnalyzer.AnalysisResult>?> AnalyzeAsync(
        string solutionPath, bool skipTests, bool skipSamples)
    {
        Console.WriteLine("=== Phase 1: Code Analysis ===");
        var analyzer = new CodeAnalyzer();
        var allResults = await analyzer.AnalyzeSolutionAsync(solutionPath, skipTests, skipSamples);

        if (allResults.Count == 0)
        {
            Console.WriteLine("\nNo code symbols found. Nothing to ingest.");
            return null;
        }

        return allResults;
    }

    static async Task IngestAsync(
        ConnectionInfo conn, Dictionary<string, CodeAnalyzer.AnalysisResult> allResults,
        string? nugetSlnfPath, string solutionPath)
    {
        Console.WriteLine("\n=== Phase 2: Neo4j Ingestion ===");
        try
        {
            await using var neo4j = await GlobalOptions.ConnectNeo4jAsync(conn);
            if (neo4j == null) return;

            await neo4j.InitializeSchemaAsync();

            Console.WriteLine("\n--- Creating nodes ---");
            foreach (var (name, result) in allResults)
            {
                Console.WriteLine($"\n  {name}:");
                await neo4j.IngestProjectNodesAsync(name, result);
            }

            Console.WriteLine("\n--- Creating edges ---");
            foreach (var (name, result) in allResults)
            {
                Console.WriteLine($"\n  {name}:");
                await neo4j.IngestProjectEdgesAsync(name, result);
            }

            // Create Solution node from .sln filename
            var solutionName = Path.GetFileNameWithoutExtension(solutionPath);
            await neo4j.CreateSolutionNodeAsync(solutionName, allResults.Keys);

            Console.WriteLine("\nLabeling entry points...");
            await neo4j.LabelEntryPointsAsync();

            var nugetProjects = SolutionHelper.ResolveNuGetProjects(nugetSlnfPath, solutionPath);
            if (nugetProjects != null)
            {
                Console.WriteLine($"\nLabeling public API surface ({nugetProjects.Count} NuGet projects from .slnf)...");
                await neo4j.LabelPublicApiAsync(nugetProjects);
            }
            else
            {
                Console.WriteLine("\nNo .slnf found — labeling all public members as PublicApi.");
                await neo4j.LabelPublicApiAsync(null);
            }

            Console.WriteLine("\nDone! Open Neo4j Browser at http://localhost:7474 to explore the graph.");
            Console.WriteLine("Try: MATCH (p:Project)-[:CONTAINS]->(c) RETURN p, c LIMIT 50");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Neo4j error: {ex.Message}");
            Console.WriteLine("Make sure Neo4j is running: docker compose up -d");
            Environment.ExitCode = 1;
        }
    }
}
