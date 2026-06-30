using System.Text.Json;
using Pch.Core;
using Pch.Harness;
using Xunit;

namespace Pch.Harness.Tests;

public sealed class LiveSessionConductorTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void HappyPromptToMissionTurnBuildsPacketAppliesMissionAndCompilesItinerary()
    {
        var session = SyntheticTripFactory.CreateSession(3);
        var result = new LiveSessionConductor().RunTurn(session, new LivePlanningTurnRequest(
            session.SessionId,
            "Plan a quiet Japan trip with food and transit buffers.",
            "en-US",
            ["vacation"],
            LiveModelProposalEnvelope.ForMission(new ProviderMissionProposalMirror(
                "planner-live-happy",
                [
                    new("/mission/purpose", "Quiet Japan planning trip", "user", ["evidence-live-user"]),
                    new("/mission/destination_country", "Japan", "user", ["evidence-live-country"])
                ],
                [],
                []))));

        Assert.True(result.IsAccepted);
        Assert.False(result.IsBlocked);
        Assert.Equal(LiveSessionConductor.AcceptedCode, result.Code);
        Assert.Equal("prompt_packet_built", result.Prompt!.Code);
        Assert.Equal("mission_intake_applied", result.Mission!.Code);
        Assert.NotNull(session.MemoryDigest);
        Assert.Equal("Quiet Japan planning trip", session.Mission.Purpose);
        Assert.NotNull(session.LastItineraryCompilation);
        Assert.True(session.LastItineraryCompilation!.IsCompiled);
        Assert.NotNull(result.Itinerary);
        Assert.NotEmpty(result.PlanningSnapshot.Nodes);
        Assert.DoesNotContain("Plan a quiet Japan trip", JsonSerializer.Serialize(result, JsonOptions), StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedRuntimeActionBlocksAtDecodeAndDoesNotMutate()
    {
        using var document = JsonDocument.Parse("{}");
        var session = SyntheticTripFactory.CreateSession(3);
        var before = Counts(session);

        var result = new LiveSessionConductor().RunTurn(session, new LivePlanningTurnRequest(
            session.SessionId,
            "RAW_PROMPT_SHOULD_NOT_LEAK plan something.",
            "en-US",
            [],
            LiveModelProposalEnvelope.ForRuntimeAction(new ExternalActionProposal(
                "action-live-bad",
                "RAW_ACTION_KIND_SHOULD_NOT_LEAK",
                document.RootElement.Clone()))));
        var serialized = JsonSerializer.Serialize(result, JsonOptions);

        Assert.False(result.IsAccepted);
        Assert.True(result.IsBlocked);
        Assert.Equal(LiveSessionConductor.DecodeBlockedCode, result.Code);
        Assert.Equal("unknown_action_kind", result.RuntimeAction!.DecodeCode);
        Assert.Equal("not_run", result.RuntimeAction.IntakeCode);
        Assert.Equal(before, Counts(session));
        Assert.DoesNotContain("RAW_PROMPT_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_ACTION_KIND_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelBlockedEnvelopeReturnsProviderModelBlockedWithoutMutation()
    {
        var session = SyntheticTripFactory.CreateSession(3);
        var before = Counts(session);

        var result = new LiveSessionConductor().RunTurn(session, new LivePlanningTurnRequest(
            session.SessionId,
            "RAW_PROMPT_SHOULD_NOT_LEAK model blocked path.",
            "en-US",
            [],
            LiveModelProposalEnvelope.Blocked("RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK")));
        var serialized = JsonSerializer.Serialize(result, JsonOptions);

        Assert.False(result.IsAccepted);
        Assert.True(result.IsBlocked);
        Assert.Equal(LiveSessionConductor.ProviderModelBlockedCode, result.Code);
        Assert.Equal(before, Counts(session));
        Assert.DoesNotContain("RAW_PROMPT_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void ApprovalRequiredRuntimeActionDoesNotRecordActionApprovalOrDecision()
    {
        using var document = JsonDocument.Parse("""
        {
          "approval_id": "approval-review",
          "approval_action_id": "mock-booking",
          "prompt": "RAW_PROMPT_SHOULD_NOT_LEAK",
          "risk_flags": ["booking"],
          "spend_amount": 120,
          "approval_token": "RAW_APPROVAL_TOKEN_SHOULD_NOT_LEAK"
        }
        """);
        var session = SyntheticTripFactory.CreateSession(3);
        session.MoveTo(HarnessStage.ApprovalQueue);
        var before = Counts(session);

        var result = new LiveSessionConductor().RunTurn(session, new LivePlanningTurnRequest(
            session.SessionId,
            "Please book it. RAW_PROMPT_SHOULD_NOT_LEAK",
            "en-US",
            [],
            LiveModelProposalEnvelope.ForRuntimeAction(new ExternalActionProposal(
                "action-live-approval",
                HarnessAction.RequestApprovalKind,
                document.RootElement.Clone()))));
        var serialized = JsonSerializer.Serialize(result, JsonOptions);

        Assert.False(result.IsAccepted);
        Assert.True(result.IsBlocked);
        Assert.Equal(LiveSessionConductor.ApprovalRequiredCode, result.Code);
        Assert.Equal("decoded", result.RuntimeAction!.DecodeCode);
        Assert.Equal("approval_required", result.RuntimeAction.IntakeCode);
        Assert.Equal(before, Counts(session));
        Assert.DoesNotContain("RAW_APPROVAL_TOKEN_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_PROMPT_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("Please book it", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void DeterministicFallbackTurnReturnsTypedFallbackProjection()
    {
        var session = SyntheticTripFactory.CreateSession(3);
        var result = new LiveSessionConductor().RunTurn(session, new LivePlanningTurnRequest(
            session.SessionId,
            "Fallback please.",
            "en-US",
            [],
            LiveModelProposalEnvelope.Fallback()));

        Assert.True(result.IsAccepted);
        Assert.False(result.IsBlocked);
        Assert.Equal(LiveSessionConductor.DeterministicFallbackCode, result.Code);
        Assert.Equal(LiveTurnProjector.DeterministicFallbackCode, result.TurnProjection.Code);
        Assert.Null(result.Mission);
        Assert.Null(result.RuntimeAction);
    }

    [Fact]
    public void MissionProposalBlockedDoesNotMutateAndOmitsCandidateDisplaySecretSentinels()
    {
        var session = SyntheticTripFactory.CreateSession(3);
        var before = Counts(session);

        var result = new LiveSessionConductor().RunTurn(session, new LivePlanningTurnRequest(
            session.SessionId,
            "RAW_PROMPT_SHOULD_NOT_LEAK",
            "en-US",
            [],
            LiveModelProposalEnvelope.ForMission(new ProviderMissionProposalMirror(
                "planner-live-blocked",
                [
                    new("/mission/raw_prompt", "RAW_SECRET_SHOULD_NOT_LEAK", "user", ["RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK"])
                ],
                [],
                []))));
        var serialized = JsonSerializer.Serialize(result, JsonOptions);

        Assert.False(result.IsAccepted);
        Assert.True(result.IsBlocked);
        Assert.Equal(LiveSessionConductor.MissionProposalBlockedCode, result.Code);
        Assert.Equal(before, Counts(session));
        Assert.Null(session.MemoryDigest);
        Assert.DoesNotContain("RAW_PROMPT_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_SECRET_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_CANDIDATE_DISPLAY_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
    }

    private static (int Actions, int Decisions, int ItineraryDecisions, int Approvals, int Deferred, string Purpose) Counts(TripSession session)
    {
        return (
            session.Actions.Count,
            session.DecisionLedger.Records.Count,
            session.ItineraryDecisions.Count,
            session.ApprovalTokens.Count,
            session.DeferredSlots.Count,
            session.Mission.Purpose);
    }
}
