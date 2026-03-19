using GraphRagCli.Shared;
using GraphRagCli.Shared.Ai;

namespace GraphRagCli.Features.Summarize.Prompts;

public interface IPromptBuilder
{
    List<EmbeddableNode> BuildPrompts(
        List<IGraphNode> nodes,
        SummarizeModelConfig config,
        string? customPrompt = null);
}
