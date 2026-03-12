namespace GraphRagCli.Features.Database;

static class OutputHelper
{
    public static void PrintMcpJson(string name, int port, string password)
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
}
