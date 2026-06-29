using Pch.UI.Features.StageCockpit;
using System.Text.Json;
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
                && outcome.BridgeOutcomeCode == "decode_accepted"
                && outcome.RuntimeDecodeOutcomeCode == "decoded"
                && outcome.RuntimeIntakeOutcomeCode == "accepted"
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
                && outcome.BridgeOutcomeCode == "decode_accepted"
                && outcome.RuntimeDecodeOutcomeCode == "decoded"
                && outcome.RuntimeIntakeOutcomeCode == "form_id_mismatch"
                && outcome.TraceOutcome == "form_id_mismatch"
                && outcome.ErrorCode == "PCH_UI_RUNTIME_INTAKE_FORM_ID_MISMATCH"
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
                && outcome.BridgeOutcomeCode == "decode_accepted"
                && outcome.RuntimeDecodeOutcomeCode == "missing_required_argument"
                && outcome.RuntimeIntakeOutcomeCode == "not_run"
                && outcome.TraceOutcome == "missing_required_argument"
                && outcome.ErrorCode == "PCH_UI_RUNTIME_DECODE_MISSING_REQUIRED_ARGUMENT"
                && outcome.BlockedReason == "Action proposal is missing a required argument.");
        Assert.DoesNotContain(
            fixture.Session.Responses,
            response => response.Summary.Contains("RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK", StringComparison.Ordinal));
    }

    [Fact]
    public void MissionVacationIntakeAppliesUserStatedFactsAndDigest()
    {
        var service = new HarnessStageCockpitService();

        var fixture = service.RunMissionIntake("mission.vacation");

        Assert.Contains(
            fixture.MissionIntake.Outcomes,
            outcome => outcome.RunId == "mission.vacation"
                && outcome.State == "applied"
                && outcome.ProviderRuntimeOutcomeCode == "mission_planner_decode_accepted"
                && outcome.AdapterOutcomeCode == "adapter_accepted"
                && outcome.PlannerOutcomeCode == "planner_mock_accepted"
                && outcome.IntakeOutcomeCode == "mission_intake_applied"
                && outcome.MemoryDigestOutcomeCode == "memory_digest_updated"
                && outcome.TraceOutcome == "mission_intake.applied");
        Assert.Contains(
            fixture.MissionIntake.AppliedFields,
            field => field.FieldId == "mission.purpose"
                && field.Value == "vacation"
                && field.Source == "user-stated"
                && field.State == "applied");
        Assert.Contains(
            fixture.MissionIntake.MemoryDigestFacts,
            fact => fact.Text == "destination_country: Japan");
    }

    [Fact]
    public void MissionNonVacationIntakeSurfacesHighPriorityCommitment()
    {
        var service = new HarnessStageCockpitService();

        var fixture = service.RunMissionIntake("mission.non-vacation-commitment");

        Assert.Contains(
            fixture.MissionIntake.AppliedFields,
            field => field.FieldId == "mission.purpose"
                && field.Value == "helping family"
                && field.Source == "user-stated");
        Assert.Contains(
            fixture.MissionIntake.HighPriorityCommitments,
            commitment => commitment.CommitmentId == "commitment.family-anchor"
                && commitment.Priority == "high"
                && commitment.Source == "user-stated");
        Assert.Contains(
            fixture.MissionIntake.Outcomes,
            outcome => outcome.RunId == "mission.non-vacation-commitment"
                && outcome.ProviderRuntimeOutcomeCode == "mission_planner_decode_accepted"
                && outcome.AdapterOutcomeCode == "adapter_accepted"
                && outcome.IntakeOutcomeCode == "mission_intake_applied");
        Assert.Contains(
            fixture.MissionIntake.MemoryDigestFacts,
            fact => fact.Text == "commitment: Attend family support appointment");
    }

    [Fact]
    public void MissionPendingConfirmationDoesNotExposeRawPromptOrProviderPayload()
    {
        var service = new HarnessStageCockpitService();

        var fixture = service.RunMissionIntake("mission.pending-confirmation");
        var serialized = JsonSerializer.Serialize(new
        {
            fixture.MissionIntake,
            fixture.Session
        });

        Assert.Contains(
            fixture.MissionIntake.Outcomes,
            outcome => outcome.RunId == "mission.pending-confirmation"
                && outcome.State == "proposed"
                && outcome.ProviderRuntimeOutcomeCode == "mission_planner_decode_accepted"
                && outcome.AdapterOutcomeCode == "adapter_accepted"
                && outcome.IntakeOutcomeCode == "mission_intake_applied"
                && outcome.TraceOutcome == "mission_intake.proposed");
        Assert.Contains(
            fixture.MissionIntake.PendingConfirmations,
            field => field.FieldId == "mission.destination_country"
                && field.Source == "model-inferred"
                && field.State == "pending-confirmation");
        Assert.DoesNotContain("RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_RAMBLING_PROMPT_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("I know this is a mess", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void MissionValidationBlockedKeepsSanitizedProviderRuntimeFailure()
    {
        var service = new HarnessStageCockpitService();

        var fixture = service.RunMissionIntake("mission.validation-blocked");
        var serialized = JsonSerializer.Serialize(new
        {
            fixture.MissionIntake,
            fixture.Session
        });

        Assert.Contains(
            fixture.MissionIntake.Outcomes,
            outcome => outcome.RunId == "mission.validation-blocked"
                && outcome.State == "blocked"
                && outcome.ProviderRuntimeOutcomeCode == "mission_planner_decode_packet_id_mismatch"
                && outcome.AdapterOutcomeCode == "not_run"
                && outcome.PlannerOutcomeCode == "planner_mock_accepted"
                && outcome.IntakeOutcomeCode == "not_run"
                && outcome.MemoryDigestOutcomeCode == "not_run"
                && outcome.TraceOutcome == "mission_intake.blocked"
                && outcome.ErrorCode == "PCH_UI_MISSION_PROVIDER_PACKET_ID_MISMATCH"
                && outcome.BlockedReason == "Mission planner runtime blocked a packet/result mismatch.");
        Assert.DoesNotContain(
            fixture.MissionIntake.AppliedFields,
            field => field.FieldId == "mission.purpose" && field.Value == "vacation");
        Assert.DoesNotContain("RAW_PACKET_ID_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_RAMBLING_PROMPT_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void MissionAdapterBlockedKeepsSanitizedAdapterFailure()
    {
        var service = new HarnessStageCockpitService();

        var fixture = service.RunMissionIntake("mission.adapter-blocked");
        var serialized = JsonSerializer.Serialize(new
        {
            fixture.MissionIntake,
            fixture.Session
        });

        Assert.Contains(
            fixture.MissionIntake.Outcomes,
            outcome => outcome.RunId == "mission.adapter-blocked"
                && outcome.State == "blocked"
                && outcome.ProviderRuntimeOutcomeCode == "mission_planner_decode_accepted"
                && outcome.AdapterOutcomeCode == "unsupported_field_path"
                && outcome.IntakeOutcomeCode == "not_run"
                && outcome.MemoryDigestOutcomeCode == "not_run"
                && outcome.TraceOutcome == "mission_intake.blocked"
                && outcome.ErrorCode == "PCH_UI_MISSION_ADAPTER_UNSUPPORTED_FIELD_PATH"
                && outcome.BlockedReason == "Mission proposal contains an unsupported field path.");
        Assert.DoesNotContain(
            fixture.MissionIntake.AppliedFields,
            field => field.FieldId == "mission.freeform_secret_note");
        Assert.DoesNotContain("freeform_secret_note", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("do not persist", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_RAMBLING_PROMPT_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void MissionUnknownCommitmentKindBlocksAtAdapterWithoutRawEcho()
    {
        var service = new HarnessStageCockpitService();

        var fixture = service.RunMissionIntake("mission.unknown-commitment-kind");
        var serialized = JsonSerializer.Serialize(new
        {
            fixture.MissionIntake,
            fixture.Session
        });

        Assert.Contains(
            fixture.MissionIntake.Outcomes,
            outcome => outcome.RunId == "mission.unknown-commitment-kind"
                && outcome.State == "blocked"
                && outcome.ProviderRuntimeOutcomeCode == "mission_planner_decode_accepted"
                && outcome.AdapterOutcomeCode == "invalid_commitment"
                && outcome.IntakeOutcomeCode == "not_run"
                && outcome.MemoryDigestOutcomeCode == "not_run"
                && outcome.TraceOutcome == "mission_intake.blocked"
                && outcome.ErrorCode == "PCH_UI_MISSION_ADAPTER_INVALID_COMMITMENT"
                && outcome.BlockedReason == "Mission proposal commitment failed validation.");
        Assert.DoesNotContain(
            fixture.MissionIntake.HighPriorityCommitments,
            commitment => commitment.CommitmentId == "commitment.unknown-kind");
        Assert.DoesNotContain("RAW_UNKNOWN_KIND_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_UNKNOWN_COMMITMENT_TITLE_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("unsupported_commitment_kind", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("commitment.unknown-kind", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_RAMBLING_PROMPT_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
    }
}
