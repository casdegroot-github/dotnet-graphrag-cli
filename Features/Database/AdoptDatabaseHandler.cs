using System.CommandLine;
using Albatross.CommandLine;

namespace GraphRagCli.Features.Database;

public class AdoptDatabaseHandler(
    DatabaseService databaseService,
    ParseResult result,
    AdoptDatabaseParams parameters) : BaseHandler<AdoptDatabaseParams>(result, parameters)
{
    public override async Task<int> InvokeAsync(CancellationToken ct)
    {
        var adoptResult = await databaseService.AdoptAsync(parameters.Container);

        Console.WriteLine(adoptResult.Message);

        if (adoptResult.BoltPort.HasValue)
            OutputHelper.PrintMcpJson(parameters.Container, adoptResult.BoltPort.Value, adoptResult.Password!);

        return adoptResult.Message.Contains("not found") ? 1 : 0;
    }
}
