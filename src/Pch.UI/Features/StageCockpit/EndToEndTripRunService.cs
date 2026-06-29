using Pch.Core;
using Pch.Harness;
using Pch.Providers.EvidenceExport;
using Pch.Providers.Mock;
using ExportEvidenceKind = Pch.Providers.EvidenceExport.EvidenceKind;

namespace Pch.UI.Features.StageCockpit;

public sealed class EndToEndTripRunService
{
    private const string RawPromptSentinel = "RAW_END_TO_END_PROMPT_SHOULD_NOT_LEAK";
    private const string ApprovalId = "approval-itinerary-hold-activity";
    private const string ApprovalTokenValue = "ui-e2e-approval-token-not-rendered";
    private static readonly string[] KnownRunIds =
    [
        "e2e.happy-path",
        "e2e.pending-confirmation",
        "e2e.provider-mismatch",
        "e2e.wrong-slot",
        "e2e.missing-approval",
        "e2e.raw-sentinel"
    ];

    private readonly RuntimeMissionPlannerService _missionPlannerService = new();
    private readonly ItineraryDayPlannerService _itineraryDayPlannerService = new();
    private readonly TripRunSnapshotBuilder _snapshotBuilder = new();
    private readonly ItinerarySlotCompiler _slotCompiler = new();
    private readonly EvidenceExportEvaluator _evidenceExportEvaluator = new(new MockEvidenceExportProvider());

    public EndToEndTripRunResult Run(string runId)
    {
        if (!KnownRunIds.Contains(runId, StringComparer.Ordinal))
        {
            return Blocked(
                runId,
                "not_run",
                "not_run",
                TripRunSnapshotBuilder.BlockedCompilerCode,
                "not_run",
                "PCH_UI_E2E_UNKNOWN_SCENARIO",
                "End-to-end trip run scenario is not recognized.");
        }

        var session = SyntheticTripFactory.CreateSession(11);
        var promptRunId = runId == "e2e.pending-confirmation" ? "prompt.pending" : "prompt.accepted";
        var mission = _missionPlannerService.RunPrompt(session, promptRunId, PromptForRun(runId));
        if (mission.State == "blocked")
        {
            var snapshot = _snapshotBuilder.Build(session);
            return FromMissionBlocked(runId, mission, snapshot);
        }

        return runId switch
        {
            "e2e.happy-path" => CompletePath(session, runId, mission),
            "e2e.pending-confirmation" => PendingConfirmation(session, runId, mission),
            "e2e.provider-mismatch" => ItineraryBlocked(session, runId, mission, "itinerary.provider-mismatch"),
            "e2e.wrong-slot" => ItineraryBlocked(session, runId, mission, "itinerary.select.wrong-slot"),
            "e2e.missing-approval" => MissingApproval(session, runId, mission),
            "e2e.raw-sentinel" => CompletePath(session, runId, mission),
            _ => Blocked(
                runId,
                mission.IntakeOutcomeCode,
                "not_run",
                TripRunSnapshotBuilder.BlockedCompilerCode,
                "not_run",
                "PCH_UI_E2E_UNKNOWN_SCENARIO",
                "End-to-end trip run scenario is not recognized.")
        };
    }

    private EndToEndTripRunResult CompletePath(
        TripSession session,
        string runId,
        PromptIntakePlannerResult mission)
    {
        var itinerary = _itineraryDayPlannerService.Run(session, "itinerary.accepted");
        var deferred = _itineraryDayPlannerService.Run(session, "itinerary.defer-slot");
        var hold = _itineraryDayPlannerService.Run(session, "itinerary.hold.approved");
        session.RecordApproval(new ApprovalToken(ApprovalId, ApprovalTokenValue, DateTimeOffset.UtcNow));

        var snapshot = _snapshotBuilder.Build(session);
        var export = ExportIfComplete(runId, snapshot);

        return FromSnapshot(
            runId,
            "applied",
            mission,
            "itinerary_day_compiled",
            hold.Outcome.HoldOutcome,
            hold.Outcome.ApprovalId,
            null,
            null,
            snapshot,
            export,
            fallbackSelectedCount: SelectedCount(itinerary, hold),
            fallbackDeferredCount: DeferredCount(deferred));
    }

    private EndToEndTripRunResult PendingConfirmation(
        TripSession session,
        string runId,
        PromptIntakePlannerResult mission)
    {
        CompileForSnapshot(session);
        var snapshot = _snapshotBuilder.Build(session);

        return FromSnapshot(
            runId,
            "proposed",
            mission,
            "itinerary_not_run_pending_confirmation",
            "not_run",
            approvalId: null,
            errorCode: null,
            blockedReason: null,
            snapshot,
            export: null,
            fallbackSelectedCount: 0,
            fallbackDeferredCount: 0);
    }

