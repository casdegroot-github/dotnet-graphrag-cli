using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GraphRagCli.Shared.Ai;

public record EmbeddingModelConfig(
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("dimensions")] int Dimensions,
    [property: JsonPropertyName("documentPrefix")] string? DocumentPrefix,
    [property: JsonPropertyName("queryPrefix")] string? QueryPrefix);

[JsonConverter(typeof(JsonStringEnumConverter<SearchTextStrategy>))]
public enum SearchTextStrategy
{
    [JsonStringEnumMemberName("separate")] Separate,
    [JsonStringEnumMemberName("firstTwoSentences")] FirstTwoSentences
}

public record SummarizeModelConfig(
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("maxPromptChars")] int MaxPromptChars,
    [property: JsonPropertyName("concurrency")] int Concurrency,
    [property: JsonPropertyName("searchTextStrategy")] SearchTextStrategy SearchTextStrategy = SearchTextStrategy.Separate);

public record ModelDefaults(
    [property: JsonPropertyName("embedding")] string Embedding,
    [property: JsonPropertyName("summarize")] string Summarize);

public record ModelsConfig(
    [property: JsonPropertyName("embedding")] Dictionary<string, EmbeddingModelConfig> Embedding,
    [property: JsonPropertyName("summarize")] Dictionary<string, SummarizeModelConfig> Summarize,
    [property: JsonPropertyName("defaults")] ModelDefaults Defaults);

public static class ModelConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string ConfigDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".graphragcli");

    public static string ConfigPath => Path.Combine(ConfigDir, "models.json");

    public static ModelsConfig Load()
    {
        if (File.Exists(ConfigPath))
        {
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<ModelsConfig>(json, JsonOptions)
                ?? throw new InvalidOperationException($"Failed to parse {ConfigPath}");
        }

        // Seed from embedded resource
        var config = LoadEmbeddedDefaults();
        Save(config);
        return config;
    }

    public static void Save(ModelsConfig config)
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }

    public static EmbeddingModelConfig GetEmbeddingModel(this ModelsConfig config, string? modelName)
    {
        var name = modelName ?? config.Defaults.Embedding;
        if (config.Embedding.TryGetValue(name, out var model))
            return model;
        throw new InvalidOperationException(
            $"Embedding model '{name}' not found in models.json. " +
            $"Available: {string.Join(", ", config.Embedding.Keys)}");
    }

    public static SummarizeModelConfig GetSummarizeModel(this ModelsConfig config, string? modelName)
    {
        var name = modelName ?? config.Defaults.Summarize;
        if (config.Summarize.TryGetValue(name, out var model))
            return model;
        throw new InvalidOperationException(
            $"Summarize model '{name}' not found in models.json. " +
            $"Available: {string.Join(", ", config.Summarize.Keys)}");
    }

    public static bool TryGetEmbeddingModel(this ModelsConfig config, string? modelName, out EmbeddingModelConfig? model, out string? error)
    {
        var name = modelName ?? config.Defaults.Embedding;
        if (config.Embedding.TryGetValue(name, out model))
        {
            error = null;
            return true;
        }
        model = null;
        error = $"Embedding model '{name}' not found in models.json. Available: {string.Join(", ", config.Embedding.Keys)}";
        return false;
    }

    public static bool TryGetSummarizeModel(this ModelsConfig config, string? modelName, out SummarizeModelConfig? model, out string? error)
    {
        var name = modelName ?? config.Defaults.Summarize;
        if (config.Summarize.TryGetValue(name, out model))
        {
            error = null;
            return true;
        }
        model = null;
        error = $"Summarize model '{name}' not found in models.json. Available: {string.Join(", ", config.Summarize.Keys)}";
        return false;
    }

    public static string? ValidateSummarizeOptions(this ModelsConfig config, string? modelName, bool batch, int? parallel)
    {
        if (!config.TryGetSummarizeModel(modelName, out var modelConfig, out var error))
            return error;

        if (batch && modelConfig!.Provider != "claude")
            return "--batch is only supported with Claude models";
        if (batch && parallel is > 0)
            return "--batch and --parallel are mutually exclusive";
        if (parallel is < 1)
            return "--parallel must be at least 1";
        if (parallel > modelConfig!.Concurrency)
            return $"--parallel {parallel} exceeds max concurrency for {modelConfig.Provider} ({modelConfig.Concurrency})";

        return null;
    }

    public static string ResolveEmbeddingModelName(this ModelsConfig config, string? modelName) =>
        modelName ?? config.Defaults.Embedding;

    public static string ResolveSummarizeModelName(this ModelsConfig config, string? modelName) =>
        modelName ?? config.Defaults.Summarize;

    private static ModelsConfig LoadEmbeddedDefaults()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("models.json"))
            ?? throw new InvalidOperationException("Embedded models.json not found");

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        return JsonSerializer.Deserialize<ModelsConfig>(stream, JsonOptions)
            ?? throw new InvalidOperationException("Failed to parse embedded models.json");
    }
}
