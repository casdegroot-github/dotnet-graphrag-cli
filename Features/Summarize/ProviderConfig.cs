using GraphRagCli.Shared.Ai;

namespace GraphRagCli.Features.Summarize;

public record ProviderConfig(
    Provider Provider,
    int MaxSourceLength, int MaxContextChars, int MaxNamespaceMembers, int MaxConcurrency)
{
    public static ProviderConfig For(Provider p) => p switch
    {
        Provider.Claude => new(p, 0, 50_000, 100, 30),
        Provider.Ollama => new(p, 8_000, 4_000, 30, 4),
        _ => throw new NotSupportedException($"Provider {p} is not supported for summarization")
    };
}
