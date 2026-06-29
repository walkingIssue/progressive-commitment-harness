namespace Pch.Providers.ModelActions;

public sealed class ModelActionEvaluator : IModelActionEvaluator
{
    private readonly IModelActionRunner _runner;

    public ModelActionEvaluator(IModelActionRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public async Task<IReadOnlyList<ModelActionEvalResult>> EvaluateAsync(
        IReadOnlyList<ModelActionEvalCase> cases,
        ModelActionRunnerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cases);

        var results = new List<ModelActionEvalResult>(cases.Count);
        foreach (var evalCase in cases)
        {
            try
            {
                var run = await _runner.RunAsync(evalCase.Packet, options, cancellationToken).ConfigureAwait(false);
                results.Add(new ModelActionEvalResult(
                    evalCase.Name,
                    string.Equals(evalCase.ExpectedActionName, run.ActionName, StringComparison.Ordinal),
                    evalCase.ExpectedActionName,
                    run.ActionName,
                    null));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(new ModelActionEvalResult(
                    evalCase.Name,
                    false,
                    evalCase.ExpectedActionName,
                    null,
                    ex.Message));
            }
        }

        return results;
    }
}
