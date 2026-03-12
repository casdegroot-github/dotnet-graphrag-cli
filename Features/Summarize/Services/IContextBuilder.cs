namespace GraphRagCli.Features.Summarize.Services;

public interface IContextBuilder
{
    EmbeddableNode BuildEmbeddableNode(RawNodeData raw, string? contextSuffix, int maxSourceLength);
    EmbeddableNode BuildContextualEmbeddableNode(RawContextualNodeData raw, int maxContextChars, int maxSourceLength);
}
