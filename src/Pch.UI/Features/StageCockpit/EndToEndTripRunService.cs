using Pch.Core;
using Pch.Harness;

namespace Pch.UI.Features.StageCockpit;

public sealed class EndToEndTripRunService
{
    private const string RawPromptSentinel = "RAW_END_TO_END_PROMPT_SHOULD_NOT_LEAK";
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

    public EndToEndTripRunResult Run(string runId)
    {
        if (!KnownRunIds.Contains(runId, StringComparer.Ordinal))
        {
            return Blocked(
                runId,
                "not_run",
                "not_run",
                "PCH_UI_E2E_UNKNOWN_SCENARIO",
                "End-to-end trip run scenario is not recognized.");
        }

        var session = SyntheticTripFactory.CreateSession(11);
        var promptRunId = runId == "e2e.pending-confirmation" ? "prompt.pending" : "prompt.accepted";
        var mission = _missionPlannerService.RunPrompt(session, promptRunId, PromptForRun(runId));
        if (mission.State == "blocked")
        {
            return FromMissionBlocked(runId, mission);
        }

        return runId switch
        {
            "e2e.happy-path" => HappyPath(session, runId, mission),
            "e2e.pending-confirmation" => PendingConfirmation(runId, mission),
            "e2e.provider-mismatch" => ItineraryBlocked(session, runId, mission, "itinerary.provider-mismatch"),
            "e2e.wrong-slot" => ItineraryBlocked(session, runId, mission, "itinerary.select.wrong-slot"),
            "e2e.missing-approval" => MissingApproval(session, runId, mission),
            "e2e.raw-sentinel" => RawSentinelPath(session, runId, mission),
            _ => Blocked(
                runId,
                mission.IntakeOutcomeCode,
                "not_run",
                "PCH_UI_E2E_UNKNOWN_SCENARIO",
                "End-to-end trip run scenario is not recognized.")
        };
    }

    private EndToEndTripRunResult HappyPath(
        TripSession session,
        string runId,
        PromptIntakePlannerResult mission)
    {
        var itinerary = _itineraryDayPlannerService.Run(session, "itinerary.accepted");
        var hold = _itineraryDayPlannerService.Run(session, "itinerary.hold.approved");

        return Accepted(
            runId,
            mission,
            itinerary,
            hold.Outcome.HoldOutcome,
            hold.Outcome.ApprovalId,
            SelectedCount(itinerary, hold),
            DeferredCount(itinerary));
    }

    private static EndToEndTripRunResult PendingConfirmation(
        string runId,
        PromptIntakePlannerResult mission)
    {
        var outcome = new EndToEndTripRunOutcomeFixture(
            runId,
            "proposed",
            mission.PromptPacketOutcomeCode,
            mission.IntakeOutcomeCode,
            "itinerary_not_run_pending_confirmation",
            mission.MemoryDigestOutcomeCode,
            "end_to_end.pending_confirmation",
            0,
            0,
            "not_run",
            null,
            EvidencePacketId(runId),
            ExportPacketId(runId),
            null,
            null,
            mission.Provider,
            mission.Model,
            mission.RequestId);

        return new(outcome, BuildEvidence(outcome));
    }

    private EndToEndTripRunResult ItineraryBlocked(
        TripSession session,
        string runId,
        PromptIntakePlannerResult mission,
        string itineraryRunId)
    {
        var itinerary = _itineraryDayPlannerService.Run(session, itineraryRunId);
        var outcome = new EndToEndTripRunOutcomeFixture(
            runId,
            "blocked",
            mission.PromptPacketOutcomeCode,
            mission.IntakeOutcomeCode,
            itinerary.Outcome.BlockedOutcome,
            "not_run",
            "end_to_end.blocked",
            0,
            0,
            "not_run",
            itinerary.Outcome.ApprovalId,
            EvidencePacketId(runId),
            ExportPacketId(runId),
            itinerary.Outcome.ErrorCode,
            itinerary.Outcome.BlockedReason,
            mission.Provider,
            mission.Model,
            mission.RequestId);

        return new(outcome, BuildEvidence(outcome));
    }

