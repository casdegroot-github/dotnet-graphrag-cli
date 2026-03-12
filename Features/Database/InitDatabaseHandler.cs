using System.CommandLine;
using Albatross.CommandLine;

namespace GraphRagCli.Features.Database;

public class InitDatabaseHandler(
    DatabaseService databaseService,
    ParseResult result,
    InitDatabaseParams parameters) : BaseHandler<InitDatabaseParams>(result, parameters)
{
    public override async Task<int> InvokeAsync(CancellationToken ct)
    {
        var name = parameters.Name ?? Path.GetFileName(Directory.GetCurrentDirectory()).ToLowerInvariant();
        var password = parameters.Password;

        Console.Write("Initializing Neo4j container");

        try
        {
            var initResult = await databaseService.InitAsync(name, password, parameters.Port);

            switch (initResult.Status)
            {
                case InitStatus.AlreadyRunning:
                    Console.WriteLine($"\nDatabase '{initResult.Name}' is already running on bolt://localhost:{initResult.BoltPort}");
                    OutputHelper.PrintMcpJson(initResult.Name, initResult.BoltPort, initResult.Password);
                    return 0;

                case InitStatus.Failed:
                    Console.WriteLine($"\nFailed to initialize database '{initResult.Name}'. Check: docker logs {initResult.Name}");
                    return 1;

                default:
                    Console.WriteLine("\nNeo4j is ready!");
                    OutputHelper.PrintMcpJson(initResult.Name, initResult.BoltPort, initResult.Password);
                    Console.WriteLine();
                    Console.WriteLine("Next steps:");
                    Console.WriteLine($"  dotnet run -- ingest -d {initResult.Name} <path-to-solution-or-project>");
                    Console.WriteLine($"  dotnet run -- embed -d {initResult.Name} --provider ollama");
                    return 0;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nFailed to create container: {ex.Message}");
            return 1;
        }
    }
}
