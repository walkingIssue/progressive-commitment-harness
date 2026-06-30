using Pch.Providers.Errors;
using Pch.Providers.ModelRoles;

namespace Pch.Providers.LivePreflight;

public sealed class LivePreflightEvaluator
{
    private readonly LivePreflightRunner _runner;

    public LivePreflightEvaluator(LivePreflightRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public async Task<IReadOnlyList<SanitizedLivePreflightEvalRow>> EvaluateAsync(
        IReadOnlyList<LivePreflightEvalCase?>? cases,
        LivePreflightOptions options,
        CancellationToken cancellationToken = default)
    {
        if (cases is null)
        {
            return [Rejected(LivePreflightRunner.OutcomeMalformedJson)];
        }

        var rows = new List<SanitizedLivePreflightEvalRow>(cases.Count);
        foreach (var evalCase in cases)
        {
            if (evalCase?.Packet is null)
            {
                rows.Add(Rejected(LivePreflightRunner.OutcomeMalformedJson));
                continue;
            }

            try
            {
                var result = await _runner.RunAsync(evalCase.Packet, options, cancellationToken).ConfigureAwait(false);
                rows.Add(ToRow(evalCase, result, options));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                rows.Add(ExceptionRow(ex));
            }
        }

        return rows;
    }

    private static SanitizedLivePreflightEvalRow ToRow(
        LivePreflightEvalCase evalCase,
        LivePreflightResult result,
        LivePreflightOptions options)
    {
        if (!string.Equals(evalCase.Packet.PacketId, result.PacketId, StringComparison.Ordinal))
        {
            return Rejected(LivePreflightRunner.OutcomePacketMismatch);
        }

        if (result.Roles is null)
        {
            return Rejected(LivePreflightRunner.OutcomeMalformedJson, "malformed_schema");
        }

        var packetByRole = evalCase.Packet.Roles.ToDictionary(role => role.Role);
        var seen = new HashSet<LiveModelRole>();
        var roleRows = new List<SanitizedLivePreflightRoleRow>(result.Roles.Count);
        foreach (var roleResult in result.Roles)
        {
            if (!Enum.IsDefined(roleResult.Role) ||
                !packetByRole.TryGetValue(roleResult.Role, out var packetRole) ||
                !seen.Add(roleResult.Role) ||
                !string.Equals(packetRole.ProbeId, roleResult.ProbeId, StringComparison.Ordinal) ||
                !string.Equals(options.ModelFor(roleResult.Role), roleResult.ModelId, StringComparison.Ordinal) ||
                roleResult.OutputKind != "structured_output_ready")
            {
                return Rejected(LivePreflightRunner.OutcomeMalformedJson, "malformed_schema");
            }

            roleRows.Add(new SanitizedLivePreflightRoleRow(
                packetRole.Role,
                packetRole.ProbeId,
                options.ModelFor(packetRole.Role),
                options.ProviderKind,
                roleResult.OutputKind));
        }

        if (seen.Count != packetByRole.Count)
        {
            return Rejected(LivePreflightRunner.OutcomeMalformedJson, "malformed_schema");
        }

        return new SanitizedLivePreflightEvalRow(
            evalCase.Name,
            evalCase.Packet.PacketId,
            true,
            LivePreflightRunner.OutcomeAccepted,
            null,
            roleRows.OrderBy(role => role.Role).ToArray(),
            evalCase.Packet.Roles.Count,
            roleRows.Count,
            result.ResponseContentLength,
            result.Provider,
            result.Model,
            result.RequestId);
    }

    private static SanitizedLivePreflightEvalRow ExceptionRow(Exception exception) =>
        exception switch
        {
            LivePreflightGuardException guard => Rejected(guard.OutcomeCode, guard.ErrorCode),
            ProviderCreditExhaustedException => Rejected(LivePreflightRunner.OutcomeCreditExhausted, "credit_exhausted"),
            ProviderEmptyResponseException => Rejected(LivePreflightRunner.OutcomeEmptyContent, "empty_content"),
            ProviderMalformedResponseException => Rejected(LivePreflightRunner.OutcomeMalformedJson, "malformed_schema"),
            ProviderUnavailableException ex when ex.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase) => Rejected(LivePreflightRunner.OutcomeTimeout, "timeout"),
            TimeoutException => Rejected(LivePreflightRunner.OutcomeTimeout, "timeout"),
            ProviderUnavailableException => Rejected(LivePreflightRunner.OutcomeProviderUnavailable, "provider_error"),
            ProviderException => Rejected(LivePreflightRunner.OutcomeProviderUnavailable, "provider_error"),
            _ => Rejected(LivePreflightRunner.OutcomeProviderUnavailable, "provider_error")
        };

    public static SanitizedLivePreflightEvalRow Rejected(string outcomeCode, string? errorCode = null) =>
        new(
            LivePreflightRunner.RejectedRowName,
            LivePreflightRunner.RejectedRowPacketId,
            false,
            outcomeCode,
            errorCode,
            [],
            0,
            0,
            null,
            null,
            null,
            null);
}
