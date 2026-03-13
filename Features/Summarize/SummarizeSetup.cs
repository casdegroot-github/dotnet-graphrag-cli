using GraphRagCli.Features.Summarize.Prompts;
using Microsoft.Extensions.DependencyInjection;

namespace GraphRagCli.Features.Summarize;

public static class SummarizeSetup
{
    public static IServiceCollection AddSummarizeServices(this IServiceCollection services)
    {
        services.AddSingleton<IPromptBuilder, PromptBuilder>();
        services.AddSingleton<SummarizeService>();
        return services;
    }
}
