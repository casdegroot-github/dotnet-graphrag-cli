using System.CommandLine;
using Albatross.CommandLine;
using GraphRagCli.Shared.Docker;

namespace GraphRagCli.Features.Database;

public class ListDatabasesHandler(
    Neo4jContainerClient containerClient,
    ParseResult result,
    ListDatabasesParams parameters) : BaseHandler<ListDatabasesParams>(result, parameters)
{
    public override async Task<int> InvokeAsync(CancellationToken ct)
    {
        var containers = await containerClient.ListWithStatusAsync();

        if (containers.Count == 0)
        {
            Console.WriteLine("No GraphRagCli databases found. Create one with: database init --name <name>");
            return 0;
        }

        Console.WriteLine($"{"Name",-30} {"Status",-25} Bolt port");
        Console.WriteLine(new string('-', 75));

        foreach (var c in containers)
            Console.WriteLine($"{c.Name,-30} {c.Status,-25} {c.BoltPort ?? ""}");

        return 0;
    }
}
