using Anthropic;
using Microsoft.Extensions.AI;
using OllamaSharp;

namespace GraphRagCli.Shared.Ai;

public class KernelFactory(string ollamaUrl = "http://localhost:11434")
{
    public ITextEmbedder CreateTextEmbedder(string embeddingModel, EmbeddingModelConfig config)
    {
        var client = new OllamaApiClient(new Uri(ollamaUrl)) { SelectedModel = embeddingModel };
        return new TextEmbedder(client, config.DocumentPrefix, config.QueryPrefix);
    }

    public Features.Summarize.Summarizers.Summarizer GetSummarizer(SummarizeModelConfig config, string model)
    {
        var chatClient = CreateChatClient(config.Provider);
        return new Features.Summarize.Summarizers.Summarizer(chatClient, model);
    }

    private IChatClient CreateChatClient(string provider) => provider switch
    {
        "ollama" => new OllamaApiClient(new HttpClient { BaseAddress = new Uri(ollamaUrl), Timeout = TimeSpan.FromMinutes(5) }),
        "claude" => new AnthropicClient().Beta.AsIChatClient("claude-haiku-4-5-20251001"),
        _ => throw new NotSupportedException($"Unknown provider: {provider}")
    };
}
