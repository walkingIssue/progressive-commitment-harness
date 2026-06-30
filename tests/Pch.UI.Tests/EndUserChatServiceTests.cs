using System.Text.Json;
using Pch.UI.Features.EndUserChat;
using Xunit;

namespace Pch.UI.Tests;

public sealed class EndUserChatServiceTests
{
    [Fact]
    public void InitialStateShowsDeterministicOfflineModeAndAccessibleTranscriptSeed()
    {
        var service = new EndUserChatService();

        var state = service.CreateInitialState();

        Assert.Equal("offline-deterministic", state.ModeState);
        Assert.Equal("verified", state.RawAbsenceState);
        Assert.Equal("idle", state.FinalState);
        Assert.Contains(state.Turns, turn => turn.TurnId == "turn-system-ready"
            && turn.Role == "system"
            && turn.OutcomeCode == "offline-deterministic");
        Assert.Contains(state.Turns, turn => turn.TurnId == "turn-assistant-start"
            && turn.Role == "assistant"
            && turn.State == "ready");
    }

    [Fact]
    public void HappyPathProducesFinalTranscriptAndEvidenceMarkers()
    {
        var service = new EndUserChatService();

        var state = service.Send("Plan a calm family trip to Japan with one quiet day.");
        var serialized = Serialize(state);

        Assert.Equal("applied", state.FinalState);
        Assert.Null(state.ErrorCode);
        Assert.Contains(state.Turns, turn => turn.TurnId == "turn-user-1"
            && turn.Role == "user"
            && turn.Kind == "prompt"
            && turn.OutcomeCode == "prompt_received");
        Assert.Contains(state.Turns, turn => turn.TurnId == "turn-assistant-final"
            && turn.Kind == "final"
            && turn.State == "applied"
            && turn.OutcomeCode == "end_to_end.applied"
            && turn.EvidenceId == "evidence-packet-e2e-happy-path");
        Assert.Contains(state.Turns, turn => turn.Kind == "evidence"
            && turn.EvidenceId == "evidence-e2e-happy-path-prompt");
        AssertChatRawTextAbsent(serialized);
    }

    [Fact]
    public void BlockedSafetyPromptShowsBlockedStateWithoutLiveHoldOrPaymentImplication()
    {
        var service = new EndUserChatService();

        var state = service.Send("Please test the approval safety block before any booking.");
        var serialized = Serialize(state);

        Assert.Equal("blocked", state.FinalState);
        Assert.Equal("PCH_UI_ITINERARY_HOLD_APPROVAL_REQUIRED", state.ErrorCode);
        Assert.Contains(state.Turns, turn => turn.TurnId == "turn-harness-hold"
            && turn.Kind == "approval"
            && turn.State == "approval-required"
            && turn.OutcomeCode == "hold_preparation_missing_approval");
        Assert.Contains(state.Turns, turn => turn.TurnId == "turn-assistant-final"
            && turn.Kind == "blocked"
            && turn.State == "blocked"
            && turn.ErrorCode == "PCH_UI_ITINERARY_HOLD_APPROVAL_REQUIRED");
        Assert.DoesNotContain("real hold", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payment", serialized, StringComparison.OrdinalIgnoreCase);
        AssertChatRawTextAbsent(serialized);
    }

    [Fact]
    public void PendingPromptKeepsConfirmationReadyTranscript()
    {
        var service = new EndUserChatService();

        var state = service.Send("Maybe Japan if the dates and destination can be confirmed.");

        Assert.Equal("proposed", state.FinalState);
        Assert.Contains(state.Turns, turn => turn.TurnId == "turn-harness-itinerary"
            && turn.State == "proposed"
            && turn.Text.Contains("waiting for confirmation", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(state.Turns, turn => turn.TurnId == "turn-assistant-final"
            && turn.State == "proposed"
            && turn.OutcomeCode == "end_to_end.pending_confirmation");
    }

    [Fact]
    public void RawSentinelPromptIsNotEchoedIntoTranscriptSerialization()
    {
        var service = new EndUserChatService();

        var state = service.Send("RAW_USER_PROMPT_SHOULD_NOT_LEAK RAW_PROVIDER_PAYLOAD_SHOULD_NOT_PERSIST SECRET_SENTINEL");
        var serialized = Serialize(state);

        Assert.Equal("applied", state.FinalState);
        Assert.Contains(state.Turns, turn => turn.TurnId == "turn-user-1"
            && turn.Text.Contains("characters", StringComparison.Ordinal));
        AssertChatRawTextAbsent(serialized);
    }

    private static string Serialize(EndUserChatState state) =>
        JsonSerializer.Serialize(state);

    private static void AssertChatRawTextAbsent(string serialized)
    {
        Assert.DoesNotContain("Plan a calm family trip", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("Please test the approval safety block", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("Maybe Japan", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_USER_PROMPT_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_PROVIDER_PAYLOAD_SHOULD_NOT_PERSIST", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_END_TO_END_PROMPT_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("APPROVAL_TOKEN_SHOULD_NOT_PERSIST", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_HOLD_REFERENCE_SHOULD_NOT_PERSIST", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("CANDIDATE_DISPLAY_VALUE_SHOULD_NOT_PERSIST", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET_SENTINEL", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("live booking", serialized, StringComparison.OrdinalIgnoreCase);
    }
}
