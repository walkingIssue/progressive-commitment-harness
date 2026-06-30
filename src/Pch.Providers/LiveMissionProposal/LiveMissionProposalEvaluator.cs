using Pch.Providers.Errors;
using Pch.Providers.ModelRoles;

namespace Pch.Providers.LiveMissionProposal;

public sealed class LiveMissionProposalEvaluator
{
    private readonly LiveMissionProposalRunner _runner;

    public LiveMissionProposalEvaluator(LiveMissionProposalRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public async Task<IReadOnlyList<SanitizedLiveMissionProposalEvalRow>> EvaluateAsync(
        IReadOnlyList<LiveMissionProposalEvalCase?>? cases,
        LiveMissionProposalOptions options,
        CancellationToken cancellationToken = default)
    {
        if (cases is null)
        {
            return [Rejected(LiveMissionProposalRunner.OutcomeSchemaInvalid, "malformed_input")];
        }

        var rows = new List<SanitizedLiveMissionProposalEvalRow>(cases.Count);
        foreach (var evalCase in cases)
        {
            if (evalCase?.Packet is null)
            {
                rows.Add(Rejected(LiveMissionProposalRunner.OutcomeSchemaInvalid, "malformed_input"));
                continue;
            }

            try
            {
                var result = await _runner.RunAsync(evalCase.Packet, options, cancellationToken).ConfigureAwait(false);
                rows.Add(ToRow(evalCase, result));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                rows.Add(ExceptionRow(ex));
            }
        }

        return rows;
    }

    private static SanitizedLiveMissionProposalEvalRow ToRow(
        LiveMissionProposalEvalCase evalCase,
        LiveMissionProposalResult result)
    {
        if (!IsSafePacket(evalCase.Packet) ||
            !string.Equals(evalCase.Packet.PacketId, result.PacketId, StringComparison.Ordinal) ||
            !string.Equals(evalCase.Packet.SessionId, result.SessionId, StringComparison.Ordinal) ||
            evalCase.Packet.Role != result.Role)
        {
            return Rejected(LiveMissionProposalRunner.OutcomePacketMismatch);
        }

        if (!evalCase.Packet.AllowedOutputKinds.Contains(result.OutputKind, StringComparer.Ordinal))
        {
            return Rejected(LiveMissionProposalRunner.OutcomeUnsupportedValue, "unsupported_output_kind");
        }

        if (result.HasUnsafeValue)
        {
            return Rejected(LiveMissionProposalRunner.OutcomeUnsafeValueRedacted, "unsafe_value_redacted");
        }

        if (!Enum.IsDefined(result.Role) ||
            !Enum.IsDefined(result.MissionKind) ||
            result.Fields is null ||
            result.Commitments is null ||
            result.PendingConfirmations is null)
        {
            return Rejected(LiveMissionProposalRunner.OutcomeSchemaInvalid, "malformed_schema");
        }

        var fieldPaths = new List<string>(result.Fields.Count);
        foreach (var field in result.Fields)
        {
            if (field is null ||
                !IsSafeFieldPath(field.FieldPath) ||
                !Enum.IsDefined(field.AuthoritySource) ||
                !AllSafeEvidence(field.EvidenceIds))
            {
                return Rejected(LiveMissionProposalRunner.OutcomeUnsupportedValue, "unsupported_proposal_value");
            }

            fieldPaths.Add(field.FieldPath);
        }

        var commitmentKinds = new List<LiveMissionCommitmentKind>(result.Commitments.Count);
        foreach (var commitment in result.Commitments)
        {
            if (commitment is null ||
                !LiveMissionProposalRunner.IsSafeIdentifier(commitment.CommitmentId) ||
                !Enum.IsDefined(commitment.CommitmentKind) ||
                !Enum.IsDefined(commitment.Priority) ||
                !Enum.IsDefined(commitment.AuthoritySource) ||
                !AllSafeEvidence(commitment.EvidenceIds))
            {
                return Rejected(LiveMissionProposalRunner.OutcomeUnsupportedValue, "unsupported_proposal_value");
            }

            commitmentKinds.Add(commitment.CommitmentKind);
        }

        var pendingReasons = new List<LiveMissionPendingReason>(result.PendingConfirmations.Count);
        foreach (var pending in result.PendingConfirmations)
        {
            if (pending is null ||
                !LiveMissionProposalRunner.IsSafeIdentifier(pending.ConfirmationId) ||
                !IsSafeFieldPath(pending.FieldPath) ||
                !Enum.IsDefined(pending.ReasonCode) ||
                !Enum.IsDefined(pending.AuthoritySource) ||
                !AllSafeEvidence(pending.EvidenceIds))
            {
                return Rejected(LiveMissionProposalRunner.OutcomeUnsupportedValue, "unsupported_proposal_value");
            }

            pendingReasons.Add(pending.ReasonCode);
        }

        return new SanitizedLiveMissionProposalEvalRow(
            evalCase.Name,
            evalCase.Packet.PacketId,
            true,
            LiveMissionProposalRunner.OutcomeAccepted,
            null,
            evalCase.Packet.SessionId,
            evalCase.Packet.Role,
            result.OutputKind,
            result.MissionKind,
            fieldPaths.OrderBy(path => path, StringComparer.Ordinal).ToArray(),
            commitmentKinds.OrderBy(kind => kind).ToArray(),
            pendingReasons.OrderBy(reason => reason).ToArray(),
            result.Fields.Count,
            result.Commitments.Count,
            result.PendingConfirmations.Count,
            result.ResponseContentLength,
            result.Provider,
            result.Model,
            result.RequestId);
    }

    private static bool IsSafePacket(LiveMissionProposalPacket packet) =>
        LiveMissionProposalRunner.IsSafeIdentifier(packet.PacketId) &&
        LiveMissionProposalRunner.IsSafeIdentifier(packet.SessionId) &&
        Enum.IsDefined(packet.Role) &&
        packet.AllowedOutputKinds is not null &&
        packet.AllowedOutputKinds.Count > 0 &&
        packet.AllowedOutputKinds.All(kind => LiveMissionProposalRunner.IsSafeIdentifier(kind));

    private static bool IsSafeFieldPath(string? fieldPath) =>
        !string.IsNullOrWhiteSpace(fieldPath) &&
        fieldPath.StartsWith("/mission/", StringComparison.Ordinal) &&
        LiveMissionProposalRunner.IsSafeIdentifier(fieldPath);

    private static bool AllSafeEvidence(IReadOnlyList<string>? evidenceIds) =>
        evidenceIds is not null && evidenceIds.All(LiveMissionProposalRunner.IsSafeIdentifier);

    private static SanitizedLiveMissionProposalEvalRow ExceptionRow(Exception exception) =>
        exception switch
        {
            LiveMissionProposalGuardException guard => Rejected(guard.OutcomeCode, guard.ErrorCode),
            ProviderCreditExhaustedException => Rejected(LiveMissionProposalRunner.OutcomeCreditExhausted, "credit_exhausted"),
            ProviderEmptyResponseException => Rejected(LiveMissionProposalRunner.OutcomeEmptyContent, "empty_content"),
            ProviderMalformedResponseException ex when ex.Message.Contains("schema", StringComparison.OrdinalIgnoreCase) => Rejected(LiveMissionProposalRunner.OutcomeSchemaInvalid, "malformed_schema"),
            ProviderMalformedResponseException => Rejected(LiveMissionProposalRunner.OutcomeMalformedJson, "malformed_json"),
            ProviderUnavailableException ex when ex.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase) => Rejected(LiveMissionProposalRunner.OutcomeTimeout, "timeout"),
            TimeoutException => Rejected(LiveMissionProposalRunner.OutcomeTimeout, "timeout"),
            ProviderUnavailableException => Rejected(LiveMissionProposalRunner.OutcomeProviderUnavailable, "provider_error"),
            ProviderException => Rejected(LiveMissionProposalRunner.OutcomeProviderUnavailable, "provider_error"),
            _ => Rejected(LiveMissionProposalRunner.OutcomeProviderUnavailable, "provider_error")
        };

    public static SanitizedLiveMissionProposalEvalRow Rejected(string outcomeCode, string? errorCode = null) =>
        new(
            LiveMissionProposalRunner.RejectedRowName,
            LiveMissionProposalRunner.RejectedRowPacketId,
            false,
            outcomeCode,
            errorCode,
            null,
            null,
            null,
            null,
            [],
            [],
            [],
            0,
            0,
            0,
            null,
            null,
            null,
            null);
}
