using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;

namespace GraphRagCli.Commands;

public static class DatabaseCommand
{
    const string Label = "graphragcli";

    static readonly Option<string> s_name = new("--name") { Description = "Database name (default: derived from current directory)" };
    static readonly Option<int?> s_port = new("--port") { Description = "Bolt port (default: auto-find free port starting at 7687)" };
    static readonly Option<string> s_password = new("--password") { Description = "Neo4j password", DefaultValueFactory = _ => "password123" };
    static readonly Argument<string> s_adoptArg = new("container") { Description = "Name of the existing Docker container to adopt" };

    public static Command Build()
    {
        var init = new Command("init", "Spin up a new Neo4j Docker container");
        init.Add(s_name);
        init.Add(s_port);
        init.Add(s_password);
        init.SetAction(async (parseResult, _) => await ExecuteInitAsync(parseResult));

        var list = new Command("list", "List all GraphRagCli Neo4j containers");
        list.SetAction(async (_, _) => await ExecuteListAsync());

        var adopt = new Command("adopt", "Adopt an existing Docker container into the GraphRagCli group");
        adopt.Add(s_adoptArg);
        adopt.SetAction(async (parseResult, _) => await ExecuteAdoptAsync(parseResult));

        var command = new Command("database", "Manage Neo4j database instances");
        command.Add(init);
        command.Add(list);
        command.Add(adopt);
        return command;
    }

    /// <summary>
    /// List names of running containers with the graphragcli label.
    /// </summary>
    public static async Task<List<string>> ListRunningAsync()
    {
        var result = await RunProcess("docker", $"ps --filter label={Label} --format {{{{.Names}}}}");
        if (result.ExitCode != 0) return [];

        return result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    /// <summary>
    /// Resolve a container name to a ConnectionInfo by inspecting Docker.
    /// </summary>
    public static async Task<ConnectionInfo?> ResolveAsync(string name)
    {
        // Check container is running
        var statusResult = await RunProcess("docker", $"inspect --format {{{{.State.Status}}}} {name}");
        if (statusResult.ExitCode != 0 || statusResult.Output.Trim() != "running") return null;

        // Get bolt port mapping
        var portResult = await RunProcess("docker", $"port {name} 7687/tcp");
        if (portResult.ExitCode != 0) return null;

        var portLine = portResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (portLine == null) return null;
        var port = int.Parse(portLine.Split(':').Last());

        // Read password from env
        var envResult = await RunProcess("docker", $"inspect --format {{{{range .Config.Env}}}}{{{{println .}}}}{{{{end}}}} {name}");
        var password = "password123";
        if (envResult.ExitCode == 0)
        {
            foreach (var line in envResult.Output.Split('\n'))
            {
                if (line.StartsWith("NEO4J_AUTH="))
                {
                    var auth = line["NEO4J_AUTH=".Length..];
                    var slash = auth.IndexOf('/');
                    if (slash >= 0) password = auth[(slash + 1)..];
                    break;
                }
            }
        }

        return new ConnectionInfo($"bolt://localhost:{port}", "neo4j", password, "http://localhost:11434");
    }

    static string FindComposeFile()
    {
        // Walk up from the executable directory to find docker-compose.yaml
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "docker-compose.yaml");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        // Fallback: current directory
        var cwd = Path.Combine(Directory.GetCurrentDirectory(), "docker-compose.yaml");
        return File.Exists(cwd) ? cwd : throw new FileNotFoundException("docker-compose.yaml not found");
    }

