using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using OllamaSharp;

namespace GraphRagCli.Shared.Ai;

/// <summary>
/// Creates Semantic Kernel instances with both providers registered.
/// Model is passed at call time via ChatOptions.ModelId.
/// </summary>
public class KernelFactory(string ollamaUrl = "http://localhost:11434")
{
    public Kernel Create()
    {
        var builder = Kernel.CreateBuilder();

        // Ollama: embeddings + chat
        var embeddingClient = new OllamaApiClient(new Uri(ollamaUrl)) { SelectedModel = "snowflake-arctic-embed2" };
        builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(embeddingClient);
        builder.Services.AddKeyedSingleton("ollama", (IChatClient)new OllamaApiClient(new Uri(ollamaUrl)));

        // Claude: chat via Anthropic SDK → IChatClient bridge (Beta supports structured output)
        builder.Services.AddKeyedSingleton<IChatClient>("claude", (_, _) =>
            (IChatClient)new AnthropicClient().Beta.AsIChatClient("claude-haiku-4-5-20251001"));

        return builder.Build();
    }

    public ITextEmbedder GetTextEmbedder(Kernel kernel) =>
        new TextEmbedder(
            kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
            documentPrefix: "search_document: ",
            queryPrefix: "search_query: ");

    public Features.Summarize.Summarizer GetSummarizer(Kernel kernel, Provider provider, string model)
    {
        var key = provider.ToString().ToLowerInvariant();
        var chatClient = kernel.Services.GetRequiredKeyedService<IChatClient>(key);
        return new Features.Summarize.Summarizer(chatClient, model);
    }
}
