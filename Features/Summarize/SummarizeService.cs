using GraphRagCli.Features.Summarize.Services;
using GraphRagCli.Features.Summarize.Steps;
using GraphRagCli.Shared.Ai;
using GraphRagCli.Shared.GraphDb;
using GraphRagCli.Shared.Pipeline;

namespace GraphRagCli.Features.Summarize;

public class SummarizeService(
    Neo4jSessionFactory sessionFactory,
    KernelFactory kernelFactory,
    IContextBuilder contextBuilder,
    IAggregationPromptBuilder aggregationPromptBuilder)
{
    public async Task<SummarizeResult> RunAsync(SummarizeParams parameters, CancellationToken ct = default)
    {
        var config = ProviderConfig.For(parameters.Provider);
        var resolvedModel = parameters.Model
            ?? (parameters.Provider == Provider.Claude ? "claude-haiku-4-5-20251001" : "qwen2.5-coder:7b");

        var steps = new IPipelineStep<SummarizeContext>[]
        {
            new LeafSummarizeStep(contextBuilder),
            new ContextualSummarizeStep(contextBuilder),
            new NamespaceSummarizeStep(aggregationPromptBuilder),
            new ProjectSummarizeStep(aggregationPromptBuilder),
            new SolutionSummarizeStep(aggregationPromptBuilder),
        };

        var pipeline = new Pipeline<SummarizeContext>(steps)
            .WithFilter(parameters.Step, parameters.Skip);

        if (parameters.ListSteps)
        {
            pipeline.PrintSteps();
            return SummarizeResult.Empty;
        }

        await using var driver = await sessionFactory.CreateDriverAsync(parameters.Database);
        var repo = new Neo4jSummarizeRepository(driver);

        var kernel = kernelFactory.Create();
        var summarizer = kernelFactory.GetSummarizer(kernel, parameters.Provider, resolvedModel);
        var processor = new NodeProcessor(summarizer);

        var concurrency = parameters.Parallel ?? 1;

        var concurrentSummarizer = new ConcurrentNodeSummarizer(processor, concurrency);

        ClaudeBatchService? claudeBatchService = null;
        INodeSummarizer nodeSummarizer;
        if (parameters.Batch)
        {
            claudeBatchService = new ClaudeBatchService(resolvedModel);
            nodeSummarizer = new BatchNodeSummarizer(claudeBatchService);
        }
        else
        {
            nodeSummarizer = concurrentSummarizer;
        }

        var ctx = new SummarizeContext(
            repo, nodeSummarizer,
            AggregationSummarizer: concurrentSummarizer,
            config, parameters.Force, parameters.Limit, parameters.Sample);

        Console.WriteLine($"Using {parameters.Provider} for summaries (model: {resolvedModel}, concurrency: {concurrency})");

        var pipelineResult = await pipeline.RunAsync(ctx, ct);

        return new SummarizeResult(
            ResolvedModel: resolvedModel,
            Concurrency: concurrency,
            HasFailures: pipelineResult.HasFailures,
            ClaudeBatchService: claudeBatchService);
    }
}

public record SummarizeResult(
    string ResolvedModel,
    int Concurrency,
    bool HasFailures,
    ClaudeBatchService? ClaudeBatchService)
{
    public static readonly SummarizeResult Empty = new("", 0, false, null);
    public bool IsEmpty => ResolvedModel == "";
}