    static async Task ExecuteInitAsync(ParseResult parseResult)
    {
        var name = parseResult.GetValue(s_name) ?? Path.GetFileName(Directory.GetCurrentDirectory()).ToLowerInvariant();
        var password = parseResult.GetValue(s_password)!;

        // Check if container already exists
        var existing = await RunProcess("docker", $"inspect --format {{{{.State.Status}}}} {name}");
        if (existing.ExitCode == 0)
        {
            var status = existing.Output.Trim();
            if (status == "running")
            {
                var resolved = await ResolveAsync(name);
                var actualPort = resolved != null ? int.Parse(resolved.Neo4jUri.Split(':').Last()) : 0;
                Console.WriteLine($"Database '{name}' is already running on bolt://localhost:{actualPort}");
                PrintMcpJson(name, actualPort, password);
                return;
            }

            Console.WriteLine($"Container '{name}' exists (status: {status}). Starting it...");
            await RunProcess("docker compose", $"-f {FindComposeFile()} -p {name} start");
        }
        else
        {
            var boltPort = parseResult.GetValue(s_port) ?? FindFreePort(7687);
            var httpPort = FindFreePort(7474);
            Console.WriteLine("Creating Neo4j container...");

            var composeFile = FindComposeFile();
            var env = new Dictionary<string, string>
            {
                ["GRAPHRAG_NAME"] = name,
                ["GRAPHRAG_BOLT_PORT"] = boltPort.ToString(),
                ["GRAPHRAG_HTTP_PORT"] = httpPort.ToString(),
                ["GRAPHRAG_PASSWORD"] = password,
            };
            var result = await RunProcess("docker", $"compose -f {composeFile} -p {name} up -d", env);

            if (result.ExitCode != 0)
            {
                Console.WriteLine($"Failed to create container: {result.Output}");
                Environment.ExitCode = 1;
                return;
            }
        }

        // Resolve actual port from container
        var conn = await ResolveAsync(name);
        if (conn == null)
        {
            Console.WriteLine("Failed to resolve container port. Check: docker compose -p " + name + " logs");
            Environment.ExitCode = 1;
            return;
        }
        var port = int.Parse(conn.Neo4jUri.Split(':').Last());

        // Wait for Neo4j to be ready
        Console.Write("Waiting for Neo4j to be ready");
        var ready = false;
        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(2000);
            Console.Write(".");

            try
            {
                var neo4j = new Neo4jService(conn.Neo4jUri, "neo4j", password);
                if (await neo4j.VerifyConnectivityAsync(silent: true))
                {
                    ready = true;
                    await neo4j.DisposeAsync();
                    break;
                }
                await neo4j.DisposeAsync();
            }
            catch { }
        }

        Console.WriteLine();

        if (!ready)
        {
            Console.WriteLine("Neo4j did not become ready in time. Check: docker compose -p " + name + " logs");
            Environment.ExitCode = 1;
            return;
        }

        Console.WriteLine("Neo4j is ready!");
        PrintMcpJson(name, port, password);