    private EndToEndTripRunResult ItineraryBlocked(
        TripSession session,
        string runId,
        PromptIntakePlannerResult mission,
        string itineraryRunId)
    {
        var itinerary = _itineraryDayPlannerService.Run(session, itineraryRunId);
        var snapshot = _snapshotBuilder.Build(session);

        return FromSnapshot(
            runId,
            "blocked",
            mission,
            itinerary.Outcome.BlockedOutcome,
            "not_run",
            itinerary.Outcome.ApprovalId,
            itinerary.Outcome.ErrorCode,
            itinerary.Outcome.BlockedReason,
            snapshot,
            export: null,
            fallbackSelectedCount: 0,
            fallbackDeferredCount: 0);
    }

    private EndToEndTripRunResult MissingApproval(
        TripSession session,
        string runId,
        PromptIntakePlannerResult mission)
    {
        var itinerary = _itineraryDayPlannerService.Run(session, "itinerary.hold.missing-approval");
        var snapshot = _snapshotBuilder.Build(session);

        return FromSnapshot(
            runId,
            "blocked",
            mission,
            itinerary.Outcome.BlockedOutcome,
            itinerary.Outcome.HoldOutcome,
            itinerary.Outcome.ApprovalId,
            itinerary.Outcome.ErrorCode,
            itinerary.Outcome.BlockedReason,
            snapshot,
            export: null,
            fallbackSelectedCount: itinerary.Outcome.SelectedOutcome == "selected" ? 1 : 0,
            fallbackDeferredCount: 0);
    }

    private EndToEndTripRunResult FromSnapshot(
        string runId,
        string state,
        PromptIntakePlannerResult mission,
        string itineraryOutcome,
        string holdOutcome,
        string? approvalId,
        string? errorCode,
        string? blockedReason,
        TripRunSnapshotResult snapshot,
        SanitizedEvidenceExportEvalRow? export,
        int fallbackSelectedCount,
        int fallbackDeferredCount)
    {
        var selectedCount = export?.SelectedCandidateCount ?? fallbackSelectedCount;
        var deferredCount = export?.DeferredCandidateCount ?? fallbackDeferredCount;
        var evidenceExportOutcome = export?.OutcomeCode ?? "not_run";
        var provider = export?.Provider ?? mission.Provider;
        var model = export?.Model ?? mission.Model;
        var requestId = export?.RequestId ?? mission.RequestId;
        var finalErrorCode = errorCode ?? ExportErrorCode(export);
        var finalBlockedReason = blockedReason ?? ExportBlockedReason(export);

        var outcome = new EndToEndTripRunOutcomeFixture(
            runId,
            state,
            mission.PromptPacketOutcomeCode,
            mission.IntakeOutcomeCode,
            itineraryOutcome,
            mission.MemoryDigestOutcomeCode,
            state == "blocked" ? "end_to_end.blocked" : state == "proposed" ? "end_to_end.pending_confirmation" : "end_to_end.applied",
            selectedCount,
            deferredCount,
            holdOutcome,
            approvalId,
            snapshot.Code,
            evidenceExportOutcome,
            EvidencePacketId(runId),
            ExportPacketId(runId),
            finalErrorCode,
            finalBlockedReason,
            provider,
            model,
            requestId);

        return new(outcome, BuildEvidence(outcome, export));
    }

    private EndToEndTripRunResult FromMissionBlocked(
        string runId,
        PromptIntakePlannerResult mission,
        TripRunSnapshotResult snapshot)
    {
        return FromSnapshot(
            runId,
            "blocked",
            mission,
            "not_run",
            "not_run",
            approvalId: null,
            mission.ErrorCode,
            mission.BlockedReason,
            snapshot,
            export: null,
            fallbackSelectedCount: 0,
            fallbackDeferredCount: 0);
    }

    private static EndToEndTripRunResult Blocked(
        string runId,
        string missionOutcome,
        string itineraryOutcome,
        string snapshotOutcome,
        string evidenceExportOutcome,
        string errorCode,
        string blockedReason)
    {
        var outcome = new EndToEndTripRunOutcomeFixture(
            runId,
            "blocked",
            "not_run",
            missionOutcome,
            itineraryOutcome,
            "not_run",
            "end_to_end.blocked",
            0,
            0,
            "not_run",
            null,
            snapshotOutcome,
            evidenceExportOutcome,
            EvidencePacketId(runId),
            ExportPacketId(runId),
            errorCode,
            blockedReason,
            "deterministic-mock",
            "mock-trip-run",
            null);

        return new(outcome, BuildEvidence(outcome, export: null));
    }

    private SanitizedEvidenceExportEvalRow? ExportIfComplete(string runId, TripRunSnapshotResult snapshot)
    {
        if (!snapshot.IsComplete)
        {
            return null;
        }

        var packet = CreateEvidenceExportPacket(runId, snapshot.Snapshot);
        return _evidenceExportEvaluator
            .EvaluateAsync(
                [new EvidenceExportEvalCase(runId, packet)],
                new EvidenceExportOptions("mock-evidence-export-deterministic"))
            .GetAwaiter()
            .GetResult()
            .Single();
    }

