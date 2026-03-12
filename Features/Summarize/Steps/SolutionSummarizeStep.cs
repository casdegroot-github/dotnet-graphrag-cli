using GraphRagCli.Features.Summarize.Services;
using GraphRagCli.Shared.Pipeline;

namespace GraphRagCli.Features.Summarize.Steps;

public class SolutionSummarizeStep(IAggregationPromptBuilder promptBuilder) : IPipelineStep<SummarizeContext>
{
    public string Name => "solution";
    public string Description => "Solution summary (aggregation)";

    public async Task<StepResult> ExecuteAsync(SummarizeContext ctx, CancellationToken ct = default)
    {
        var solutions = await ctx.Repo.GetAggregationChildrenAsync("Solution", ctx.Force);

        if (solutions.Count == 0)
        {
            Console.WriteLine("No solutions needing summary.");
            return StepResult.Success();
        }

        var nodes = promptBuilder.BuildSolutionNodes(solutions);
        await ctx.AggregationSummarizer.SummarizeNodesAsync(ctx.Repo, nodes, sample: false);

        return StepResult.Success();
    }
}
