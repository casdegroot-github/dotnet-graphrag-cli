using Microsoft.Extensions.AI;

namespace GraphRagCli.Shared.Ai;

/// <summary>
/// Provider-agnostic text embedder wrapping IEmbeddingGenerator.
/// Prefixes are configurable per embedding model (e.g. snowflake-arctic-embed2 uses "search_document: " / "search_query: ").
/// </summary>
public class TextEmbedder(
    IEmbeddingGenerator<string, Embedding<float>> generator,
    string? documentPrefix = null,
    string? queryPrefix = null) : ITextEmbedder
{
    public async Task<float[]> EmbedDocumentAsync(string text)
    {
        var input = documentPrefix is not null ? $"{documentPrefix}{text}" : text;
        var vector = await generator.GenerateVectorAsync<string, float>(input);
        return vector.ToArray();
    }

    public async Task<float[]> EmbedQueryAsync(string text)
    {
        var input = queryPrefix is not null ? $"{queryPrefix}{text}" : text;
        var vector = await generator.GenerateVectorAsync<string, float>(input);
        return vector.ToArray();
    }
}
