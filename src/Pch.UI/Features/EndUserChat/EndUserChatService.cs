using Pch.UI.Features.StageCockpit;

namespace Pch.UI.Features.EndUserChat;

public sealed class EndUserChatService
{
    public const string DefaultPrompt = "Plan a calm family trip to Japan with one quiet day and no real bookings.";
    public const string ModeLabel = "Deterministic offline";
    public const string ModeState = "offline-deterministic";
    public const string RawAbsenceState = "verified";

    private readonly EndToEndTripRunService _tripRunService = new();

    public EndUserChatState CreateInitialState() => new(
        ModeLabel,
        ModeState,
        RawAbsenceState,
        DefaultPrompt,
        "idle",
        null,
        null,
        [
            new(
                "turn-system-ready",
                "system",
                "mode",
                "ready",
                "Deterministic offline mode is on. This preview never contacts live providers or creates bookings.",
                ModeState,
                null,
                null),
            new(
                "turn-assistant-start",
                "assistant",
                "guidance",
                "ready",
                "Describe the trip you want to test, then send it through the deterministic planner.",
                null,
                null,
                null)
        ]);

    public EndUserChatState Send(string prompt)
    {
        var normalizedPrompt = NormalizePrompt(prompt);
        var scenario = SelectScenario(normalizedPrompt);
        var run = _tripRunService.Run(scenario);
        var turns = BuildTurns(normalizedPrompt, run);

        return new(
            ModeLabel,
            ModeState,
            RawAbsenceState,
            string.Empty,
            run.Outcome.State,
            run.Outcome.ErrorCode,
            run.Outcome.BlockedReason,
            turns);
    }

    private static IReadOnlyList<EndUserChatTurn> BuildTurns(
        string normalizedPrompt,
        EndToEndTripRunResult run)
    {
        var outcome = run.Outcome;
        var turns = new List<EndUserChatTurn>
        {
            new(
                "turn-user-1",
                "user",
                "prompt",
                "submitted",
                PromptSummary(normalizedPrompt),
                "prompt_received",
                null,
                null),
            new(
                "turn-harness-mode",
                "harness",
                "mode",
                "applied",
                "Offline deterministic mode used canonical mock planner, itinerary, hold, and evidence paths only.",
                ModeState,
                null,
                null),
            new(
                "turn-assistant-mission",
                "assistant",
                "mission",
                outcome.State == "blocked" ? "blocked" : "applied",
                MissionText(outcome),
                outcome.MissionOutcomeCode,
                "evidence-user-purpose",
                outcome.State == "blocked" ? outcome.ErrorCode : null),
            new(
                "turn-harness-itinerary",
                "harness",
                "itinerary",
                outcome.State == "blocked" ? "blocked" : outcome.State,
                ItineraryText(outcome),
                outcome.ItineraryOutcomeCode,
                "evidence-fixture-candidates",
                outcome.State == "blocked" ? outcome.ErrorCode : null),
            new(
                "turn-harness-hold",
                "harness",
                "approval",
                ResolveHoldState(outcome),
                HoldText(outcome),
                outcome.HoldOutcomeCode,
                outcome.ApprovalId,
                outcome.HoldOutcomeCode == "not_run" ? outcome.ErrorCode : null),
            new(
                "turn-assistant-final",
                "assistant",
                outcome.State == "blocked" ? "blocked" : "final",
                outcome.State,
                FinalText(outcome),
                outcome.TraceOutcome,
                outcome.EvidencePacketId,
                outcome.ErrorCode)
        };

        turns.AddRange(run.Evidence.Take(3).Select((evidence, index) => new EndUserChatTurn(
            $"turn-evidence-{index + 1}",
            "harness",
            "evidence",
            outcome.State,
            $"Evidence packet {evidence.ExportPacketId} recorded {evidence.Outcome}.",
            evidence.Outcome,
            evidence.EvidenceId,
            null)));

        return turns;
    }

    private static string SelectScenario(string normalizedPrompt)
    {
        if (normalizedPrompt.Contains("block", StringComparison.OrdinalIgnoreCase)
            || normalizedPrompt.Contains("safety", StringComparison.OrdinalIgnoreCase)
            || normalizedPrompt.Contains("approval", StringComparison.OrdinalIgnoreCase))
        {
            return "e2e.missing-approval";
        }

        if (normalizedPrompt.Contains("maybe", StringComparison.OrdinalIgnoreCase)
            || normalizedPrompt.Contains("confirm", StringComparison.OrdinalIgnoreCase))
        {
            return "e2e.pending-confirmation";
        }

        return "e2e.happy-path";
    }

    private static string NormalizePrompt(string prompt)
    {
        var trimmed = string.IsNullOrWhiteSpace(prompt) ? DefaultPrompt : prompt.Trim();
        return trimmed.Length <= 280 ? trimmed : trimmed[..280];
    }

    private static string PromptSummary(string prompt) =>
        $"Trip request accepted with {prompt.Length} characters. Raw prompt text is kept out of transcript storage.";

    private static string MissionText(EndToEndTripRunOutcomeFixture outcome) =>
        outcome.State == "blocked"
            ? "Mission intake stopped before completing the deterministic trip run."
            : "Mission facts were applied through deterministic prompt intake.";

    private static string ItineraryText(EndToEndTripRunOutcomeFixture outcome) =>
        outcome.State switch
        {
            "blocked" => outcome.BlockedReason ?? "The deterministic itinerary path was blocked.",
            "proposed" => "The planner is waiting for confirmation before building itinerary slots.",
            _ => $"Selected {outcome.SelectedCount} candidate and deferred {outcome.DeferredCount} slot."
        };

    private static string ResolveHoldState(EndToEndTripRunOutcomeFixture outcome) =>
        outcome.HoldOutcomeCode switch
        {
            "hold_preparation_ready" => "ready",
            "hold_preparation_missing_approval" => "approval-required",
            "not_run" => "not-run",
            _ => outcome.State
        };

    private static string HoldText(EndToEndTripRunOutcomeFixture outcome) =>
        outcome.HoldOutcomeCode switch
        {
            "hold_preparation_ready" => "Mock hold preparation is ready after approval gating; no real hold was placed.",
            "hold_preparation_missing_approval" => "Mock hold preparation is blocked until explicit approval is available.",
            _ => "No hold, booking, payment, or live provider handoff ran."
        };

    private static string FinalText(EndToEndTripRunOutcomeFixture outcome) =>
        outcome.State switch
        {
            "blocked" => $"Blocked with {outcome.ErrorCode}.",
            "proposed" => "Pending confirmation before final itinerary and hold steps.",
            _ => $"Final deterministic plan is ready with evidence packet {outcome.EvidencePacketId}."
        };
}
