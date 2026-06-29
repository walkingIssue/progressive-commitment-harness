using Pch.UI.Features.StageCockpit;
using Xunit;

namespace Pch.UI.Tests;

public sealed class HarnessStageCockpitServiceTests
{
    [Fact]
    public void ApplyFormAfterApprovalRequestBlocksWithoutAdvancingApprovalStage()
    {
        var service = new HarnessStageCockpitService();
        var approvalFixture = service.RequestApprovalStage();

        var repairedFixture = service.ApplyForm(new Dictionary<string, string>
        {
            ["destination_country"] = "Japan",
            ["purpose"] = "family travel"
        });

        Assert.Equal("ApprovalQueue", repairedFixture.Packet.Name);
        Assert.Equal("approval-review", repairedFixture.Approval.Id);
        Assert.Equal("approval-required", repairedFixture.Approval.State);
        Assert.Contains(
            repairedFixture.Session.Responses,
            response => response.State == SessionResponseState.ApprovalRequired
                && response.ApprovalId == "approval-review");
        Assert.Contains(
            repairedFixture.Session.Responses,
            response => response.State == SessionResponseState.Blocked
                && response.ApprovalId == "approval-review"
                && response.Summary.Contains("Cannot apply form", StringComparison.Ordinal));
        Assert.Equal("ApprovalQueue", approvalFixture.Packet.Name);
    }

    [Fact]
    public void AcceptedSuggestedActionRoutesThroughDecoderAndIntake()
    {
        var service = new HarnessStageCockpitService();

        var fixture = service.ApplySuggestedAction("suggestion.accept.defer-slot");

        Assert.Contains(
            fixture.SuggestedActions.Outcomes,
            outcome => outcome.SuggestionId == "suggestion.accept.defer-slot"
                && outcome.State == "accepted"
                && outcome.ActionKind == "defer_slot"
                && outcome.TraceOutcome == "suggestion.accepted");
        Assert.Contains(
            fixture.Trace.Claims,
            claim => claim.Text == "deferred_slot_count: 1");
        Assert.Contains(
            fixture.Session.Responses,
            response => response.State == SessionResponseState.Applied
                && response.Target == "suggestion.accept.defer-slot"
                && response.Summary.Contains("defer_slot", StringComparison.Ordinal));
    }

    [Fact]
    public void BlockedSuggestedActionRoutesThroughIntakeWithoutMutation()
    {
        var service = new HarnessStageCockpitService();

        var fixture = service.ApplySuggestedAction("suggestion.blocked.booking");

        Assert.Empty(fixture.ChoiceSet.SelectedCandidateId);
        Assert.DoesNotContain(
            fixture.Trace.Claims,
            claim => claim.Text == "deferred_slot_count: 1");
        Assert.Contains(
            fixture.SuggestedActions.Outcomes,
            outcome => outcome.SuggestionId == "suggestion.blocked.booking"
                && outcome.State == "blocked"
                && outcome.ActionKind == "handoff"
                && outcome.ApprovalId == "approval-review"
                && outcome.ErrorCode == "PCH_UI_INTAKE_ACTION_NOT_ALLOWED_FOR_STAGE"
                && outcome.TraceOutcome == "action_not_allowed_for_stage");
        Assert.Contains(
            fixture.Session.Responses,
            response => response.State == SessionResponseState.Blocked
                && response.Summary.Contains("action_not_allowed_for_stage", StringComparison.Ordinal)
                && response.Summary.Contains("Rejected action kind for current stage.", StringComparison.Ordinal));
    }

    [Fact]
    public void DecodeFailureUsesFixedUiErrorCodeWithoutRawPayloadEcho()
    {
        var service = new HarnessStageCockpitService();

        var fixture = service.ApplySuggestedAction("suggestion.decode.failure");

        Assert.Contains(
            fixture.SuggestedActions.Outcomes,
            outcome => outcome.SuggestionId == "suggestion.decode.failure"
                && outcome.State == "blocked"
                && outcome.ActionKind == "defer_slot"
                && outcome.ErrorCode == "PCH_UI_DECODE_MALFORMED_JSON"
                && outcome.TraceOutcome == "suggestion.blocked"
                && outcome.BlockedReason == "Action proposal arguments are malformed JSON.");
        Assert.DoesNotContain(
            fixture.Session.Responses,
            response => response.Summary.Contains("RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK", StringComparison.Ordinal));
    }

    [Fact]
    public void RunModelAcceptedRoutesThroughProviderBridgeDecoderAndIntake()
    {
        var service = new HarnessStageCockpitService();

        var fixture = service.RunModelSuggestion("server-model.accept.defer-slot");

        Assert.Contains(
            fixture.ModelSuggestionRuns.Outcomes,
            outcome => outcome.RunId == "server-model.accept.defer-slot"
                && outcome.State == "accepted"
                && outcome.ActionKind == "defer_slot"
                && outcome.DecodeOutcomeCode == "decode_accepted"
                && outcome.IntakeOutcomeCode == "accepted"
                && outcome.TraceOutcome == "server_model.accepted"
                && outcome.Provider == "deterministic-mock"
                && outcome.Model == "mock-stage-action");
        Assert.Contains(
            fixture.Trace.Claims,
            claim => claim.Text == "deferred_slot_count: 1");
        Assert.Contains(
            fixture.Session.Responses,
            response => response.State == SessionResponseState.Applied
                && response.Target == "server-model.accept.defer-slot"
                && response.Summary.Contains("server_model.accepted", StringComparison.Ordinal));
    }

    [Fact]
    public void RunModelBlockedRoutesThroughIntakeWithoutMutation()
    {
        var service = new HarnessStageCockpitService();

        var fixture = service.RunModelSuggestion("server-model.block.form-mismatch");

        Assert.DoesNotContain(
            fixture.Trace.Claims,
            claim => claim.Text == "deferred_slot_count: 1");
        Assert.Contains(
            fixture.ModelSuggestionRuns.Outcomes,
            outcome => outcome.RunId == "server-model.block.form-mismatch"
                && outcome.State == "blocked"
                && outcome.ActionKind == "emit_form"
                && outcome.DecodeOutcomeCode == "decode_accepted"
                && outcome.IntakeOutcomeCode == "form_id_mismatch"
                && outcome.TraceOutcome == "form_id_mismatch"
                && outcome.ErrorCode == "PCH_UI_INTAKE_FORM_ID_MISMATCH"
                && outcome.BlockedReason == "Rejected form action that does not match pending form.");
    }

    [Fact]
    public void RunModelDecodeFailureUsesSanitizedCodeWithoutRawPayloadEcho()
    {
        var service = new HarnessStageCockpitService();

        var fixture = service.RunModelSuggestion("server-model.decode.missing-argument");

        Assert.Contains(
            fixture.ModelSuggestionRuns.Outcomes,
            outcome => outcome.RunId == "server-model.decode.missing-argument"
                && outcome.State == "blocked"
                && outcome.ActionKind == "defer_slot"
                && outcome.DecodeOutcomeCode == "decode_accepted"
                && outcome.IntakeOutcomeCode == "intake_not_run_decode_failed"
                && outcome.TraceOutcome == "decode.failed"
                && outcome.ErrorCode == "PCH_UI_DECODE_MISSING_REQUIRED_ARGUMENT"
                && outcome.BlockedReason == "Action proposal is missing a required argument.");
        Assert.DoesNotContain(
            fixture.Session.Responses,
            response => response.Summary.Contains("RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK", StringComparison.Ordinal));
    }
}
