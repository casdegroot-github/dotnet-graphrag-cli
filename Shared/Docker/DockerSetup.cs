using Docker.DotNet;
using Microsoft.Extensions.DependencyInjection;

namespace GraphRagCli.Shared.Docker;

public static class DockerSetup
{
    public static IServiceCollection AddDockerServices(this IServiceCollection services)
    {
        services.AddSingleton<IDockerClient>(_ => new DockerClientConfiguration().CreateClient());
        services.AddSingleton<Neo4jContainerClient>();
        return services;
    }
}
