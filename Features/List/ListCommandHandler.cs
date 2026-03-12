using System.CommandLine;
using System.Text;
using Albatross.CommandLine;
using GraphRagCli.Shared.GraphDb;

namespace GraphRagCli.Features.List;

public class ListCommandHandler(
    Neo4jSessionFactory sessionFactory,
    ParseResult result,
    ListParams parameters) : BaseHandler<ListParams>(result, parameters)
{
    public override async Task<int> InvokeAsync(CancellationToken ct)
    {
        var database = parameters.Database;
        var dbLabel = database ?? "neo4j";

        try
        {
            await using var driver = await sessionFactory.CreateDriverAsync(database);
            var repo = new Neo4jListRepository(driver);
            var info = await repo.GetDatabaseInfoAsync();

            Console.Write(FormatOutput(dbLabel, info));
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static string FormatOutput(string dbLabel, DatabaseInfo info)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"\n=== Database: {dbLabel} ===\n");

        if (info.Solutions.Count > 0)
        {
            sb.AppendLine("Solutions:");
            foreach (var sol in info.Solutions)
            {
                sb.AppendLine($"  {sol.Name}");
                if (sol.Summary is not null)
                    sb.AppendLine($"    {sol.Summary}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("Node counts:");
        foreach (var (label, count) in info.NodeCounts.OrderByDescending(kv => kv.Value))
            sb.AppendLine($"  {label,-12} {count,6:N0}");
        sb.AppendLine($"  {"Total",-12} {info.NodeCounts.Values.Sum(),6:N0}");

        sb.AppendLine($"\nEmbedding coverage:");
        sb.AppendLine($"  Embedded: {info.Embedded,5:N0} / {info.TotalEmbeddable:N0}");

        sb.AppendLine($"\nProjects ({info.Projects.Count}):");
        foreach (var proj in info.Projects)
        {
            sb.AppendLine($"\n  {proj.Name} ({proj.MemberCount} members)");
            if (proj.Summary is not null)
            {
                var summary = proj.Summary.Length > 120
                    ? proj.Summary[..117] + "..."
                    : proj.Summary;
                sb.AppendLine($"    {summary}");
            }
        }

        sb.AppendLine();
        return sb.ToString();
    }
}
