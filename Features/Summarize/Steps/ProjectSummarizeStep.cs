using GraphRagCli.Features.Summarize.Services;
using GraphRagCli.Shared.Pipeline;

namespace GraphRagCli.Features.Summarize.Steps;

public class ProjectSummarizeStep(IAggregationPromptBuilder promptBuilder) : IPipelineStep<SummarizeContext>
{
    public string Name => "project";
    public string Description => "Project summaries (aggregation)";

    public async Task<StepResult> ExecuteAsync(SummarizeContext ctx, CancellationToken ct = default)
    {
        var projects = await ctx.Repo.GetAggregationChildrenAsync("Project", ctx.Force);
        Console.WriteLine($"Found {projects.Count} projects needing summary.");

        if (projects.Count == 0) return StepResult.Success();

        var nodes = promptBuilder.BuildProjectNodes(projects);
        await ctx.AggregationSummarizer.SummarizeNodesAsync(ctx.Repo, nodes, sample: false);

        return StepResult.Success();
    }
}
