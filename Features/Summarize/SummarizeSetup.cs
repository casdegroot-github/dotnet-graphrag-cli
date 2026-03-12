using GraphRagCli.Features.Summarize.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GraphRagCli.Features.Summarize;

public static class SummarizeSetup
{
    public static IServiceCollection AddSummarizeServices(this IServiceCollection services)
    {
        services.AddSingleton<IContextBuilder, ContextBuilder>();
        services.AddSingleton<IAggregationPromptBuilder, AggregationPromptBuilder>();
        services.AddSingleton<SummarizeService>();
        return services;
    }
}
