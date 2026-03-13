namespace GraphRagCli.Features.Embed;

public record EmbeddableNode(string ElementId, string FullName, string Summary, string? SearchText, List<string>? Tags);

public record GraphMeta(string? EmbeddingModel, int? EmbeddingDimensions);

public record EmbedResult(int Total, int Embedded, int Failed, bool CentralityComputed)
{
    public static readonly EmbedResult Empty = new(0, 0, 0, false);
    public bool IsEmpty => Total == 0;
}
