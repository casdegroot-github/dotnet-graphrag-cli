using Microsoft.Extensions.DependencyInjection;

namespace GraphRagCli.Shared.Ai;

public static class AiSetup
{
    public static IServiceCollection AddAiServices(this IServiceCollection services)
    {
        services.AddSingleton(_ => ModelConfigLoader.Load());
        services.AddSingleton<KernelFactory>();
        return services;
    }
}
