using GraphRagCli.Shared.Docker;
using Neo4j.Driver;

namespace GraphRagCli.Features.Database;

public class DatabaseService(
    Neo4JContainerLifecycle lifecycle,
    Neo4jContainerClient containerClient)
{
    public async Task<InitResult> InitAsync(string name, string password, int? requestedPort)
    {
        var status = await containerClient.GetStatusAsync(name);

        if (status == "running")
        {
            var resolved = await containerClient.ResolveAsync(name);
            var port = resolved != null ? int.Parse(resolved.Uri.Split(':').Last()) : 0;
            return new InitResult(name, port, password, InitStatus.AlreadyRunning);
        }

        if (status != null)
        {
            await lifecycle.StartAsync(name);
        }
        else
        {
            var boltPort = requestedPort ?? Neo4JContainerLifecycle.FindFreePort(7687);
            var httpPort = Neo4JContainerLifecycle.FindFreePort(7474);
            await lifecycle.CreateAndStartAsync(name, password, boltPort, httpPort);
        }

        var conn = await containerClient.ResolveAsync(name);
        if (conn == null)
            return new InitResult(name, 0, password, InitStatus.Failed);

        var resolvedPort = int.Parse(conn.Uri.Split(':').Last());

        if (!await WaitForReadyAsync(conn.Uri, password))
            return new InitResult(name, resolvedPort, password, InitStatus.Failed);

        var driver = GraphDatabase.Driver(conn.Uri, AuthTokens.Basic("neo4j", password));
        try
        {
            await Neo4jSchemaService.InitializeAsync(driver);
        }
        finally
        {
            await driver.DisposeAsync();
        }

        return new InitResult(name, resolvedPort, password,
            status != null ? InitStatus.Started : InitStatus.Created);
    }

    public async Task<AdoptResult> AdoptAsync(string containerName)
    {
        var status = await containerClient.GetStatusAsync(containerName);
        if (status == null)
            return new AdoptResult($"Container '{containerName}' not found.", null, null);

        if (await containerClient.IsManagedAsync(containerName))
            return new AdoptResult($"Container '{containerName}' is already in the GraphRagCli group.", null, null);

        var adopted = containerClient.LoadAdoptedContainers();
        if (!adopted.Add(containerName))
            return new AdoptResult($"Container '{containerName}' is already adopted.", null, null);

        containerClient.SaveAdoptedContainers(adopted);

        var connection = await containerClient.ResolveAsync(containerName);
        if (connection != null)
        {
            var port = int.Parse(connection.Uri.Split(':').Last());
            return new AdoptResult($"Adopted '{containerName}' into the GraphRagCli group.", port, connection.Password);
        }

        return new AdoptResult($"Adopted '{containerName}' into the GraphRagCli group.", null, null);
    }

    private static async Task<bool> WaitForReadyAsync(string uri, string password, int maxAttempts = 30)
    {
        for (var i = 0; i < maxAttempts; i++)
        {
            await Task.Delay(2000);
            try
            {
                var neo4j = GraphDatabase.Driver(uri, AuthTokens.Basic("neo4j", password));
                try
                {
                    await neo4j.VerifyConnectivityAsync();
                    await neo4j.DisposeAsync();
                    return true;
                }
                catch
                {
                    await neo4j.DisposeAsync();
                }
            }
            catch { }
        }
        return false;
    }
}

public record InitResult(string Name, int BoltPort, string Password, InitStatus Status);

public enum InitStatus { AlreadyRunning, Started, Created, Failed }

public record AdoptResult(string Message, int? BoltPort, string? Password);