    private EndToEndTripRunResult MissingApproval(
        TripSession session,
        string runId,
        PromptIntakePlannerResult mission)
    {
        var itinerary = _itineraryDayPlannerService.Run(session, "itinerary.hold.missing-approval");
        var outcome = new EndToEndTripRunOutcomeFixture(
            runId,
            "blocked",
            mission.PromptPacketOutcomeCode,
            mission.IntakeOutcomeCode,
            itinerary.Outcome.BlockedOutcome,
            "not_run",
            "end_to_end.blocked",
            itinerary.Outcome.SelectedOutcome == "selected" ? 1 : 0,
            0,
            itinerary.Outcome.HoldOutcome,
            itinerary.Outcome.ApprovalId,
            EvidencePacketId(runId),
            ExportPacketId(runId),
            itinerary.Outcome.ErrorCode,
            itinerary.Outcome.BlockedReason,
            mission.Provider,
            mission.Model,
            mission.RequestId);

        return new(outcome, BuildEvidence(outcome));
    }

    private EndToEndTripRunResult RawSentinelPath(
        TripSession session,
        string runId,
        PromptIntakePlannerResult mission)
    {
        var itinerary = _itineraryDayPlannerService.Run(session, "itinerary.accepted");

        return Accepted(
            runId,
            mission,
            itinerary,
            "none",
            null,
            SelectedCount(itinerary),
            DeferredCount(itinerary));
    }

    private static EndToEndTripRunResult Accepted(
        string runId,
        PromptIntakePlannerResult mission,
        ItineraryDayPlannerResult itinerary,
        string holdOutcome,
        string? approvalId,
        int selectedCount,
        int deferredCount)
    {
        var outcome = new EndToEndTripRunOutcomeFixture(
            runId,
            "applied",
            mission.PromptPacketOutcomeCode,
            mission.IntakeOutcomeCode,
            "itinerary_day_compiled",
            mission.MemoryDigestOutcomeCode,
            "end_to_end.applied",
            selectedCount,
            deferredCount,
            holdOutcome,
            approvalId,
            EvidencePacketId(runId),
            ExportPacketId(runId),
            null,
            null,
            mission.Provider,
            mission.Model,
            mission.RequestId);

        return new(outcome, BuildEvidence(outcome, itinerary));
    }

    private static EndToEndTripRunResult FromMissionBlocked(
        string runId,
        PromptIntakePlannerResult mission)
    {
        var outcome = new EndToEndTripRunOutcomeFixture(
            runId,
            "blocked",
            mission.PromptPacketOutcomeCode,
            mission.IntakeOutcomeCode,
            "not_run",
            mission.MemoryDigestOutcomeCode,
            "end_to_end.blocked",
            0,
            0,
            "not_run",
            null,
            EvidencePacketId(runId),
            ExportPacketId(runId),
            mission.ErrorCode,
            mission.BlockedReason,
            mission.Provider,
            mission.Model,
            mission.RequestId);

        return new(outcome, BuildEvidence(outcome));
    }

    private static EndToEndTripRunResult Blocked(
        string runId,
        string missionOutcome,
        string itineraryOutcome,
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
            EvidencePacketId(runId),
            ExportPacketId(runId),
            errorCode,
            blockedReason,
            "deterministic-mock",
            "mock-trip-run",
            null);

        return new(outcome, BuildEvidence(outcome));
    }

    private static IReadOnlyList<EndToEndTripEvidenceFixture> BuildEvidence(
        EndToEndTripRunOutcomeFixture outcome,
        ItineraryDayPlannerResult? itinerary = null)
    {
        var itineraryReference = itinerary?.Outcome.DayId;
        if (string.IsNullOrWhiteSpace(itineraryReference))
        {
            itineraryReference = outcome.ItineraryOutcomeCode;
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
                itineraryReference),
            new(
                $"evidence-{RunSuffix(outcome.RunId)}-hold",
                outcome.ExportPacketId,
                outcome.RunId,
                outcome.HoldOutcomeCode,
                outcome.ApprovalId ?? "approval-not-required")
        ];
    }

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
