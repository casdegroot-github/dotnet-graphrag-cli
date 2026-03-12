using GraphRagCli.Features.Summarize.Services;
using GraphRagCli.Shared.Pipeline;

namespace GraphRagCli.Features.Summarize.Steps;

public class LeafSummarizeStep(IContextBuilder contextBuilder) : IPipelineStep<SummarizeContext>
{
    public string Name => "leaf";
    public string Description => "Leaf nodes — Embeddable nodes without outgoing dependencies";

    public async Task<StepResult> ExecuteAsync(SummarizeContext ctx, CancellationToken ct = default)
    {
        var rawNodes = await ctx.Repo.GetLeafRawNodesAsync(ctx.Force);
        var embeddable = rawNodes
            .Select(n => contextBuilder.BuildEmbeddableNode(n, null, ctx.Config.MaxSourceLength))
            .ToList();
        if (ctx.Limit.HasValue) embeddable = embeddable.Take(ctx.Limit.Value).ToList();

        var changed = await ctx.NodeSummarizer.SummarizeNodesAsync(ctx.Repo, embeddable, ctx.Sample);

        if (changed.Count > 0)
        {
            var staleCount = await ctx.Repo.MarkStaleDependentsAsync(changed);
            Console.WriteLine($"Marked {staleCount} dependents as needing re-summarization.");
        }

        return StepResult.Success();
    }
}