    private static EvidenceExportPacket CreateEvidenceExportPacket(string runId, TripRunSnapshot snapshot)
    {
        var selected = snapshot.ItineraryDecisions
            .Where(decision => string.Equals(decision.Kind, ItinerarySlotDecisionKind.Selected.ToString(), StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(decision.CandidateId))
            .ToArray();
        var deferredCount = snapshot.ItineraryDecisions.Count(decision =>
            string.Equals(decision.Kind, ItinerarySlotDecisionKind.Deferred.ToString(), StringComparison.Ordinal));
        var preparedHoldCount = snapshot.MockHold.IsReadyForMockHold ? 1 : 0;
        var suffix = RunSuffix(runId);
        var holdEvidenceId = $"evidence-{suffix}-hold";

        return new(
            EvidencePacketId(runId),
            new TripPlanEvidenceSummary(
                ExportPacketId(runId),
                snapshot.Itinerary.DayCount,
                selected.Length,
                deferredCount,
                preparedHoldCount,
                EvidenceCount: 3),
            [
                new($"evidence-{suffix}-prompt", ExportEvidenceKind.MissionField, snapshot.Mission.MissionId),
                new($"evidence-{suffix}-itinerary", ExportEvidenceKind.Candidate, snapshot.SnapshotId),
                new(holdEvidenceId, ExportEvidenceKind.Hold, ApprovalId)
            ],
            selected.Select(decision => new TripPlanHoldOutcome(
                    decision.SlotId,
                    decision.CandidateId!,
                    snapshot.MockHold.IsReadyForMockHold ? HoldOutcomeKind.HoldPrepared : HoldOutcomeKind.Previewed,
                    holdEvidenceId))
                .ToArray(),
            "en-US",
            "structured-trip-run-snapshot");
    }

    private void CompileForSnapshot(TripSession session)
    {
        _slotCompiler.Compile(session, new ItineraryCompilationRequest(
            session.SessionId,
            null,
            null,
            null,
            ["ui-end-to-end-trip-run"]));
    }

    private static IReadOnlyList<EndToEndTripEvidenceFixture> BuildEvidence(
        EndToEndTripRunOutcomeFixture outcome,
        SanitizedEvidenceExportEvalRow? export)
    {
        if (export?.Passed == true)
        {
            return export.EvidenceIds
                .Select(evidenceId => new EndToEndTripEvidenceFixture(
                    evidenceId,
                    outcome.ExportPacketId,
                    outcome.RunId,
                    export.OutcomeCode,
                    evidenceId))
                .ToArray();
        }

        return
        [
            new(
                $"evidence-{RunSuffix(outcome.RunId)}-prompt",
                outcome.ExportPacketId,
                outcome.RunId,
                outcome.MissionOutcomeCode,
                outcome.RequestId ?? "deterministic-request"),
            new(
                $"evidence-{RunSuffix(outcome.RunId)}-itinerary",
                outcome.ExportPacketId,
                outcome.RunId,
                outcome.ItineraryOutcomeCode,
                outcome.SnapshotOutcomeCode),
            new(
                $"evidence-{RunSuffix(outcome.RunId)}-hold",
                outcome.ExportPacketId,
                outcome.RunId,
                outcome.HoldOutcomeCode,
                outcome.ApprovalId ?? "approval-not-required")
        ];
    }

    private static string? ExportErrorCode(SanitizedEvidenceExportEvalRow? export) =>
        export is { Passed: false }
            ? $"PCH_UI_EVIDENCE_EXPORT_{export.OutcomeCode.ToUpperInvariant()}"
            : null;

    private static string? ExportBlockedReason(SanitizedEvidenceExportEvalRow? export) =>
        export is { Passed: false }
            ? "Evidence export provider response did not match the canonical trip snapshot."
            : null;

    private static int SelectedCount(params ItineraryDayPlannerResult[] results) =>
        results.Count(result => result.Outcome.SelectedOutcome == "selected");

    private static int DeferredCount(params ItineraryDayPlannerResult[] results) =>
        results.Count(result => result.Outcome.DeferredOutcome == "deferred");

    private static string PromptForRun(string runId) =>
        runId == "e2e.pending-confirmation"
            ? $"{RawPromptSentinel} Maybe Japan in spring if the destination and dates are right."
            : $"{RawPromptSentinel} Family vacation in Japan with one calm day and approval-gated holds.";

    private static string EvidencePacketId(string runId) => $"evidence-packet-{RunSuffix(runId)}";

    private static string ExportPacketId(string runId) => $"export-packet-{RunSuffix(runId)}";

    private static string RunSuffix(string runId) => runId.Replace('.', '-');
}

public sealed record EndToEndTripRunResult(
    EndToEndTripRunOutcomeFixture Outcome,
    IReadOnlyList<EndToEndTripEvidenceFixture> Evidence);
