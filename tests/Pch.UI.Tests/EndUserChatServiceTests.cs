using System.Text.Json;
using Pch.Harness;
using Pch.Providers.ModelRoles;
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
        Assert.Equal(ModelRoleStatusEvaluator.OutcomeReady, state.RoleStatusOutcome);
        Assert.Equal("deterministic-offline", state.RoleStatusActiveRole);
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
    public async Task HappyPathProducesFinalTranscriptAndEvidenceMarkers()
    {
        var service = new EndUserChatService();

        var state = await service.SendAsync("Plan a calm family trip to Japan with one quiet day.");
        var serialized = Serialize(state);

        Assert.Equal("applied", state.FinalState);
        Assert.Null(state.ErrorCode);
        Assert.Equal(ModelRoleStatusEvaluator.OutcomeReady, state.RoleStatusOutcome);
        Assert.Equal("deterministic-offline", state.RoleStatusActiveRole);
        Assert.Contains(state.Turns, turn => turn.TurnId == "turn-user-1"
            && turn.Role == "user"
            && turn.Kind == "prompt"
            && turn.OutcomeCode == "prompt_received");
        Assert.Contains(state.Turns, turn => turn.TurnId == "turn-provider-role-status"
            && turn.Role == "provider"
            && turn.Kind == "role-status"
            && turn.OutcomeCode == ModelRoleStatusEvaluator.OutcomeReady);
        Assert.Contains(state.Turns, turn => turn.TurnId == "turn-03"
            && turn.Role == "harness"
            && turn.Kind == "harness"
            && turn.OutcomeCode == "mission_intake_applied");
        Assert.Contains(state.Turns, turn => turn.TurnId == "turn-assistant-final"
            && turn.Kind == "final"
            && turn.State == "applied"
            && turn.OutcomeCode == GoldenTurnTraceRunner.TraceCompleteCode);
        Assert.Contains(state.Turns, turn => turn.Kind == "evidence"
            && turn.OutcomeCode == "complete");
        AssertChatRawTextAbsent(serialized);
    }

    [Fact]
    public async Task BlockedSafetyPromptShowsBlockedStateWithoutLiveHoldOrBookingImplication()
    {
        var service = new EndUserChatService();

        var state = await service.SendAsync("Please test the approval safety block before any booking.");
        var serialized = Serialize(state);

        Assert.Equal("blocked", state.FinalState);
        Assert.Equal(GoldenTurnTraceRunner.TraceBlockedCode, state.ErrorCode);
        Assert.Contains(state.Turns, turn => turn.TurnId == "turn-06"
            && turn.Kind == "blocked"
            && turn.State == "blocked"
            && turn.OutcomeCode == "approval_required_preview");
        Assert.Contains(state.Turns, turn => turn.TurnId == "turn-assistant-final"
            && turn.Kind == "blocked"
            && turn.State == "blocked"
            && turn.ErrorCode == GoldenTurnTraceRunner.TraceBlockedCode);
        Assert.DoesNotContain("real hold", serialized, StringComparison.OrdinalIgnoreCase);
        AssertChatRawTextAbsent(serialized);
    }

    [Fact]
    public async Task PendingPromptKeepsConfirmationReadyTranscript()
    {
        var service = new EndUserChatService();

        var state = await service.SendAsync("Maybe Japan if the dates and destination can be confirmed.");

        Assert.Equal("pending", state.FinalState);
        Assert.Equal("end_user_chat_pending_confirmation", state.ErrorCode);
        Assert.Contains(state.Turns, turn => turn.TurnId == "turn-03"
            && turn.State == "pending"
            && turn.OutcomeCode == "mission_intake_applied");
        Assert.Contains(state.Turns, turn => turn.TurnId == "turn-assistant-final"
            && turn.State == "pending"
            && turn.OutcomeCode == "end_user_chat_pending_confirmation");
    }

    [Fact]
    public async Task RawSentinelPromptIsNotEchoedIntoTranscriptSerialization()
    {
        var service = new EndUserChatService();

        var state = await service.SendAsync("RAW_USER_PROMPT_SHOULD_NOT_LEAK RAW_PROVIDER_PAYLOAD_SHOULD_NOT_PERSIST SECRET_SENTINEL");
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
        Assert.DoesNotContain("RAW_HOLD_REFERENCE_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("CANDIDATE_DISPLAY_VALUE_SHOULD_NOT_PERSIST", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_CANDIDATE_DISPLAY_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET_SENTINEL", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("live booking", serialized, StringComparison.OrdinalIgnoreCase);
    }
}
