namespace GraphRagCli.Shared.Ai;

public interface ITextEmbedder
{
    Task<float[]> EmbedDocumentAsync(string text);
    Task<float[]> EmbedQueryAsync(string text);
}
