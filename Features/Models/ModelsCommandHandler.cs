using System.CommandLine;
using Albatross.CommandLine;
using GraphRagCli.Shared.Ai;

namespace GraphRagCli.Features.Models;

public class ModelsListHandler(
    ParseResult result,
    ModelsListParams parameters) : BaseHandler<ModelsListParams>(result, parameters)
{
    public override Task<int> InvokeAsync(CancellationToken ct)
    {
        var config = ModelConfigLoader.Load();

        Console.WriteLine("Embedding models:");
        foreach (var (name, model) in config.Embedding)
        {
            var isDefault = name == config.Defaults.Embedding;
            var marker = isDefault ? "* " : "  ";
            var defaultTag = isDefault ? "  (default)" : "";
            Console.WriteLine($"  {marker}{name,-30} {model.Provider,-8} {model.Dimensions,4} dims{defaultTag}");
        }

        Console.WriteLine();
        Console.WriteLine("Summarize models:");
        foreach (var (name, model) in config.Summarize)
        {
            var isDefault = name == config.Defaults.Summarize;
            var marker = isDefault ? "* " : "  ";
            var defaultTag = isDefault ? "  (default)" : "";
            Console.WriteLine($"  {marker}{name,-30} {model.Provider,-8} {model.MaxPromptChars,5} chars  concurrency: {model.Concurrency}{defaultTag}");
        }

        return Task.FromResult(0);
    }
}

public class ModelsAddHandler(
    ParseResult result,
    ModelsAddParams parameters) : BaseHandler<ModelsAddParams>(result, parameters)
{
    public override Task<int> InvokeAsync(CancellationToken ct)
    {
        var config = ModelConfigLoader.Load();

        switch (parameters.Type.ToLowerInvariant())
        {
            case "embedding":
                if (!parameters.Dimensions.HasValue)
                {
                    Console.WriteLine("Error: --dimensions is required for embedding models");
                    return Task.FromResult(1);
                }
                config.Embedding[parameters.Name] = new EmbeddingModelConfig(
                    parameters.Provider, parameters.Dimensions.Value,
                    parameters.DocumentPrefix, parameters.QueryPrefix);
                break;

            case "summarize":
                if (!parameters.MaxPromptChars.HasValue)
                {
                    Console.WriteLine("Error: --max-prompt-chars is required for summarize models");
                    return Task.FromResult(1);
                }
                config.Summarize[parameters.Name] = new SummarizeModelConfig(
                    parameters.Provider, parameters.MaxPromptChars.Value, parameters.Concurrency);
                break;

            default:
                Console.WriteLine("Error: Type must be 'embedding' or 'summarize'");
                return Task.FromResult(1);
        }

        ModelConfigLoader.Save(config);
        Console.WriteLine($"Added {parameters.Type} model '{parameters.Name}'");
        return Task.FromResult(0);
    }
}

public class ModelsRemoveHandler(
    ParseResult result,
    ModelsRemoveParams parameters) : BaseHandler<ModelsRemoveParams>(result, parameters)
{
    public override Task<int> InvokeAsync(CancellationToken ct)
    {
        var config = ModelConfigLoader.Load();

        if (parameters.Name == config.Defaults.Embedding || parameters.Name == config.Defaults.Summarize)
        {
            Console.WriteLine($"Error: Cannot remove '{parameters.Name}' — it is the current default. Change the default first.");
            return Task.FromResult(1);
        }

        var removed = config.Embedding.Remove(parameters.Name) || config.Summarize.Remove(parameters.Name);
        if (!removed)
        {
            Console.WriteLine($"Error: Model '{parameters.Name}' not found");
            return Task.FromResult(1);
        }

        ModelConfigLoader.Save(config);
        Console.WriteLine($"Removed model '{parameters.Name}'");
        return Task.FromResult(0);
    }
}

public class ModelsDefaultHandler(
    ParseResult result,
    ModelsDefaultParams parameters) : BaseHandler<ModelsDefaultParams>(result, parameters)
{
    public override Task<int> InvokeAsync(CancellationToken ct)
    {
        var config = ModelConfigLoader.Load();

        switch (parameters.Type.ToLowerInvariant())
        {
            case "embedding":
                if (!config.Embedding.ContainsKey(parameters.Name))
                {
                    Console.WriteLine($"Error: Embedding model '{parameters.Name}' not found. Add it first.");
                    return Task.FromResult(1);
                }
                config = config with { Defaults = config.Defaults with { Embedding = parameters.Name } };
                break;

            case "summarize":
                if (!config.Summarize.ContainsKey(parameters.Name))
                {
                    Console.WriteLine($"Error: Summarize model '{parameters.Name}' not found. Add it first.");
                    return Task.FromResult(1);
                }
                config = config with { Defaults = config.Defaults with { Summarize = parameters.Name } };
                break;

            default:
                Console.WriteLine("Error: Type must be 'embedding' or 'summarize'");
                return Task.FromResult(1);
        }

        ModelConfigLoader.Save(config);
        Console.WriteLine($"Default {parameters.Type} model set to '{parameters.Name}'");
        return Task.FromResult(0);
    }
}
