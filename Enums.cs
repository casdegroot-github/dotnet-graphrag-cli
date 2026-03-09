namespace CodeGraphIndexer;

public enum Provider { Ollama, Claude }
public enum SearchMode { Hybrid, Vector }

public record ProviderConfig(
    Provider Provider, string FieldPrefix,
    int MaxSourceLength, int MaxContextChars, int MaxNamespaceMembers)
{
    public static ProviderConfig For(Provider p) => p switch
    {
        Provider.Claude => new(p, "claude_", 0, 50_000, 100),
        _ => new(p, "", 8_000, 4_000, 30),
    };
}
