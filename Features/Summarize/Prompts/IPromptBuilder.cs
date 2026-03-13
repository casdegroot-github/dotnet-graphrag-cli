using GraphRagCli.Shared.Ai;

namespace GraphRagCli.Features.Summarize.Prompts;

public interface IPromptBuilder
{
    List<EmbeddableNode> BuildPrompts(List<ReadyNodeData> nodes, SummarizeModelConfig config, string? customPrompt = null);
}
