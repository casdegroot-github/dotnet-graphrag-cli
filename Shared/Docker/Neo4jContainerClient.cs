using System.Text.Json;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace GraphRagCli.Shared.Docker;

/// <summary>
/// Read-only Docker queries for Neo4j containers: list, inspect, resolve, status.
/// Used by Neo4jSessionFactory, Database handlers, and Neo4jContainerLifecycle.
/// </summary>
public class Neo4jContainerClient(IDockerClient docker)
{
    internal const string Label = "graphragcli";
    const string DefaultPassword = "password123";
    const string BoltPort = "7687/tcp";

    public async Task<List<string>> ListRunningAsync()
    {
        var containers = await docker.Containers.ListContainersAsync(new ContainersListParameters
        {
            Filters = ByLabel()
        });
        return ContainerNames(containers);
    }

    public async Task<ResolvedConnection?> ResolveAsync(string name)
    {
        var inspect = await TryInspectAsync(name);
        if (inspect == null || inspect.State.Status != "running")
            return null;

        var port = ParseBoltPort(inspect);
        if (port == null) return null;

        var password = ParsePassword(inspect) ?? DefaultPassword;
        return new ResolvedConnection($"bolt://localhost:{port}", "neo4j", password);
    }

    public async Task<string?> GetStatusAsync(string name)
    {
        var inspect = await TryInspectAsync(name);
        return inspect?.State.Status;
    }

    public async Task<bool> IsManagedAsync(string name)
    {
        var inspect = await TryInspectAsync(name);
        return inspect?.Config.Labels?.ContainsKey(Label) == true;
    }

    public async Task<List<Neo4jContainerInfo>> ListWithStatusAsync()
    {
        var names = await CollectAllNamesAsync();
        var results = new List<Neo4jContainerInfo>();

        foreach (var name in names.Order())
        {
            var inspect = await TryInspectAsync(name);
            if (inspect == null)
            {
                results.Add(new Neo4jContainerInfo(name, "not found", null));
                continue;
            }

            var port = inspect.State.Status == "running"
                ? ParseBoltPort(inspect)
                : null;

            results.Add(new Neo4jContainerInfo(name, inspect.State.Status, port));
        }

        return results;
    }

    // --- Adopted containers (read) ---

    public HashSet<string> LoadAdoptedContainers()
    {
        if (!File.Exists(AdoptedPath)) return [];
        return JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(AdoptedPath)) ?? [];
    }

    public void SaveAdoptedContainers(HashSet<string> containers)
    {
        var dir = Path.GetDirectoryName(AdoptedPath)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(AdoptedPath,
            JsonSerializer.Serialize(containers, new JsonSerializerOptions { WriteIndented = true }));
    }
    
    private async Task<ContainerInspectResponse?> TryInspectAsync(string name)
    {
        try { return await docker.Containers.InspectContainerAsync(name); }
        catch (DockerContainerNotFoundException) { return null; }
    }

    private static string? ParseBoltPort(ContainerInspectResponse inspect)
    {
        if (inspect.NetworkSettings.Ports?.TryGetValue(BoltPort, out var bindings) == true
            && bindings is { Count: > 0 })
            return bindings[0].HostPort;
        return null;
    }

    private static List<string> ContainerNames(IList<ContainerListResponse> containers) =>
        containers.Select(c => c.Names.First().TrimStart('/')).ToList();

    private static Dictionary<string, IDictionary<string, bool>> ByLabel() => new()
    {
        ["label"] = new Dictionary<string, bool> { [Label] = true }
    };

    async Task<HashSet<string>> CollectAllNamesAsync()
    {
        var labeled = await docker.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = ByLabel()
        });

        var names = new HashSet<string>(ContainerNames(labeled));
        foreach (var n in LoadAdoptedContainers())
            names.Add(n);
        return names;
    }

    static string? ParsePassword(ContainerInspectResponse inspect)
    {
        var authEnv = inspect.Config.Env?.FirstOrDefault(e => e.StartsWith("NEO4J_AUTH="));
        if (authEnv == null) return null;
        var slash = authEnv.IndexOf('/');
        return slash >= 0 ? authEnv[(slash + 1)..] : null;
    }

    static string AdoptedPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".graphragcli", "adopted.json");
}
