using System.CommandLine;
using System.CommandLine.Parsing;

namespace CodeGraphIndexer;

public record ConnectionInfo(string Neo4jUri, string Neo4jUser, string Neo4jPassword, string OllamaUrl);

public static class GlobalOptions
{
    public static readonly Option<string> Uri = new("--uri") { Description = "Neo4j connection URI", DefaultValueFactory = _ => "bolt://localhost:7687" };
    public static readonly Option<string> User = new("--user") { Description = "Neo4j username", DefaultValueFactory = _ => "neo4j" };
    public static readonly Option<string> Password = new("--password") { Description = "Neo4j password", DefaultValueFactory = _ => "password123" };
    public static readonly Option<string> OllamaUrl = new("--ollama-url") { Description = "Ollama endpoint URL", DefaultValueFactory = _ => "http://localhost:11434" };

    public static void AddNeo4jOptions(Command command)
    {
        command.Add(Uri);
        command.Add(User);
        command.Add(Password);
    }

    public static void AddAllOptions(Command command)
    {
        AddNeo4jOptions(command);
        command.Add(OllamaUrl);
    }

    public static ConnectionInfo Parse(ParseResult r) =>
        new(r.GetValue(Uri)!, r.GetValue(User)!, r.GetValue(Password)!, r.GetValue(OllamaUrl)!);

    public static ConnectionInfo ParseNeo4jOnly(ParseResult r) =>
        new(r.GetValue(Uri)!, r.GetValue(User)!, r.GetValue(Password)!, "");

    public static async Task<Neo4jService?> ConnectNeo4jAsync(ConnectionInfo conn)
    {
        var neo4j = new Neo4jService(conn.Neo4jUri, conn.Neo4jUser, conn.Neo4jPassword);
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
