using System.CommandLine;
using Albatross.CommandLine;
using GraphRagCli.Features.Database;
using GraphRagCli.Features.Embed;
using GraphRagCli.Features.Ingest;
using GraphRagCli.Features.Summarize;
using GraphRagCli.Shared.Ai;
using GraphRagCli.Shared.Docker;
using GraphRagCli.Shared.GraphDb;
using Microsoft.Extensions.DependencyInjection;

await using var host = new CommandHost("GraphRagCli - Build a Code Intelligence Graph in Neo4j");
host.RegisterServices((ParseResult _, IServiceCollection services) =>
    {
        services.AddDockerServices();
        services.AddGraphDbServices();
        services.AddAiServices();
        services.AddIngestServices();
        services.AddSummarizeServices();

        services.AddSingleton<Neo4JContainerLifecycle>();
        services.AddSingleton<DatabaseService>();
        services.AddSingleton<EmbedService>();

        services.RegisterCommands();
    })
    .AddCommands()
    .Parse(args, false)
    .Build();

return await host.InvokeAsync();
