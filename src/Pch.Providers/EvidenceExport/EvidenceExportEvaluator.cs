namespace Pch.Providers.EvidenceExport;

public sealed class EvidenceExportEvaluator
{
    public const string OutcomeExportReady = "evidence_export_ready";
    public const string OutcomePacketIdMismatch = "evidence_export_packet_id_mismatch";
    public const string OutcomeResultMismatch = "evidence_export_result_mismatch";
    public const string OutcomeUnsupported = "evidence_export_unsupported";
    public const string OutcomeMalformed = "evidence_export_malformed";
    public const string OutcomeError = "evidence_export_error";

    private readonly IEvidenceExportProvider _provider;

    public EvidenceExportEvaluator(IEvidenceExportProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public async Task<IReadOnlyList<SanitizedEvidenceExportEvalRow>> EvaluateAsync(
        IReadOnlyList<EvidenceExportEvalCase> cases,
        EvidenceExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cases);

        var rows = new List<SanitizedEvidenceExportEvalRow>(cases.Count);
        foreach (var evalCase in cases)
        {
            try
            {
                var result = await _provider.ExportAsync(evalCase.Packet, options, cancellationToken).ConfigureAwait(false);
                rows.Add(ToRow(evalCase, result));
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

    private static SanitizedEvidenceExportEvalRow ToRow(
        EvidenceExportEvalCase evalCase,
        EvidenceExportResult result)
    {
        if (!string.Equals(evalCase.Packet.PacketId, result.PacketId, StringComparison.Ordinal))
        {
            return Rejected(evalCase.Name, evalCase.Packet.PacketId, OutcomePacketIdMismatch);
        }

        var outcome = result.Kind switch
        {
            EvidenceExportResultKind.ExportReady => OutcomeExportReady,
            EvidenceExportResultKind.Unsupported => OutcomeUnsupported,
            EvidenceExportResultKind.Malformed => OutcomeMalformed,
            _ => OutcomeMalformed
        };

        if (outcome != OutcomeExportReady)
        {
            return Rejected(evalCase.Name, evalCase.Packet.PacketId, outcome);
        }

        if (!ExportMatchesPacket(evalCase.Packet, result.Export))
        {
            return Rejected(evalCase.Name, evalCase.Packet.PacketId, OutcomeResultMismatch);
        }

        var evidenceIds = evalCase.Packet.Evidence
            .Select(evidence => evidence.EvidenceId)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var slotIds = evalCase.Packet.HoldOutcomes
            .Select(hold => hold.SlotId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var candidateIds = evalCase.Packet.HoldOutcomes
            .Select(hold => hold.CandidateId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return new SanitizedEvidenceExportEvalRow(
            evalCase.Name,
            evalCase.Packet.PacketId,
            true,
            OutcomeExportReady,
            null,
            evalCase.Packet.Summary.PlanId,
            evalCase.Packet.Summary.SelectedCandidateCount,
            evalCase.Packet.Summary.DeferredCandidateCount,
            evalCase.Packet.Summary.PreparedHoldCount,
            evalCase.Packet.Summary.EvidenceCount,
            evidenceIds,
            slotIds,
            candidateIds,
            result.ResponseContentLength,
            result.Provider,
            result.Model,
            result.RequestId);
    }

    private static bool ExportMatchesPacket(EvidenceExportPacket packet, TripPlanEvidenceExport export)
    {
        return string.Equals(packet.Summary.PlanId, export.PlanId, StringComparison.Ordinal) &&
            packet.Summary.DayCount == export.DayCount &&
            packet.Summary.SelectedCandidateCount == export.SelectedCandidateCount &&
            packet.Summary.DeferredCandidateCount == export.DeferredCandidateCount &&
            packet.Summary.PreparedHoldCount == export.PreparedHoldCount &&
            SetEquals(packet.Evidence.Select(evidence => evidence.EvidenceId), export.EvidenceIds) &&
            SetEquals(packet.HoldOutcomes.Select(hold => hold.SlotId), export.SlotIds) &&
            SetEquals(packet.HoldOutcomes.Select(hold => hold.CandidateId), export.CandidateIds);
    }

    private static bool SetEquals(IEnumerable<string> expected, IReadOnlyList<string> actual)
    {
        var expectedSet = new HashSet<string>(expected, StringComparer.Ordinal);
        return expectedSet.SetEquals(actual);
    }

    private static SanitizedEvidenceExportEvalRow Rejected(
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
            null,
            0,
            0,
            0,
            0,
            [],
            [],
            [],
            null,
            null,
            null,
            null);

    private static string ToErrorCode(Exception exception) =>
        exception.GetType().Name.Contains("Provider", StringComparison.Ordinal)
            ? "provider_error"
            : "evidence_export_error";
}
