using GraphRagCli.Features.Summarize.Services;
using GraphRagCli.Shared.Pipeline;

namespace GraphRagCli.Features.Summarize.Steps;

public class NamespaceSummarizeStep(IAggregationPromptBuilder promptBuilder) : IPipelineStep<SummarizeContext>
{
    public string Name => "namespace";
    public string Description => "Namespace summaries (aggregation)";

    public async Task<StepResult> ExecuteAsync(SummarizeContext ctx, CancellationToken ct = default)
    {
        var namespaces = await ctx.Repo.GetAggregationChildrenAsync("Namespace", ctx.Force);
        Console.WriteLine($"Found {namespaces.Count} namespaces needing summary.");

        if (namespaces.Count == 0) return StepResult.Success();

        var nodes = promptBuilder.BuildNamespaceNodes(namespaces, ctx.Config);
        await ctx.AggregationSummarizer.SummarizeNodesAsync(ctx.Repo, nodes, sample: false);

        return StepResult.Success();
    }
}
