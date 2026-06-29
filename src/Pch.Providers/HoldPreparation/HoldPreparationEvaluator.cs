namespace Pch.Providers.HoldPreparation;

public sealed class HoldPreparationEvaluator
{
    public const string OutcomePreviewReady = "hold_preparation_preview_ready";
    public const string OutcomeHoldPrepared = "hold_preparation_hold_prepared";
    public const string OutcomeMissingApproval = "hold_preparation_missing_approval";
    public const string OutcomeApprovalMismatch = "hold_preparation_approval_mismatch";
    public const string OutcomePacketIdMismatch = "hold_preparation_packet_id_mismatch";
    public const string OutcomeCandidateMismatch = "hold_preparation_candidate_mismatch";
    public const string OutcomeMalformedResult = "hold_preparation_malformed_result";
    public const string OutcomeError = "hold_preparation_error";

    private readonly IHoldPreparationAdapter _adapter;

    public HoldPreparationEvaluator(IHoldPreparationAdapter adapter)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
    }

    public async Task<IReadOnlyList<SanitizedHoldPreparationEvalRow>> EvaluateAsync(
        IReadOnlyList<HoldPreparationEvalCase> cases,
        HoldPreparationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cases);

        var rows = new List<SanitizedHoldPreparationEvalRow>(cases.Count);
        foreach (var evalCase in cases)
        {
            try
            {
                var result = await _adapter.PrepareAsync(evalCase.Packet, options, cancellationToken).ConfigureAwait(false);
                rows.Add(ToRow(evalCase, result, options));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                rows.Add(Rejected(
                    evalCase.Name,
                    evalCase.Packet.PacketId,
                    OutcomeError,
                    ToErrorCode(ex)));
            }
        }

        return rows;
    }

    private static SanitizedHoldPreparationEvalRow ToRow(
        HoldPreparationEvalCase evalCase,
        HoldPreparationResult result,
        HoldPreparationOptions? options)
    {
        if (!string.Equals(evalCase.Packet.PacketId, result.PacketId, StringComparison.Ordinal))
        {
            return Rejected(evalCase.Name, evalCase.Packet.PacketId, OutcomePacketIdMismatch);
        }

        var outcome = result.Kind switch
        {
            HoldPreparationResultKind.ApprovalMissing => OutcomeMissingApproval,
            HoldPreparationResultKind.ApprovalRejected => OutcomeApprovalMismatch,
            HoldPreparationResultKind.PreviewReady when evalCase.Packet.Operation == HoldPreparationOperation.Preview => OutcomePreviewReady,
            HoldPreparationResultKind.HoldPrepared when evalCase.Packet.Operation == HoldPreparationOperation.Hold => OutcomeHoldPrepared,
            _ => OutcomeMalformedResult
        };

        if (outcome is OutcomeMissingApproval or OutcomeApprovalMismatch or OutcomeMalformedResult)
        {
            return Rejected(evalCase.Name, evalCase.Packet.PacketId, outcome);
        }

        if (outcome == OutcomeHoldPrepared)
        {
            var approvalOutcome = ValidateApproval(evalCase.Packet, options);
            if (approvalOutcome is not null)
            {
                return Rejected(evalCase.Name, evalCase.Packet.PacketId, approvalOutcome);
            }
        }

        if (!SelectedCandidateSetsMatch(evalCase.Packet.SelectedCandidates, result.Candidates))
        {
            return Rejected(evalCase.Name, evalCase.Packet.PacketId, OutcomeCandidateMismatch);
        }

        var resultByKey = result.Candidates.ToDictionary(
            candidate => CandidateKey(candidate.SlotId, candidate.CandidateId),
            StringComparer.Ordinal);
        var candidateRows = evalCase.Packet.SelectedCandidates
            .Select(candidate => new SanitizedHoldCandidateRow(
                candidate.SlotId,
                candidate.CandidateId,
                candidate.Category))
            .OrderBy(candidate => candidate.SlotId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.CandidateId, StringComparer.Ordinal)
            .ToArray();

        if (candidateRows.Any(candidate =>
                resultByKey[CandidateKey(candidate.SlotId, candidate.CandidateId)].Category != candidate.Category))
        {
            return Rejected(evalCase.Name, evalCase.Packet.PacketId, OutcomeCandidateMismatch);
        }

        var expectedStatus = outcome == OutcomeHoldPrepared
            ? HoldPreparationCandidateStatus.HoldPrepared
            : HoldPreparationCandidateStatus.PreviewAvailable;
        if (result.Candidates.Any(candidate => candidate.Status != expectedStatus))
        {
            return Rejected(evalCase.Name, evalCase.Packet.PacketId, OutcomeMalformedResult);
        }

        return new SanitizedHoldPreparationEvalRow(
            evalCase.Name,
            evalCase.Packet.PacketId,
            true,
            outcome,
            null,
            candidateRows,
            candidateRows.Length,
            result.ResponseContentLength,
            result.Provider,
            result.Model,
            result.RequestId);
    }

    private static bool SelectedCandidateSetsMatch(
        IReadOnlyList<SelectedItineraryCandidate> packetCandidates,
        IReadOnlyList<HoldPreparationCandidateResult> resultCandidates)
    {
        var packetKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in packetCandidates)
        {
            if (!packetKeys.Add(CandidateKey(candidate.SlotId, candidate.CandidateId)))
            {
                return false;
            }
        }

        var resultKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in resultCandidates)
        {
            if (!resultKeys.Add(CandidateKey(candidate.SlotId, candidate.CandidateId)))
            {
                return false;
            }
        }

        return packetKeys.SetEquals(resultKeys);
    }

    private static string? ValidateApproval(HoldPreparationPacket packet, HoldPreparationOptions? options)
    {
        if (packet.Operation != HoldPreparationOperation.Hold)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(packet.ApprovalToken))
        {
            return OutcomeMissingApproval;
        }

        var requiredApprovalToken = options?.RequiredApprovalToken ?? "mock-approval-token";
        return string.Equals(packet.ApprovalToken, requiredApprovalToken, StringComparison.Ordinal)
            ? null
            : OutcomeApprovalMismatch;
    }

    private static string CandidateKey(string slotId, string candidateId) =>
        $"{slotId}\u001F{candidateId}";

    private static SanitizedHoldPreparationEvalRow Rejected(
        string name,
        string packetId,
        string outcomeCode,
        string? errorCode = null) =>
        new(
            name,
            packetId,
            false,
            outcomeCode,
            errorCode,
            [],
            0,
            null,
            null,
            null,
            null);

    private static string ToErrorCode(Exception exception) =>
        exception.GetType().Name.Contains("Provider", StringComparison.Ordinal)
            ? "provider_error"
            : "hold_preparation_error";
}
