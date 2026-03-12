using GraphRagCli.Features.Ingest.Analysis;
using Microsoft.Build.Locator;
using Microsoft.Extensions.DependencyInjection;

namespace GraphRagCli.Features.Ingest;

public static class IngestSetup
{
    public static IServiceCollection AddIngestServices(this IServiceCollection services)
    {
        MSBuildLocator.RegisterDefaults();

        services.AddSingleton<SolutionResolver>();
        services.AddSingleton<ICodeAnalyzer, CodeAnalyzer>();
        services.AddSingleton<IngestService>();

        return services;
    }
}
