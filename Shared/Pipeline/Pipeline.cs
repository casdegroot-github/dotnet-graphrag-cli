namespace GraphRagCli.Shared.Pipeline;

public class Pipeline<TContext>
{
    private readonly List<IPipelineStep<TContext>> _steps;
    private HashSet<string>? _onlySteps;
    private HashSet<string>? _skipSteps;

    public Pipeline(IEnumerable<IPipelineStep<TContext>> steps)
    {
        _steps = steps.ToList();
    }

    public Pipeline<TContext> WithFilter(IEnumerable<string>? onlySteps, IEnumerable<string>? skipSteps)
    {
        _onlySteps = onlySteps?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _skipSteps = skipSteps?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return this;
    }

    public void PrintSteps()
    {
        Console.WriteLine("Available pipeline steps:");
        foreach (var step in _steps)
            Console.WriteLine($"  {step.Name,-20} {step.Description}");
    }

    public async Task<PipelineResult> RunAsync(TContext context, CancellationToken ct = default)
    {
        var results = new List<(string StepName, StepResult Result)>();

        foreach (var step in _steps)
        {
            if (_onlySteps is { Count: > 0 } && !_onlySteps.Contains(step.Name))
            {
                results.Add((step.Name, StepResult.Skipped("filtered out by --step")));
                continue;
            }

            if (_skipSteps?.Contains(step.Name) == true)
            {
                Console.WriteLine($"Skipping step: {step.Name}");
                results.Add((step.Name, StepResult.Skipped("skipped by --skip")));
                continue;
            }

            if (step.ShouldSkip(context))
            {
                results.Add((step.Name, StepResult.Skipped()));
                continue;
            }

            Console.WriteLine($"\n=== {step.Name}: {step.Description} ===");
            try
            {
                var result = await step.ExecuteAsync(context, ct);
                results.Add((step.Name, result));

                if (result.Outcome == StepOutcome.Failed)
                {
                    Console.WriteLine($"Step {step.Name} failed: {result.Message}");
                    break;
                }
            }
            catch (Exception ex)
            {
                var result = StepResult.Failed(ex.Message);
                results.Add((step.Name, result));
                Console.WriteLine($"Step {step.Name} failed: {ex.Message}");
                break;
            }
        }

        return new PipelineResult(results);
    }
}
