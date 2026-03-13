using System.CommandLine;
using Albatross.CommandLine;
using GraphRagCli.Shared.Ai;
using GraphRagCli.Shared.GraphDb;

namespace GraphRagCli.Features.Embed;

public class EmbedCommandHandler(
    Neo4jSessionFactory sessionFactory,
    KernelFactory kernelFactory,
    ModelsConfig modelsConfig,
    EmbedService embedService,
    ParseResult result,
    EmbedParams parameters) : BaseHandler<EmbedParams>(result, parameters)
{
    public override async Task<int> InvokeAsync(CancellationToken ct)
    {
        var modelName = modelsConfig.ResolveEmbeddingModelName(parameters.Model);
        var embeddingConfig = modelsConfig.GetEmbeddingModel(parameters.Model);

        Console.WriteLine("GraphRagCli - Embed");
        Console.WriteLine($"  Database:   {parameters.Database ?? "(auto-detect)"}");
        Console.WriteLine($"  Model:      {modelName}");
        Console.WriteLine($"  Dimensions: {embeddingConfig.Dimensions}");
        Console.WriteLine($"  Force:      {parameters.Force}");
        Console.WriteLine();

        try
        {
            await using var driver = await sessionFactory.CreateDriverAsync(parameters.Database);
            var embedder = kernelFactory.CreateTextEmbedder(modelName, embeddingConfig);

            var embedResult = await embedService.EmbedAsync(
                driver, embedder, parameters, modelName, embeddingConfig.Dimensions);

            if (embedResult.IsEmpty)
                Console.WriteLine("No nodes to embed.");

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}
