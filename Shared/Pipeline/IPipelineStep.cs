namespace GraphRagCli.Shared.Pipeline;

public enum StepOutcome { Success, Skipped, Failed }

public record StepResult(StepOutcome Outcome, string? Message = null)
{
    public static StepResult Success(string? message = null) => new(StepOutcome.Success, message);
    public static StepResult Skipped(string? message = null) => new(StepOutcome.Skipped, message);
    public static StepResult Failed(string message) => new(StepOutcome.Failed, message);
}

public record PipelineResult(List<(string StepName, StepResult Result)> Steps)
{
    public bool HasFailures => Steps.Any(s => s.Result.Outcome == StepOutcome.Failed);
}

public interface IPipelineStep<TContext>
{
    string Name { get; }
    string Description { get; }
    Task<StepResult> ExecuteAsync(TContext context, CancellationToken ct = default);
    bool ShouldSkip(TContext context) => false;
}
