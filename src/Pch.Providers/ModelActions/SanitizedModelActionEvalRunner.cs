namespace Pch.Providers.ModelActions;

public sealed class SanitizedModelActionEvalRunner
{
    private readonly IModelActionRunner _runner;

    public SanitizedModelActionEvalRunner(IModelActionRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public async Task<IReadOnlyList<SanitizedModelActionEvalRow>> EvaluateAsync(
        IReadOnlyList<ModelActionEvalCase> cases,
        ModelActionRunnerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cases);

        var rows = new List<SanitizedModelActionEvalRow>(cases.Count);
        foreach (var evalCase in cases)
        {
            try
            {
                var result = await _runner.RunAsync(evalCase.Packet, options, cancellationToken).ConfigureAwait(false);
                rows.Add(new SanitizedModelActionEvalRow(
                    evalCase.Name,
                    evalCase.Packet.PacketId,
                    string.Equals(evalCase.ExpectedActionName, result.ActionName, StringComparison.Ordinal),
                    evalCase.ExpectedActionName,
                    result.ActionName,
                    null,
                    result.ResponseContentLength,
                    result.Provider,
                    result.Model,
                    result.RequestId));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                rows.Add(new SanitizedModelActionEvalRow(
                    evalCase.Name,
                    evalCase.Packet.PacketId,
                    false,
                    evalCase.ExpectedActionName,
                    null,
                    ToErrorCode(ex),
                    null,
                    null,
                    null,
                    null));
            }
        }

        return rows;
    }

    private static string ToErrorCode(Exception exception) =>
        exception switch
        {
            ModelActionRunnerException => "model_action_runner_error",
            _ when exception.GetType().Name.Contains("Provider", StringComparison.Ordinal) => "provider_error",
            _ => "unexpected_error"
        };
}