        Console.WriteLine();
        Console.WriteLine("Next steps:");
        Console.WriteLine($"  dotnet run -- ingest -d {name} <path-to-solution-or-project>");
        Console.WriteLine($"  dotnet run -- embed -d {name} --provider ollama");
    }

    static async Task ExecuteAdoptAsync(ParseResult parseResult)
    {
        var containerName = parseResult.GetValue(s_adoptArg)!;

        // Verify container exists
        var existing = await RunProcess("docker", $"inspect --format {{{{.State.Status}}}} {containerName}");
        if (existing.ExitCode != 0)
        {
            Console.WriteLine($"Container '{containerName}' not found.");
            Environment.ExitCode = 1;
            return;
        }

        // Check if already labeled
        var labelResult = await RunProcess("docker", $"inspect --format {{{{index .Config.Labels \"{Label}\"}}}} {containerName}");
        if (labelResult.ExitCode == 0 && labelResult.Output.Trim() != "<no value>")
        {
            Console.WriteLine($"Container '{containerName}' is already in the GraphRagCli group.");
            return;
        }

        // Docker doesn't support adding labels to existing containers.
        // Recreate: stop → rename → create new with same config + label → copy volumes → remove old.
        // Simpler: store adopted names in a local file.
        var adopted = LoadAdoptedContainers();
        if (adopted.Add(containerName))
        {
            SaveAdoptedContainers(adopted);
            Console.WriteLine($"Adopted '{containerName}' into the GraphRagCli group.");

            var resolved = await ResolveAsync(containerName);
            if (resolved != null)
            {
                var port = int.Parse(resolved.Neo4jUri.Split(':').Last());
                PrintMcpJson(containerName, port, resolved.Neo4jPassword);
            }
        }
        else
        {
            Console.WriteLine($"Container '{containerName}' is already adopted.");
        }
    }

    static async Task ExecuteListAsync()
    {
        var containers = await ListAllContainersAsync();
        if (containers.Count == 0)
        {
            Console.WriteLine("No GraphRagCli databases found. Create one with: database init --name <name>");
            return;
        }

        Console.WriteLine($"{"Name",-30} {"Status",-25} Bolt port");
        Console.WriteLine(new string('-', 75));

        foreach (var name in containers)
        {
            var statusResult = await RunProcess("docker", $"inspect --format {{{{.State.Status}}}} {name}");
            var status = statusResult.ExitCode == 0 ? statusResult.Output.Trim() : "not found";

            var ports = "";
            if (status == "running")
            {
                var portResult = await RunProcess("docker", $"port {name} 7687/tcp");
                if (portResult.ExitCode == 0)
                    ports = portResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            }

            Console.WriteLine($"{name,-30} {status,-25} {ports}");
        }
    }

    /// <summary>
    /// All containers: labeled (native) + adopted.
    /// </summary>
    static async Task<List<string>> ListAllContainersAsync()
    {
        var containers = new HashSet<string>();

        // Labeled containers
        var result = await RunProcess("docker", $"ps -a --filter label={Label} --format {{{{.Names}}}}");
        if (result.ExitCode == 0)
            foreach (var n in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                containers.Add(n);

        // Adopted containers
        foreach (var n in LoadAdoptedContainers())
            containers.Add(n);

        return containers.Order().ToList();
    }

    static void PrintMcpJson(string name, int port, string password)
    {
        Console.WriteLine("Add this to your project's .mcp.json:");
        Console.WriteLine();
        Console.WriteLine($$"""
            {
              "mcpServers": {
                "{{name}}": {
                  "command": "neo4j-mcp",
                  "env": {
                    "NEO4J_URI": "bolt://localhost:{{port}}",
                    "NEO4J_USERNAME": "neo4j",
                    "NEO4J_PASSWORD": "{{password}}",
                    "NEO4J_DATABASE": "neo4j",
                    "NEO4J_TRANSPORT_MODE": "stdio"
                  }
                }
              }
            }
            """);
    }

    static int FindFreePort(int startFrom)
    {
        for (var port = startFrom; port < startFrom + 100; port++)
        {
            try
            {
                using var client = new TcpClient();
                client.Connect(System.Net.IPAddress.Loopback, port);
            }
            catch (SocketException)
            {
                return port;
            }
        }
        return startFrom;
    }

    static string AdoptedPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".graphragcli", "adopted.json");

    static HashSet<string> LoadAdoptedContainers()
    {
        if (!File.Exists(AdoptedPath)) return [];
        return JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(AdoptedPath)) ?? [];
    }

    static void SaveAdoptedContainers(HashSet<string> containers)
    {
        var dir = Path.GetDirectoryName(AdoptedPath)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(AdoptedPath, JsonSerializer.Serialize(containers, new JsonSerializerOptions { WriteIndented = true }));
    }

    internal static async Task<(int ExitCode, string Output)> RunProcess(string command, string args, Dictionary<string, string>? envVars = null)
    {
        var psi = new ProcessStartInfo(command, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        if (envVars != null)
            foreach (var (key, value) in envVars)
                psi.Environment[key] = value;
        var process = Process.Start(psi)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, string.IsNullOrEmpty(output) ? error : output);
    }
}