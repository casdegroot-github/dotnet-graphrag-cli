using Microsoft.Extensions.DependencyInjection;

namespace GraphRagCli.Shared.GraphDb;

public static class GraphDbSetup
{
    public static IServiceCollection AddGraphDbServices(this IServiceCollection services)
    {
        services.AddSingleton<Neo4jSessionFactory>();
        return services;
    }
}
