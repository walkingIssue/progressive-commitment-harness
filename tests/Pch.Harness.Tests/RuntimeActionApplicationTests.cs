using Pch.Core;
using Pch.Harness;
using System.Text.Json;
using Xunit;

namespace Pch.Harness.Tests;

public sealed class RuntimeActionApplicationTests
{
    [Fact]
    public void AcceptedDeferSlotReturnsStableRuntimeResultAndMutates()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        session.MoveTo(HarnessStage.Meals);

        var result = new RuntimeActionApplication().ApplyJson(
            session,
            "action-defer",
            HarnessAction.DeferSlotKind,
            """{ "slot_id": "dinner-day-2", "reason": "Need user preference." }""");

        Assert.True(result.IsAccepted);
        Assert.False(result.IsBlocked);
        Assert.Equal("decoded", result.DecodeCode);
        Assert.Equal("accepted", result.IntakeCode);
        Assert.Equal("Meals", result.Stage);
        Assert.NotEmpty(result.PacketId);
        Assert.Single(result.Trace);
        Assert.Single(session.Actions);
        Assert.Single(session.DeferredSlots);
    }

    [Fact]
    public void BlockedDisallowedHandoffReturnsIntakeCodeAndDoesNotMutate()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        session.MoveTo(HarnessStage.Intake);

        var result = new RuntimeActionApplication().ApplyJson(
            session,
            "action-handoff",
            HarnessAction.HandoffKind,
            """{ "target": "strong-model-auditor", "reason": "Review trace." }""");

        Assert.False(result.IsAccepted);
        Assert.True(result.IsBlocked);
        Assert.Equal("decoded", result.DecodeCode);
        Assert.Equal("action_not_allowed_for_stage", result.IntakeCode);
        Assert.Equal("Intake", result.Stage);
        Assert.Empty(session.Actions);
        Assert.Empty(session.Handoffs);
    }

    [Fact]
    public void MalformedJsonReturnsDecodeFailureAndDoesNotMutateOrEchoRawPayload()
    {
        const string raw = "{ \"reason\": \"RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK\" ";
        var session = SyntheticTripFactory.CreateSession(7);

        var result = new RuntimeActionApplication().ApplyJson(
            session,
            "action-defer",
            HarnessAction.DeferSlotKind,
            raw);
        var serialized = JsonSerializer.Serialize(result);

        Assert.True(result.IsBlocked);
        Assert.Equal("malformed_json", result.DecodeCode);
        Assert.Equal("not_run", result.IntakeCode);
        Assert.Empty(session.Actions);
        Assert.DoesNotContain("RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownKindReturnsDecodeFailureAndDoesNotEchoRawKind()
    {
        const string rawKind = "RAW_ACTION_KIND_SHOULD_NOT_LEAK";
        var session = SyntheticTripFactory.CreateSession(7);

        var result = new RuntimeActionApplication().ApplyJson(
            session,
            "action-unknown",
            rawKind,
            "{}");
        var serialized = JsonSerializer.Serialize(result);

        Assert.True(result.IsBlocked);
        Assert.Equal("unknown_action_kind", result.DecodeCode);
        Assert.Equal("not_run", result.IntakeCode);
        Assert.Empty(session.Actions);
        Assert.DoesNotContain(rawKind, serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderSuppliedApprovalTokenIsDroppedAndCannotSatisfyApprovalGate()
    {
        const string sentinelToken = "RAW_PROVIDER_APPROVAL_TOKEN_SHOULD_NOT_LEAK";
        var session = SyntheticTripFactory.CreateSession(7);
        session.MoveTo(HarnessStage.ApprovalQueue);

        var result = new RuntimeActionApplication().ApplyJson(
            session,
            "action-approval",
            HarnessAction.RequestApprovalKind,
            $$"""
            {
              "approval_id": "approval-review",
              "approval_action_id": "mock-booking",
              "prompt": "Approve.",
              "risk_flags": ["booking"],
              "approval_token": "{{sentinelToken}}"
            }
            """);
        var serialized = JsonSerializer.Serialize(result);

        Assert.True(result.IsBlocked);
        Assert.Equal("decoded", result.DecodeCode);
        Assert.Equal("approval_required", result.IntakeCode);
        Assert.Empty(session.Actions);
        Assert.Empty(session.ApprovalTokens);
        Assert.Empty(session.DecisionLedger.Records);
        Assert.DoesNotContain(sentinelToken, serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void DecodeFailureFromProposalObjectDoesNotMutateSession()
    {
        using var document = JsonDocument.Parse("{}");
        var proposal = new ExternalActionProposal("action-unknown", "unknown_kind", document.RootElement.Clone());
        var session = SyntheticTripFactory.CreateSession(7);

        var result = new RuntimeActionApplication().Apply(session, proposal);

        Assert.True(result.IsBlocked);
        Assert.Equal("unknown_action_kind", result.DecodeCode);
        Assert.Equal("not_run", result.IntakeCode);
        Assert.Empty(session.Actions);
    }
}
