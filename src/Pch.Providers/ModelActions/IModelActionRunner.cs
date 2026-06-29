namespace Pch.Providers.ModelActions;

public interface IModelActionRunner
{
    Task<ModelActionRunResult> RunAsync(
        ModelActionPacket packet,
        ModelActionRunnerOptions? options = null,
        CancellationToken cancellationToken = default);
}

public interface IModelActionEvaluator
{
    Task<IReadOnlyList<ModelActionEvalResult>> EvaluateAsync(
        IReadOnlyList<ModelActionEvalCase> cases,
        ModelActionRunnerOptions? options = null,
        CancellationToken cancellationToken = default);
}
