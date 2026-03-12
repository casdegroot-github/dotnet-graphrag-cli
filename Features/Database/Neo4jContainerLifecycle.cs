using System.Net.Sockets;
using Docker.DotNet;
using Docker.DotNet.Models;
using GraphRagCli.Shared.Docker;

namespace GraphRagCli.Features.Database;

/// <summary>
/// Write operations for Neo4j containers: pull image, create, start.
/// </summary>
public class Neo4JContainerLifecycle(IDockerClient docker)
{
    const string Neo4jImage = "neo4j:5";

    static readonly string[] Neo4jEnv =
    [
        "NEO4J_apoc_export_file_enabled=true",
        "NEO4J_apoc_import_file_enabled=true",
        "NEO4J_PLUGINS=[\"apoc\", \"graph-data-science\"]",
        "NEO4J_dbms_security_procedures_unrestricted=apoc.*,gds.*"
    ];

    public async Task CreateAndStartAsync(string name, string password, int boltPort, int httpPort)
    {
        await PullImageAsync();
        var id = await CreateContainerAsync(name, password, boltPort, httpPort);
        await docker.Containers.StartContainerAsync(id, new ContainerStartParameters());
    }

    public async Task StartAsync(string name)
    {
        await docker.Containers.StartContainerAsync(name, new ContainerStartParameters());
    }

    public static int FindFreePort(int startFrom)
    {
        for (var port = startFrom; port < startFrom + 100; port++)
        {
            try
            {
                using var tcp = new TcpClient();
                tcp.Connect(System.Net.IPAddress.Loopback, port);
            }
            catch (SocketException)
            {
                return port;
            }
        }
        return startFrom;
    }

    async Task PullImageAsync()
    {
        var (image, tag) = ParseImageRef(Neo4jImage);
        await docker.Images.CreateImageAsync(
            new ImagesCreateParameters { FromImage = image, Tag = tag },
            null,
            new Progress<JSONMessage>(m =>
            {
                if (!string.IsNullOrEmpty(m.Status)) Console.Write(".");
            }));
    }

    async Task<string> CreateContainerAsync(string name, string password, int boltPort, int httpPort)
    {
        var env = new List<string>(Neo4jEnv) { $"NEO4J_AUTH=neo4j/{password}" };

        var response = await docker.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Name = name,
            Image = Neo4jImage,
            Env = env,
            Labels = new Dictionary<string, string> { [Neo4jContainerClient.Label] = "true" },
            ExposedPorts = new Dictionary<string, EmptyStruct>
            {
                ["7474/tcp"] = default,
                ["7687/tcp"] = default
            },
            HostConfig = new HostConfig
            {
                PortBindings = new Dictionary<string, IList<PortBinding>>
                {
                    ["7474/tcp"] = [new PortBinding { HostPort = httpPort.ToString() }],
                    ["7687/tcp"] = [new PortBinding { HostPort = boltPort.ToString() }]
                },
                Mounts =
                [
                    new Mount { Type = "volume", Source = $"{name}-data", Target = "/data" },
                    new Mount { Type = "volume", Source = $"{name}-plugins", Target = "/plugins" }
                ]
            }
        });

        return response.ID;
    }

    static (string Image, string Tag) ParseImageRef(string imageRef)
    {
        var parts = imageRef.Split(':');
        return parts.Length == 2 ? (parts[0], parts[1]) : (imageRef, "latest");
    }
}
