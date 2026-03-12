using System.CommandLine;
using Albatross.CommandLine;
using GraphRagCli.Features.Ingest.Analysis;
using GraphRagCli.Shared.GraphDb;

namespace GraphRagCli.Features.Ingest;

public class IngestCommandHandler(
    Neo4jSessionFactory sessionFactory,
    IngestService ingestService,
    SolutionResolver solutionResolver,
    ParseResult result,
    IngestParams parameters) : BaseHandler<IngestParams>(result, parameters)
{
    public override async Task<int> InvokeAsync(CancellationToken ct)
    {
        var solutionPath = solutionResolver.ResolveSolutionPath(parameters.SolutionPath);
        if (solutionPath == null)
        {
            Console.WriteLine($"Error: No .sln, .slnx, or .csproj file found at or under: {parameters.SolutionPath}");
            return 1;
        }

        Console.WriteLine("GraphRagCli - Ingest");
        Console.WriteLine($"  Solution:     {solutionPath}");
        Console.WriteLine($"  Database:     {parameters.Database ?? "(auto-detect)"}");
        Console.WriteLine($"  Skip tests:   {parameters.SkipTests}");
        Console.WriteLine($"  Skip samples: {parameters.SkipSamples}");
        Console.WriteLine();

        try
        {
            await using var driver = await sessionFactory.CreateDriverAsync(parameters.Database);
            var ingestResult = await ingestService.IngestAsync(driver, solutionPath, parameters);

            if (ingestResult.IsEmpty)
            {
                Console.WriteLine("No code symbols found. Nothing to ingest.");
                return 0;
            }

            PrintResult(ingestResult);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Neo4j error: {ex.Message}");
            Console.WriteLine("Make sure Neo4j is running: dotnet run -- database init");
            return 1;
        }
    }

    private static void PrintResult(IngestResult r)
    {
        Console.WriteLine("\n=== Ingestion Results ===");

        foreach (var p in r.Projects)
        {
            Console.WriteLine($"\n  {p.Name}:");
            Console.WriteLine($"    Nodes: {p.Namespaces} namespaces, {p.Classes} classes, {p.Interfaces} interfaces, {p.Methods} methods, {p.Enums} enums");
            Console.WriteLine($"    Edges: {p.Calls} calls, {p.References} references");
        }

        Console.WriteLine("\n--- Reconcile ---");
        if (r.Reconcile.DeletedEdges > 0 || r.Reconcile.DeletedNodes > 0)
            Console.WriteLine($"Cleaned up {r.Reconcile.DeletedNodes} stale nodes and {r.Reconcile.DeletedEdges} stale edges.");
        if (r.Reconcile.TransferredEmbeddings > 0)
            Console.WriteLine($"Transferred {r.Reconcile.TransferredEmbeddings} embeddings from renamed/moved nodes.");
        if (r.Reconcile is { DeletedEdges: 0, DeletedNodes: 0, TransferredEmbeddings: 0 })
            Console.WriteLine("Graph is up to date — no stale nodes or edges.");

        Console.WriteLine($"\nSolution node '{r.SolutionName}' created with {r.Projects.Count} projects.");

        Console.WriteLine($"Linked {r.EntryPoints.LinkedImplementations} interface method implementations.");
        Console.WriteLine($"Labeled {r.EntryPoints.EntryPoints} entry points.");

        if (r.NugetProjectCount != null)
            Console.WriteLine($"\nPublic API surface ({r.NugetProjectCount} NuGet projects from .slnf):");
        else
            Console.WriteLine("\nPublic API surface (all public members):");

        foreach (var (display, count) in r.PublicApi.TypeCounts)
            Console.WriteLine($"  {count} public {display}");
        Console.WriteLine($"  {r.PublicApi.MethodCount} public methods");
        Console.WriteLine($"  Total PublicApi: {r.PublicApi.Total}");

        Console.WriteLine("\nDone! Open Neo4j Browser at http://localhost:7474 to explore the graph.");
    }
}
