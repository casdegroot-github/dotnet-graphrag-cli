using GraphRagCli.Features.Summarize.Services;
using GraphRagCli.Shared.Pipeline;

namespace GraphRagCli.Features.Summarize.Steps;

public class ContextualSummarizeStep(IContextBuilder contextBuilder) : IPipelineStep<SummarizeContext>
{
    public string Name => "contextual";
    public string Description => "Contextual nodes — Embeddable nodes with outgoing dependencies (tiered)";

    public async Task<StepResult> ExecuteAsync(SummarizeContext ctx, CancellationToken ct = default)
    {
        var tiers = await ctx.Repo.GetContextualTiersAsync(ctx.Force);

        if (tiers.Count == 0)
        {
            Console.WriteLine("No contextual nodes to process.");
            return StepResult.Success();
        }

        foreach (var (tier, names) in tiers.OrderBy(t => t.Key))
        {
            Console.WriteLine($"\n--- Tier {tier + 1}/{tiers.Count}: {names.Count} nodes ---");
            var contextNodes = await ctx.Repo.GetContextualRawNodesAsync(ctx.Force, elementIds: names);
            var embeddable = contextNodes
                .Select(n => contextBuilder.BuildContextualEmbeddableNode(n, ctx.Config.MaxContextChars, ctx.Config.MaxSourceLength))
                .ToList();
            if (ctx.Limit.HasValue) embeddable = embeddable.Take(ctx.Limit.Value).ToList();

            await ctx.NodeSummarizer.SummarizeNodesAsync(ctx.Repo, embeddable, ctx.Sample);
        }

        return StepResult.Success();
    }
}
