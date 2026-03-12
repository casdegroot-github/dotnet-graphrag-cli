using GraphRagCli.Shared.Docker;
using Neo4j.Driver;

namespace GraphRagCli.Shared.GraphDb;

/// <summary>
/// Resolves a Neo4j driver from a Docker container name (or auto-detects).
/// </summary>
public class Neo4jSessionFactory(Neo4jContainerClient containerClient)
{
    /// <summary>
    /// Resolves the Neo4j driver from a Docker container name (or auto-detects).
    /// Caller owns the driver lifetime.
    /// </summary>
    public async Task<IDriver> CreateDriverAsync(string? containerName)
    {
        return await ResolveDriverAsync(containerName)
            ?? throw new InvalidOperationException("Database connection failed.");
    }

    private async Task<IDriver?> ResolveDriverAsync(string? containerName)
    {
        ResolvedConnection? resolved;

        if (containerName is not null)
        {
            resolved = await containerClient.ResolveAsync(containerName);
            if (resolved == null)
            {
                Console.WriteLine($"Container '{containerName}' not found or not running. Use 'database list' to see available.");
                return null;
            }
        }
        else
        {
            var running = await containerClient.ListRunningAsync();
            if (running.Count == 1)
            {
                resolved = await containerClient.ResolveAsync(running[0]);
                if (resolved != null)
                    Console.WriteLine($"Auto-detected container: {running[0]}");
            }
            else if (running.Count > 1)
            {
                Console.WriteLine("Multiple containers running. Specify one with --database/-d:");
                foreach (var name in running)
                    Console.WriteLine($"  - {name}");
                return null;
            }
            else
            {
                Console.WriteLine("No containers running. Use 'database init' to create one, or specify with -d.");
                return null;
            }

            if (resolved == null)
            {
                Console.WriteLine("Cannot connect to Neo4j. Is it running?");
                return null;
            }
        }

        var driver = GraphDatabase.Driver(resolved.Uri, AuthTokens.Basic(resolved.User, resolved.Password));
        try
        {
            await driver.VerifyConnectivityAsync();
            Console.WriteLine("Connected to Neo4j.");
            return driver;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Cannot connect to Neo4j: {ex.Message}");
            await driver.DisposeAsync();
            return null;
        }
    }
}
