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

    [Fact]
    public void PromptAcceptedAppliesMissionFactsWithoutRawPrompt()
    {
        var service = new HarnessStageCockpitService();

        var fixture = service.RunPromptIntake("prompt.accepted");
        var serialized = SerializePromptFixture(fixture);

        Assert.Contains(
            fixture.PromptIntake.Outcomes,
            outcome => outcome.RunId == "prompt.accepted"
                && outcome.State == "applied"
                && outcome.PromptPacketOutcomeCode == "prompt_packet_built"
                && outcome.ProviderRuntimeOutcomeCode == "mission_planner_decode_accepted"
                && outcome.AdapterOutcomeCode == "adapter_accepted"
                && outcome.IntakeOutcomeCode == "mission_intake_applied"
                && outcome.MemoryDigestOutcomeCode == "memory_digest_updated"
                && outcome.TraceOutcome == "prompt_intake.applied");
        Assert.Contains(
            fixture.PromptIntake.AppliedFields,
            field => field.FieldId == "mission.purpose"
                && field.Value == "vacation"
                && field.Source == "user-stated");
        Assert.Contains(
            fixture.PromptIntake.MemoryDigestFacts,
            fact => fact.Text == "destination_country: Japan");
        AssertPromptRawTextAbsent(serialized);
    }

    [Fact]
    public void PromptPendingKeepsModelInferencesConfirmationReady()
    {
        var service = new HarnessStageCockpitService();

        var fixture = service.RunPromptIntake("prompt.pending");
        var serialized = SerializePromptFixture(fixture);

        Assert.Contains(
            fixture.PromptIntake.Outcomes,
            outcome => outcome.RunId == "prompt.pending"
                && outcome.State == "proposed"
                && outcome.PromptPacketOutcomeCode == "prompt_packet_built"
                && outcome.ProviderRuntimeOutcomeCode == "mission_planner_decode_accepted"
                && outcome.AdapterOutcomeCode == "adapter_accepted"
                && outcome.IntakeOutcomeCode == "mission_intake_applied"
                && outcome.TraceOutcome == "prompt_intake.proposed");
        Assert.Contains(
            fixture.PromptIntake.PendingConfirmations,
            field => field.FieldId == "mission.destination_country"
                && field.Source == "model-inferred");
        AssertPromptRawTextAbsent(serialized);
    }

    [Fact]
    public void PromptProviderBlockedStopsBeforeAdapterAndDigest()
    {
        var service = new HarnessStageCockpitService();

        var fixture = service.RunPromptIntake("prompt.provider-blocked");
        var serialized = SerializePromptFixture(fixture);

        Assert.Contains(
            fixture.PromptIntake.Outcomes,
            outcome => outcome.RunId == "prompt.provider-blocked"
                && outcome.State == "blocked"
                && outcome.PromptPacketOutcomeCode == "prompt_packet_built"
                && outcome.ProviderRuntimeOutcomeCode == "mission_planner_decode_packet_id_mismatch"
                && outcome.AdapterOutcomeCode == "not_run"
                && outcome.IntakeOutcomeCode == "not_run"
                && outcome.MemoryDigestOutcomeCode == "not_run"
                && outcome.ErrorCode == "PCH_UI_MISSION_PROVIDER_PACKET_ID_MISMATCH");
        Assert.Empty(fixture.PromptIntake.AppliedFields);
        Assert.Empty(fixture.PromptIntake.MemoryDigestFacts);
        AssertPromptRawTextAbsent(serialized);
    }

    [Fact]
    public void PromptAdapterBlockedKeepsSanitizedAdapterFailure()
    {
        var service = new HarnessStageCockpitService();

        var fixture = service.RunPromptIntake("prompt.adapter-blocked");
        var serialized = SerializePromptFixture(fixture);

        Assert.Contains(
            fixture.PromptIntake.Outcomes,
            outcome => outcome.RunId == "prompt.adapter-blocked"
                && outcome.State == "blocked"
                && outcome.PromptPacketOutcomeCode == "prompt_packet_built"
                && outcome.ProviderRuntimeOutcomeCode == "mission_planner_decode_accepted"
                && outcome.AdapterOutcomeCode == "unsupported_field_path"
                && outcome.IntakeOutcomeCode == "not_run"
                && outcome.MemoryDigestOutcomeCode == "not_run"
                && outcome.ErrorCode == "PCH_UI_MISSION_ADAPTER_UNSUPPORTED_FIELD_PATH");
        Assert.DoesNotContain(
            fixture.PromptIntake.AppliedFields,
            field => field.FieldId == "mission.freeform_secret_note");
        Assert.DoesNotContain("freeform_secret_note", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("do not persist", serialized, StringComparison.Ordinal);
        AssertPromptRawTextAbsent(serialized);
    }

    [Fact]
    public void PromptValidationBlocksBlankAndOverlongBeforeProviderRuntime()
    {
        var service = new HarnessStageCockpitService();

        var blank = service.RunPromptIntake("prompt.blank");
        var overlong = service.RunPromptIntake("prompt.overlong");
        var serialized = JsonSerializer.Serialize(new
        {
            BlankPromptIntake = blank.PromptIntake,
            OverlongPromptIntake = overlong.PromptIntake,
            overlong.Session
        });

        Assert.Contains(
            blank.PromptIntake.Outcomes,
            outcome => outcome.RunId == "prompt.blank"
                && outcome.State == "blocked"
                && outcome.PromptPacketOutcomeCode == "invalid_prompt"
                && outcome.ProviderRuntimeOutcomeCode == "not_run"
                && outcome.AdapterOutcomeCode == "not_run"
                && outcome.IntakeOutcomeCode == "not_run"
                && outcome.MemoryDigestOutcomeCode == "not_run"
                && outcome.ErrorCode == "PCH_UI_PROMPT_PACKET_INVALID_PROMPT");
        Assert.Contains(
            overlong.PromptIntake.Outcomes,
            outcome => outcome.RunId == "prompt.overlong"
                && outcome.State == "blocked"
                && outcome.PromptPacketOutcomeCode == "prompt_too_long"
                && outcome.ProviderRuntimeOutcomeCode == "not_run"
                && outcome.AdapterOutcomeCode == "not_run"
                && outcome.IntakeOutcomeCode == "not_run"
                && outcome.MemoryDigestOutcomeCode == "not_run"
                && outcome.ErrorCode == "PCH_UI_PROMPT_PACKET_TOO_LONG");
        Assert.DoesNotContain(new string('x', 4_001), serialized, StringComparison.Ordinal);
        AssertPromptRawTextAbsent(serialized);
    }

    private static string SerializePromptFixture(StageCockpitFixture fixture)
    {
        return JsonSerializer.Serialize(new
        {
            fixture.PromptIntake,
            fixture.Session
        });
    }

    private static void AssertPromptRawTextAbsent(string serialized)
    {
        Assert.DoesNotContain("RAW_USER_PROMPT_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("Family wants a calm vacation", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("Maybe Japan in October", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_RAMBLING_PROMPT_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_PACKET_ID_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void ItineraryAcceptedBuildsDaySkeletonAndDigestMarkers()
    {
        var service = new HarnessStageCockpitService();

        var fixture = service.RunItineraryDayPlanner("itinerary.accepted");
        var serialized = SerializeItineraryFixture(fixture);

        Assert.Contains(
            fixture.ItineraryDayPlanner.Outcomes,
            outcome => outcome.RunId == "itinerary.accepted"
                && outcome.State == "applied"
                && outcome.DayId == "day-20270402"
                && outcome.SelectedOutcome == "selected"
                && outcome.DeferredOutcome == "deferred"
                && outcome.BlockedOutcome == "none");
        Assert.Contains(
            fixture.ItineraryDayPlanner.Days,
            day => day.DayId == "day-20270402"
                && day.State == "accepted"
                && day.Slots.Any(slot => slot.SlotId == "slot-20270402-lunch"
                    && slot.SlotType == "meal"
                    && slot.State == "selected"
                    && slot.SelectedCandidateId == "slot-20270402-lunch-dining-1"));
        Assert.Contains(
            fixture.ItineraryDayPlanner.DigestFacts,
            fact => fact.Text == "date_window: 2027-04-02/2027-04-02");
        AssertItineraryRawTextAbsent(serialized);
    }

    [Fact]
    public void ItineraryCandidatePoolsPreserveCandidateIdsAndEvidence()
    {
        var service = new HarnessStageCockpitService();

        var fixture = service.RunItineraryDayPlanner("itinerary.accepted");

        Assert.Contains(
            fixture.ItineraryDayPlanner.CandidatePools,
            pool => pool.PoolId == "pool-slot-20270402-lunch"
                && pool.SlotId == "slot-20270402-lunch"
                && pool.Candidates.Any(candidate => candidate.CandidateId == "slot-20270402-lunch-dining-1"
                    && candidate.Category == "dining"
                    && candidate.EvidenceIds.Contains("evidence.candidate.meal")));
        Assert.Contains(
            fixture.ItineraryDayPlanner.Evidence,
            evidence => evidence.EvidenceId == "evidence.candidate.recovery"
                && evidence.Outcome == "deferred");
    }

    [Fact]
    public void ItinerarySelectionRunMarksSelectedCandidate()
    {
        var service = new HarnessStageCockpitService();

        var fixture = service.RunItineraryDayPlanner("itinerary.select-candidate");
        var serialized = SerializeItineraryFixture(fixture);

        Assert.Contains(
            fixture.ItineraryDayPlanner.Outcomes,
            outcome => outcome.RunId == "itinerary.select-candidate"
                && outcome.State == "applied"
                && outcome.SelectedOutcome == "selected"
                && outcome.DeferredOutcome == "none"
                && outcome.HoldOutcome == "none"
                && outcome.ApprovalId is null);
        Assert.Contains(
            fixture.ItineraryDayPlanner.Days,
            day => day.Slots.Any(slot => slot.SlotId == "slot-20270402-lunch"
                && slot.State == "selected"
                && slot.SelectedCandidateId == "slot-20270402-lunch-dining-1"));
        Assert.Empty(fixture.ItineraryDayPlanner.Holds);
        AssertItineraryRawTextAbsent(serialized);
    }

    [Fact]
    public void ItineraryWrongSlotCandidateBlocksThroughCanonicalApplication()
    {
        var service = new HarnessStageCockpitService();

        var fixture = service.RunItineraryDayPlanner("itinerary.select.wrong-slot");
        var serialized = SerializeItineraryFixture(fixture);

        Assert.Contains(
            fixture.ItineraryDayPlanner.Outcomes,
            outcome => outcome.RunId == "itinerary.select.wrong-slot"
                && outcome.State == "blocked"
                && outcome.BlockedOutcome == "blocked_candidate_application"
                && outcome.ErrorCode == "PCH_UI_ITINERARY_CANDIDATE_POOL_MISMATCH"
                && outcome.BlockedReason == "Itinerary candidate is not associated with the compiled slot.");
        Assert.Empty(fixture.ItineraryDayPlanner.Days);
        Assert.Empty(fixture.ItineraryDayPlanner.CandidatePools);
        Assert.Empty(fixture.ItineraryDayPlanner.Holds);
        AssertItineraryRawTextAbsent(serialized);
    }

    [Fact]
    public void ItineraryDeferRunMarksDeferredSlot()
    {
        var service = new HarnessStageCockpitService();

        var fixture = service.RunItineraryDayPlanner("itinerary.defer-slot");
        var serialized = SerializeItineraryFixture(fixture);

        Assert.Contains(
            fixture.ItineraryDayPlanner.Outcomes,
            outcome => outcome.RunId == "itinerary.defer-slot"
                && outcome.State == "applied"
                && outcome.SelectedOutcome == "none"
                && outcome.DeferredOutcome == "deferred"
                && outcome.HoldOutcome == "none");
        Assert.Contains(
            fixture.ItineraryDayPlanner.Days,
            day => day.Slots.Any(slot => slot.SlotId == "slot-20270402-activity"
                && slot.State == "deferred"
                && slot.SelectedCandidateId is null));
        Assert.Empty(fixture.ItineraryDayPlanner.Holds);
        AssertItineraryRawTextAbsent(serialized);
    }

    [Fact]
    public void ItineraryHoldApprovalRequiredKeepsApprovalMarkerWithoutCommit()
    {
        var service = new HarnessStageCockpitService();

        var fixture = service.RunItineraryDayPlanner("itinerary.hold.approval-required");
        var serialized = SerializeItineraryFixture(fixture);

        Assert.Contains(
            fixture.ItineraryDayPlanner.Outcomes,
            outcome => outcome.RunId == "itinerary.hold.approval-required"
                && outcome.State == "approval-required"
                && outcome.SelectedOutcome == "selected"
                && outcome.HoldOutcome == "hold_preparation_preview_ready"
                && outcome.ApprovalId == "approval-itinerary-hold-activity"
                && outcome.ErrorCode is null);
        Assert.Contains(
            fixture.ItineraryDayPlanner.Holds,
            hold => hold.HoldId == "hold-itinerary.hold.approval-required"
                && hold.SlotId == "slot-20270402-activity"
                && hold.CandidateId == "slot-20270402-activity-activity-1"
                && hold.ApprovalId == "approval-itinerary-hold-activity"
                && hold.Outcome == "hold_preparation_preview_ready"
                && hold.Provider == "mock-hold-preparation"
                && hold.ConfirmationId is null);
        Assert.Contains(
            fixture.Session.Responses,
            response => response.State == SessionResponseState.ApprovalRequired
                && response.ApprovalId == "approval-itinerary-hold-activity");
        AssertItineraryRawTextAbsent(serialized);
    }

    [Fact]
    public void ItineraryApprovedMockHoldUsesApprovalBeforeAdapterCommit()
    {
        var service = new HarnessStageCockpitService();

        var fixture = service.RunItineraryDayPlanner("itinerary.hold.approved");
        var serialized = SerializeItineraryFixture(fixture);

        Assert.Contains(
            fixture.ItineraryDayPlanner.Outcomes,
            outcome => outcome.RunId == "itinerary.hold.approved"
                && outcome.State == "applied"
                && outcome.HoldOutcome == "hold_preparation_hold_prepared"
                && outcome.ApprovalId == "approval-itinerary-hold-activity"
                && outcome.ErrorCode is null);
        Assert.Contains(
            fixture.ItineraryDayPlanner.Holds,
            hold => hold.HoldId == "hold-itinerary.hold.approved"
                && hold.Outcome == "hold_preparation_hold_prepared"
                && hold.Provider == "mock-hold-preparation"
                && hold.ConfirmationId is null
                && hold.ErrorCode is null);
        AssertItineraryRawTextAbsent(serialized);
    }

    [Fact]
    public void ItineraryMissingApprovalBlocksMockHold()
    {
        var service = new HarnessStageCockpitService();

        var fixture = service.RunItineraryDayPlanner("itinerary.hold.missing-approval");
        var serialized = SerializeItineraryFixture(fixture);

        Assert.Contains(
            fixture.ItineraryDayPlanner.Outcomes,
            outcome => outcome.RunId == "itinerary.hold.missing-approval"
                && outcome.State == "blocked"
                && outcome.BlockedOutcome == "blocked_missing_approval"
                && outcome.HoldOutcome == "hold_preparation_missing_approval"
                && outcome.ApprovalId == "approval-itinerary-hold-activity"
                && outcome.ErrorCode == "PCH_UI_ITINERARY_HOLD_APPROVAL_REQUIRED"
                && outcome.BlockedReason == "Mock hold requires approval before provider handoff.");
        Assert.Contains(
            fixture.ItineraryDayPlanner.Holds,
            hold => hold.HoldId == "hold-itinerary.hold.missing-approval"
                && hold.Outcome == "blocked_missing_approval"
                && hold.ErrorCode == "PCH_UI_ITINERARY_HOLD_APPROVAL_REQUIRED"
                && hold.ConfirmationId is null);
        AssertItineraryRawTextAbsent(serialized);
    }

    [Fact]
    public void ItineraryHoldProviderMismatchBlocksWithSanitizedMarkers()
    {
        var service = new HarnessStageCockpitService();

        var fixture = service.RunItineraryDayPlanner("itinerary.hold.provider-mismatch");
        var serialized = SerializeItineraryFixture(fixture);

        Assert.Contains(
            fixture.ItineraryDayPlanner.Outcomes,
            outcome => outcome.RunId == "itinerary.hold.provider-mismatch"
                && outcome.State == "blocked"
                && outcome.BlockedOutcome == "blocked_provider_mismatch"
                && outcome.HoldOutcome == "hold_preparation_packet_id_mismatch"
                && outcome.ApprovalId == "approval-itinerary-hold-activity"
                && outcome.ErrorCode == "PCH_UI_ITINERARY_HOLD_PROVIDER_MISMATCH"
                && outcome.BlockedReason == "Mock hold provider response did not match the selected candidate.");
        Assert.Contains(
            fixture.ItineraryDayPlanner.Holds,
            hold => hold.HoldId == "hold-itinerary.hold.provider-mismatch"
                && hold.SlotId == "slot-20270402-activity"
                && hold.CandidateId == "slot-20270402-activity-activity-1"
                && hold.Outcome == "blocked_provider_mismatch"
                && hold.ErrorCode == "PCH_UI_ITINERARY_HOLD_PROVIDER_MISMATCH");
        AssertItineraryRawTextAbsent(serialized);
        Assert.DoesNotContain("provider-hold-mismatch-candidate", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void ItineraryFixedCommitmentConflictBlocksWithSanitizedCode()
    {
        var service = new HarnessStageCockpitService();

        var fixture = service.RunItineraryDayPlanner("itinerary.conflict");
        var serialized = SerializeItineraryFixture(fixture);

        Assert.Contains(
            fixture.ItineraryDayPlanner.Outcomes,
            outcome => outcome.RunId == "itinerary.conflict"
                && outcome.State == "blocked"
                && outcome.DayId == "day-20270402"
                && outcome.BlockedOutcome == "blocked_conflict"
                && outcome.ErrorCode == "PCH_UI_ITINERARY_FIXED_COMMITMENT_CONFLICT"
                && outcome.BlockedReason == "Fixed commitment conflict blocks itinerary compilation.");
        Assert.Empty(fixture.ItineraryDayPlanner.Days);
        Assert.Empty(fixture.ItineraryDayPlanner.CandidatePools);
        AssertItineraryRawTextAbsent(serialized);
    }

    [Fact]
    public void ItineraryProviderSlotMismatchBlocksBeforeCandidateRendering()
    {
        var service = new HarnessStageCockpitService();

        var fixture = service.RunItineraryDayPlanner("itinerary.provider-mismatch");
        var serialized = SerializeItineraryFixture(fixture);

        Assert.Contains(
            fixture.ItineraryDayPlanner.Outcomes,
            outcome => outcome.RunId == "itinerary.provider-mismatch"
                && outcome.State == "blocked"
                && outcome.DayId == "day-20270402"
                && outcome.BlockedOutcome == "blocked_candidate_expansion"
                && outcome.ErrorCode == "PCH_UI_ITINERARY_CANDIDATE_SLOT_MISMATCH"
                && outcome.BlockedReason == "Candidate expansion returned an invalid compiled slot.");
        Assert.Empty(fixture.ItineraryDayPlanner.Days);
        Assert.Empty(fixture.ItineraryDayPlanner.CandidatePools);
        AssertItineraryRawTextAbsent(serialized);
        Assert.DoesNotContain("slot-provider-unknown", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("candidate-provider-unknown", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void ItineraryMissingDateWindowBlocksBeforeCandidates()
    {
        var service = new HarnessStageCockpitService();

        var fixture = service.RunItineraryDayPlanner("itinerary.missing-date");
        var serialized = SerializeItineraryFixture(fixture);

        Assert.Contains(
            fixture.ItineraryDayPlanner.Outcomes,
            outcome => outcome.RunId == "itinerary.missing-date"
                && outcome.State == "blocked"
                && outcome.DayId == ""
                && outcome.BlockedOutcome == "blocked_date_window"
                && outcome.ErrorCode == "PCH_UI_ITINERARY_MISSING_DATE_WINDOW"
                && outcome.BlockedReason == "Itinerary day planner requires an applied start and end date.");
        Assert.Empty(fixture.ItineraryDayPlanner.Days);
        Assert.Empty(fixture.ItineraryDayPlanner.CandidatePools);
        AssertItineraryRawTextAbsent(serialized);
    }

    [Fact]
    public void EndToEndHappyPathCarriesPromptMissionItineraryHoldAndExportMarkers()
    {
        var service = new HarnessStageCockpitService();

        var fixture = service.RunEndToEndTrip("e2e.happy-path");
        var serialized = SerializeEndToEndFixture(fixture);

        Assert.Contains(
            fixture.EndToEndTripRuns.Outcomes,
            outcome => outcome.RunId == "e2e.happy-path"
                && outcome.State == "applied"
                && outcome.PromptPacketOutcomeCode == "prompt_packet_built"
                && outcome.MissionOutcomeCode == "mission_intake_applied"
                && outcome.ItineraryOutcomeCode == "itinerary_day_compiled"
                && outcome.MemoryDigestOutcomeCode == "memory_digest_updated"
                && outcome.SelectedCount == 2
                && outcome.DeferredCount == 1
                && outcome.HoldOutcomeCode == "hold_preparation_hold_prepared"
                && outcome.ApprovalId == "approval-itinerary-hold-activity"
                && outcome.EvidencePacketId == "evidence-packet-e2e-happy-path"
                && outcome.ExportPacketId == "export-packet-e2e-happy-path"
                && outcome.ErrorCode is null);
        Assert.Contains(
            fixture.EndToEndTripRuns.Evidence,
            evidence => evidence.EvidenceId == "evidence-e2e-happy-path-hold"
                && evidence.ExportPacketId == "export-packet-e2e-happy-path"
                && evidence.Outcome == "hold_preparation_hold_prepared"
                && evidence.ReferenceId == "approval-itinerary-hold-activity");
        AssertEndToEndRawTextAbsent(serialized);
    }

    [Fact]
    public void EndToEndPendingConfirmationStopsBeforeItineraryAndHold()
    {
        var service = new HarnessStageCockpitService();

        var fixture = service.RunEndToEndTrip("e2e.pending-confirmation");
        var serialized = SerializeEndToEndFixture(fixture);

        Assert.Contains(
            fixture.EndToEndTripRuns.Outcomes,
            outcome => outcome.RunId == "e2e.pending-confirmation"
                && outcome.State == "proposed"
                && outcome.PromptPacketOutcomeCode == "prompt_packet_built"
                && outcome.MissionOutcomeCode == "mission_intake_applied"
                && outcome.ItineraryOutcomeCode == "itinerary_not_run_pending_confirmation"
                && outcome.SelectedCount == 0
                && outcome.DeferredCount == 0
                && outcome.HoldOutcomeCode == "not_run"
                && outcome.ApprovalId is null
                && outcome.ErrorCode is null);
        AssertEndToEndRawTextAbsent(serialized);
    }

    [Fact]
    public void EndToEndProviderCandidateMismatchBlocksBeforeHold()
    {
        var service = new HarnessStageCockpitService();

        var fixture = service.RunEndToEndTrip("e2e.provider-mismatch");
        var serialized = SerializeEndToEndFixture(fixture);

        Assert.Contains(
            fixture.EndToEndTripRuns.Outcomes,
            outcome => outcome.RunId == "e2e.provider-mismatch"
                && outcome.State == "blocked"
                && outcome.ItineraryOutcomeCode == "blocked_candidate_expansion"
                && outcome.HoldOutcomeCode == "not_run"
                && outcome.ErrorCode == "PCH_UI_ITINERARY_CANDIDATE_SLOT_MISMATCH"
                && outcome.BlockedReason == "Candidate expansion returned an invalid compiled slot.");
        AssertEndToEndRawTextAbsent(serialized);
        Assert.DoesNotContain("slot-provider-unknown", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("candidate-provider-unknown", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void EndToEndWrongSlotCandidateBlocksThroughCanonicalSelection()
    {
        var service = new HarnessStageCockpitService();

        var fixture = service.RunEndToEndTrip("e2e.wrong-slot");
        var serialized = SerializeEndToEndFixture(fixture);

        Assert.Contains(
            fixture.EndToEndTripRuns.Outcomes,
            outcome => outcome.RunId == "e2e.wrong-slot"
                && outcome.State == "blocked"
                && outcome.ItineraryOutcomeCode == "blocked_candidate_application"
                && outcome.HoldOutcomeCode == "not_run"
                && outcome.ErrorCode == "PCH_UI_ITINERARY_CANDIDATE_POOL_MISMATCH"
                && outcome.BlockedReason == "Itinerary candidate is not associated with the compiled slot.");
        AssertEndToEndRawTextAbsent(serialized);
    }

    [Fact]
    public void EndToEndMissingApprovalBlocksMockHold()
    {
        var service = new HarnessStageCockpitService();

        var fixture = service.RunEndToEndTrip("e2e.missing-approval");
        var serialized = SerializeEndToEndFixture(fixture);

        Assert.Contains(
            fixture.EndToEndTripRuns.Outcomes,
            outcome => outcome.RunId == "e2e.missing-approval"
                && outcome.State == "blocked"
                && outcome.ItineraryOutcomeCode == "blocked_missing_approval"
                && outcome.SelectedCount == 1
                && outcome.HoldOutcomeCode == "hold_preparation_missing_approval"
                && outcome.ApprovalId == "approval-itinerary-hold-activity"
                && outcome.ErrorCode == "PCH_UI_ITINERARY_HOLD_APPROVAL_REQUIRED"
                && outcome.BlockedReason == "Mock hold requires approval before provider handoff.");
        AssertEndToEndRawTextAbsent(serialized);
    }

    [Fact]
    public void EndToEndEvidenceExportMarkersAreStable()
    {
        var service = new HarnessStageCockpitService();

        var fixture = service.RunEndToEndTrip("e2e.raw-sentinel");
        var serialized = SerializeEndToEndFixture(fixture);

        Assert.Contains(
            fixture.EndToEndTripRuns.Outcomes,
            outcome => outcome.RunId == "e2e.raw-sentinel"
                && outcome.EvidencePacketId == "evidence-packet-e2e-raw-sentinel"
                && outcome.ExportPacketId == "export-packet-e2e-raw-sentinel"
                && outcome.TraceOutcome == "end_to_end.applied");
        Assert.Contains(
            fixture.EndToEndTripRuns.Evidence,
            evidence => evidence.EvidenceId == "evidence-e2e-raw-sentinel-prompt"
                && evidence.ExportPacketId == "export-packet-e2e-raw-sentinel"
                && evidence.Outcome == "mission_intake_applied");
        AssertEndToEndRawTextAbsent(serialized);
    }

    private static string SerializeEndToEndFixture(StageCockpitFixture fixture)
    {
        return JsonSerializer.Serialize(new
        {
            fixture.EndToEndTripRuns,
            fixture.Session
        });
    }

    private static void AssertEndToEndRawTextAbsent(string serialized)
    {
        Assert.DoesNotContain("RAW_END_TO_END_PROMPT_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("hold-reference", serialized, StringComparison.Ordinal);
        AssertItineraryRawTextAbsent(serialized);
    }

    private static string SerializeItineraryFixture(StageCockpitFixture fixture)
    {
        return JsonSerializer.Serialize(new
        {
            fixture.ItineraryDayPlanner,
            fixture.Session
        });
    }

    private static void AssertItineraryRawTextAbsent(string serialized)
    {
        Assert.DoesNotContain("RAW_USER_PROMPT_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_RAMBLING_PROMPT_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_PACKET_ID_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET_SENTINEL", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("ui-hold-approval-token-not-rendered", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("An approval token is required before hold, book, or pay can be committed.", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("slot-provider-unknown", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("candidate-provider-unknown", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_PACKET_ID_SHOULD_NOT_PERSIST", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("mock-hold-slot", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("structured-itinerary-hold-context", serialized, StringComparison.Ordinal);
    }
}
