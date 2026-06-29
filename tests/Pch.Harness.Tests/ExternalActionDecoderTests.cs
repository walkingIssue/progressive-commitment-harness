using Pch.Core;
using Pch.Harness;
using System.Text.Json;
using Xunit;

namespace Pch.Harness.Tests;

public sealed class ExternalActionDecoderTests
{
    [Fact]
    public void MalformedJsonReturnsSanitizedFailure()
    {
        const string raw = "{ \"reason\": \"RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK\" ";

        var result = new ExternalActionDecoder().DecodeJson("action-1", HarnessAction.DeferSlotKind, raw);

        Assert.False(result.IsDecoded);
        Assert.Equal("malformed_json", result.Code);
        Assert.DoesNotContain("RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownKindReturnsSanitizedFailure()
    {
        const string rawKind = "RAW_ACTION_TEXT_SHOULD_NOT_LEAK";

        var result = new ExternalActionDecoder().DecodeJson("action-1", rawKind, "{}");

        Assert.False(result.IsDecoded);
        Assert.Equal("unknown_action_kind", result.Code);
        Assert.DoesNotContain(rawKind, result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingRequiredFieldReturnsFixedFailureWithoutMutation()
    {
        var session = SyntheticTripFactory.CreateSession(7);

        var result = new ExternalActionDecoder().DecodeJson(
            "action-1",
            HarnessAction.DeferSlotKind,
            """{ "slot_id": "dinner-day-2" }""");

        Assert.False(result.IsDecoded);
        Assert.Equal("missing_required_argument", result.Code);
        Assert.Empty(session.Actions);
        Assert.Empty(session.DeferredSlots);
    }

    [Fact]
    public void DecodedDeferRoutesThroughIntakeAndMutatesOnlyAfterAcceptance()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        session.MoveTo(HarnessStage.Meals);
        var decode = new ExternalActionDecoder().DecodeJson(
            "action-defer",
            HarnessAction.DeferSlotKind,
            """{ "slot_id": "dinner-day-2", "reason": "Need user preference." }""");

        Assert.True(decode.IsDecoded);
        var intake = new HarnessActionIntake().Accept(session, decode.Action!);

        Assert.False(intake.IsBlocked);
        Assert.Single(session.Actions);
        Assert.Single(session.DeferredSlots);
        Assert.Equal("accepted", intake.Trace.Single().Outcome);
    }

    [Fact]
    public void DecodedActionBlockedByStageDoesNotMutate()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        session.MoveTo(HarnessStage.Intake);
        var decode = new ExternalActionDecoder().DecodeJson(
            "action-handoff",
            HarnessAction.HandoffKind,
            """{ "target": "strong-model-auditor", "reason": "Review trace." }""");

        var intake = new HarnessActionIntake().Accept(session, decode.Action!);

        Assert.True(intake.IsBlocked);
        Assert.Equal("action_not_allowed_for_stage", intake.Trace.Single().Outcome);
        Assert.Empty(session.Actions);
        Assert.Empty(session.Handoffs);
    }

    [Fact]
    public void DecodedApprovalMismatchBlocksWithoutMutation()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        session.MoveTo(HarnessStage.ApprovalQueue);
        var decode = new ExternalActionDecoder().DecodeJson(
            "action-approval",
            HarnessAction.RequestApprovalKind,
            """
            {
              "approval_id": "other-approval",
              "approval_action_id": "mock-booking",
              "prompt": "Approve.",
              "risk_flags": ["booking"],
              "approval_token": "approved-token"
            }
            """);

        var intake = new HarnessActionIntake().Accept(session, decode.Action!);

        Assert.True(intake.IsBlocked);
        Assert.Equal("approval_id_mismatch", intake.Trace.Single().Outcome);
        Assert.Empty(session.Actions);
        Assert.Empty(session.ApprovalTokens);
    }

    [Fact]
    public void DecodedApprovalIgnoresProviderSuppliedTokenAndDoesNotPersistIt()
    {
        const string sentinelToken = "RAW_PROVIDER_APPROVAL_TOKEN_SHOULD_NOT_LEAK";
        var decode = new ExternalActionDecoder().DecodeJson(
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

        var action = Assert.IsType<RequestApprovalAction>(decode.Action);
        var serialized = JsonSerializer.Serialize(decode);

        Assert.True(decode.IsDecoded);
        Assert.Null(action.Approval.ApprovalToken);
        Assert.DoesNotContain(sentinelToken, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(sentinelToken, decode.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void DecodedApprovalWithOnlyProviderTokenIsBlockedByIntakeGateWithoutMutation()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        session.MoveTo(HarnessStage.ApprovalQueue);
        var decode = new ExternalActionDecoder().DecodeJson(
            "action-approval",
            HarnessAction.RequestApprovalKind,
            """
            {
              "approval_id": "approval-review",
              "approval_action_id": "mock-booking",
              "prompt": "Approve.",
              "risk_flags": ["booking"],
              "approval_token": "RAW_PROVIDER_APPROVAL_TOKEN_SHOULD_NOT_LEAK"
            }
            """);

        var intake = new HarnessActionIntake().Accept(session, decode.Action!);

        Assert.True(intake.IsBlocked);
        Assert.Equal("approval_required", intake.Trace.Single().Outcome);
        Assert.Empty(session.Actions);
        Assert.Empty(session.ApprovalTokens);
        Assert.Empty(session.DecisionLedger.Records);
    }

    [Fact]
    public void DecodesAllCurrentHarnessActionKinds()
    {
        var decoder = new ExternalActionDecoder();

        var cases = new Dictionary<string, string>
        {
            [HarnessAction.EmitFormKind] = """{ "form_id": "mission-intake", "title": "Mission intake" }""",
            [HarnessAction.EmitChoiceSetKind] = """{ "title": "Choices", "candidate_ids": ["candidate-01"], "max_selectable": 1 }""",
            [HarnessAction.ProposeSearchKind] = """{ "query": "tokyo hotels", "search_surface": "lodging" }""",
            [HarnessAction.SummarizeKind] = """{ "audience": "traveler", "claim_ids": ["claim-1"] }""",
            [HarnessAction.RequestApprovalKind] = """{ "approval_id": "approval-review", "approval_action_id": "mock-booking", "prompt": "Approve.", "risk_flags": ["booking"] }""",
            [HarnessAction.StatePatchKind] = """{ "patch_id": "patch-1", "path": "/travelers/0/needs", "proposed_value": "quiet hotel" }""",
            [HarnessAction.DeferSlotKind] = """{ "slot_id": "dinner-day-2", "reason": "Need user preference." }""",
            [HarnessAction.HandoffKind] = """{ "target": "strong-model-auditor", "reason": "Review trace." }"""
        };

        foreach (var (kind, json) in cases)
        {
            var result = decoder.DecodeJson($"action-{kind}", kind, json);
            Assert.True(result.IsDecoded, $"{kind} failed: {result.Code}");
            Assert.Equal(kind, result.Action!.Kind);
        }
    }
}
