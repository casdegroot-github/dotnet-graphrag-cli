using System.CommandLine;
using System.CommandLine.Parsing;

namespace GraphRagCli.Commands;

public static class ListCommand
{
    public static Command Build()
    {
        var command = new Command("list", "Show database contents: projects, node counts, and embedding coverage");
        GlobalOptions.AddNeo4jOptions(command);

        command.SetAction(async (parseResult, _) => await ExecuteAsync(parseResult));
        return command;
    }

    static async Task ExecuteAsync(ParseResult parseResult)
    {
        var conn = GlobalOptions.ParseNeo4jOnly(parseResult);
        var dbLabel = conn.Database ?? "neo4j";

        try
        {
            await using var neo4j = await GlobalOptions.ConnectNeo4jAsync(conn);
            if (neo4j == null) return;

            var info = await neo4j.GetDatabaseInfoAsync();

            Console.WriteLine($"\n=== Database: {dbLabel} ===\n");

            // Solutions
            if (info.Solutions.Count > 0)
            {
                Console.WriteLine("Solutions:");
                foreach (var sol in info.Solutions)
                {
                    Console.WriteLine($"  {sol.Name}");
                    if (sol.Summary is not null)
                        Console.WriteLine($"    {sol.Summary}");
                }
                Console.WriteLine();
            }

            // Node counts
            Console.WriteLine("Node counts:");
            foreach (var (label, count) in info.NodeCounts.OrderByDescending(kv => kv.Value))
                Console.WriteLine($"  {label,-12} {count,6:N0}");
            Console.WriteLine($"  {"Total",-12} {info.NodeCounts.Values.Sum(),6:N0}");

            // Embedding coverage
            Console.WriteLine($"\nEmbedding coverage:");
            Console.WriteLine($"  Embedded: {info.Embedded,5:N0} / {info.TotalEmbeddable:N0}");

            // Projects
            Console.WriteLine($"\nProjects ({info.Projects.Count}):");
            foreach (var proj in info.Projects)
            {
                Console.WriteLine($"\n  {proj.Name} ({proj.MemberCount} members)");
                if (proj.Summary is not null)
                {
                    var summary = proj.Summary.Length > 120
                        ? proj.Summary[..117] + "..."
                        : proj.Summary;
                    Console.WriteLine($"    {summary}");
                }
            }

            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }
}