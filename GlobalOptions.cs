using System.CommandLine;
using System.CommandLine.Parsing;
using GraphRagCli.Commands;

namespace GraphRagCli;

public record ConnectionInfo(string Neo4jUri, string Neo4jUser, string Neo4jPassword, string OllamaUrl, string? Database = null);

public static class GlobalOptions
{
    public static readonly Option<string> Uri = new("--uri") { Description = "Neo4j connection URI", DefaultValueFactory = _ => "bolt://localhost:7687" };
    public static readonly Option<string> User = new("--user") { Description = "Neo4j username", DefaultValueFactory = _ => "neo4j" };
    public static readonly Option<string> Password = new("--password") { Description = "Neo4j password", DefaultValueFactory = _ => "password123" };
    public static readonly Option<string> OllamaUrl = new("--ollama-url") { Description = "Ollama endpoint URL", DefaultValueFactory = _ => "http://localhost:11434" };
    public static readonly Option<string?> Database = new("--database", ["-d"]) { Description = "Database name — resolves connection from Docker container" };

    public static void AddNeo4jOptions(Command command)
    {
        command.Add(Uri);
        command.Add(User);
        command.Add(Password);
        command.Add(Database);
    }

    public static void AddAllOptions(Command command)
    {
        AddNeo4jOptions(command);
        command.Add(OllamaUrl);
    }

    public static ConnectionInfo Parse(ParseResult r) =>
        new(r.GetValue(Uri)!, r.GetValue(User)!, r.GetValue(Password)!, r.GetValue(OllamaUrl)!, r.GetValue(Database));

    public static ConnectionInfo ParseNeo4jOnly(ParseResult r) =>
        new(r.GetValue(Uri)!, r.GetValue(User)!, r.GetValue(Password)!, "", r.GetValue(Database));

    public static async Task<Neo4jService?> ConnectNeo4jAsync(ConnectionInfo conn)
    {
        var effectiveConn = conn;

        if (conn.Database is not null)
        {
            // Explicit --database flag: resolve from Docker container
            var resolved = await DatabaseCommand.ResolveAsync(conn.Database);
            if (resolved == null)
            {
                Console.WriteLine($"Database '{conn.Database}' not found or not running. Use 'database list' to see available databases.");
                Environment.ExitCode = 1;
                return null;
            }
            effectiveConn = resolved with { OllamaUrl = conn.OllamaUrl };
        }
        else
        {
            // No --database: auto-detect from running containers
            var running = await DatabaseCommand.ListRunningAsync();
            if (running.Count == 1)
            {
                var resolved = await DatabaseCommand.ResolveAsync(running[0]);
                if (resolved != null)
                {
                    Console.WriteLine($"Auto-detected database: {running[0]}");
                    effectiveConn = resolved with { OllamaUrl = conn.OllamaUrl };
                }
            }
            else if (running.Count > 1)
            {
                Console.WriteLine("Multiple databases are running. Specify one with --database/-d:");
                foreach (var name in running)
                    Console.WriteLine($"  - {name}");
                Environment.ExitCode = 1;
                return null;
            }
        }

        var neo4j = new Neo4jService(effectiveConn.Neo4jUri, effectiveConn.Neo4jUser, effectiveConn.Neo4jPassword);
        if (await neo4j.VerifyConnectivityAsync())
        {
            Console.WriteLine("Connected to Neo4j.");
            return neo4j;
        }

        Console.WriteLine("Cannot connect to Neo4j. Is it running?");
        Environment.ExitCode = 1;
        await neo4j.DisposeAsync();
        return null;
    }
}
