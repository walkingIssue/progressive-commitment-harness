using System.Text.Json;
using Pch.Core;
using Pch.Harness;
using Xunit;

namespace Pch.Harness.Tests;

public sealed class LiveMultiTurnSessionConductorTests
{
    private static readonly DateTimeOffset FixedAt = new(2027, 4, 1, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ObservedAt = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void PromptToMissionAcceptedWithPendingConfirmationKeepsSessionAndProjectionTyped()
    {
        var session = SyntheticTripFactory.CreateSession(3);
        var conductor = new LiveMultiTurnSessionConductor(session);
        var start = conductor.StartInitialPrompt(InitialPrompt(session, "RAW_PROMPT_SHOULD_NOT_LEAK pending trip."));
        var proposal = PendingMissionProposal(start.ModelInput!);

        var result = conductor.ApplyModelProposal(new LiveModelProposalApplicationRequest(
            session.SessionId,
            proposal,
            ["vacation"]));
        var serialized = JsonSerializer.Serialize(result, JsonOptions);

        Assert.True(start.IsAccepted);
        Assert.False(start.DidMutate);
        Assert.Equal(LiveMultiTurnSessionConductor.AwaitingModelProposalCode, start.Code);
        Assert.Contains(LiveMultiTurnSessionConductor.ApplyModelProposalOperation, start.AllowedNextOperationKinds);
        Assert.True(result.IsAccepted);
        Assert.True(result.DidMutate);
        Assert.Equal(LiveSessionConductor.AwaitingUserInputCode, result.Code);
        Assert.Equal("form", result.AssistantWorkItemKind);
        Assert.Contains(LiveMultiTurnSessionConductor.SubmitConfirmationOperation, result.AllowedNextOperationKinds);
        Assert.Single(session.MemoryDigest!.PendingConfirmations);
        Assert.DoesNotContain("RAW_PROMPT_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void PromptMissionAcceptedThenUserConfirmationProducesChoiceWorkItemFromFreshState()
    {
        var session = SyntheticTripFactory.CreateSession(3);
        var conductor = new LiveMultiTurnSessionConductor(session);
        var start = conductor.StartInitialPrompt(InitialPrompt(session, "RAW_PROMPT_SHOULD_NOT_LEAK confirmation trip."));
        conductor.ApplyModelProposal(new LiveModelProposalApplicationRequest(
            session.SessionId,
            PendingMissionProposal(start.ModelInput!),
            ["vacation"]));

        var confirmation = conductor.SubmitConfirmation(new LiveUserConfirmationRequest(
            session.SessionId,
            [new("/mission/destination_country", LiveConfirmationDecision.Confirm, null)]));
        var fresh = conductor.BuildFreshModelInput(new LiveModelInputRefreshRequest(session.SessionId, "en-US", ["second-turn"]));
        var serialized = JsonSerializer.Serialize(new { confirmation, fresh }, JsonOptions);

        Assert.True(confirmation.IsAccepted);
        Assert.True(confirmation.DidMutate);
        Assert.Equal(LiveMultiTurnSessionConductor.ConfirmationAppliedCode, confirmation.Code);
        Assert.Equal("choice", confirmation.AssistantWorkItemKind);
        Assert.Contains(LiveMultiTurnSessionConductor.SelectOptionOperation, confirmation.AllowedNextOperationKinds);
        Assert.NotNull(session.LastItineraryCompilation);
        Assert.Empty(session.MemoryDigest!.PendingConfirmations);
        Assert.True(fresh.IsAccepted);
        Assert.False(fresh.DidMutate);
        Assert.NotNull(fresh.ModelInput);
        Assert.True(fresh.ModelInput!.MissionFactCount > 0);
        Assert.DoesNotContain("RAW_PROMPT_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("confirmation trip", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionSelectionThenQuotePreviewReturnsApprovalRequiredWithoutHoldOrTokenMutation()
    {
        var session = PreparedChoiceSession();
        var conductor = new LiveMultiTurnSessionConductor(session);
        var slot = ActivitySlot(session);
        session.AddItineraryCandidatePool(slot.SlotId, CandidatePoolFor(
            "pool-priced-live",
            new Candidate(
                "candidate-priced-live",
                CandidateKind.Activity,
                "RAW_CANDIDATE_DISPLAY_SHOULD_NOT_LEAK",
                "RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK",
                125,
                "USD",
                ["evidence-priced-live"],
                140)));

        var selected = conductor.ApplyOptionDecision(new LiveOptionDecisionRequest(
            session.SessionId,
            slot.SlotId,
            ItinerarySlotDecisionKind.Selected,
            slot.Kind,
            "candidate-priced-live",
            CandidateKind.Activity,
            FixedAt));
        var app = new AvailabilityQuotePreviewApplication();
        var context = app.CurrentContext(session);
        var preview = conductor.PreviewAvailability(new LiveAvailabilityPreviewTurnRequest(
            session.SessionId,
            new AvailabilityQuotePreviewRequest(
                session.SessionId,
                slot.SlotId,
                "candidate-priced-live",
                slot.Kind,
                CandidateKind.Activity,
                AvailabilityQuoteKind.Quote,
                context.CompilationFingerprint,
                context.SnapshotId,
                FixedAt)));
        var serialized = JsonSerializer.Serialize(new { selected, preview }, JsonOptions);

        Assert.True(selected.IsAccepted);
        Assert.True(selected.DidMutate);
        Assert.Equal("choice_echo", selected.AssistantWorkItemKind);
        Assert.False(preview.IsAccepted);
        Assert.True(preview.IsBlocked);
        Assert.False(preview.DidMutate);
        Assert.Equal(AvailabilityQuotePreviewApplication.ApprovalRequiredCode, preview.Code);
        Assert.Equal("blocked", preview.AssistantWorkItemKind);
        Assert.DoesNotContain("RAW_CANDIDATE_DISPLAY_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("APPROVAL_TOKEN", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderBlockedOnSecondTurnDoesNotMutateAndKeepsFreshModelInput()
    {
        var session = PreparedChoiceSession();
        var conductor = new LiveMultiTurnSessionConductor(session);
        var fresh = conductor.BuildFreshModelInput(new LiveModelInputRefreshRequest(session.SessionId, "en-US", ["second-turn"]));
        var before = Counts(session);

        var blocked = conductor.RecordProviderModelBlocked(new LiveProviderModelBlockedRequest(
            session.SessionId,
            "RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK"));
        var serialized = JsonSerializer.Serialize(blocked, JsonOptions);

        Assert.True(fresh.IsAccepted);
        Assert.NotNull(fresh.ModelInput);
        Assert.False(blocked.IsAccepted);
        Assert.True(blocked.IsBlocked);
        Assert.False(blocked.DidMutate);
        Assert.Equal(LiveSessionConductor.ProviderModelBlockedCode, blocked.Code);
        Assert.Equal(before, Counts(session));
        Assert.DoesNotContain("RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedProposalUnsupportedOperationStaleAndInvalidCommitmentDoNotMutateOrLeak()
    {
        var malformed = ApplyProposalToFreshSession(LiveModelProposalEnvelope.ForMission(new ProviderMissionProposalMirror(
            "proposal-malformed",
            null!,
            [],
            [])));
        var unsupported = ApplyProposalToFreshSession(LiveModelProposalEnvelope.ForMission(new ProviderMissionProposalMirror(
            "proposal-unsupported",
            [new("/mission/purpose", "Should not apply", "user", ["evidence-user"])],
            [],
            [])) with
        {
            OperationCode = "unsupported_operation"
        });
        var staleSession = SyntheticTripFactory.CreateSession(3);
        var staleConductor = new LiveMultiTurnSessionConductor(staleSession);
        var staleStart = staleConductor.StartInitialPrompt(InitialPrompt(staleSession, "Fresh prompt."));
        var staleBefore = Counts(staleSession);
        var stale = staleConductor.ApplyModelProposal(new LiveModelProposalApplicationRequest(
            staleSession.SessionId,
            PendingMissionProposal(staleStart.ModelInput!) with
            {
                Correlation = new LiveProposalCorrelation(staleSession.SessionId, "packet-stale", staleStart.ModelInput!.Stage)
            },
            []));
        var invalidCommitment = ApplyProposalToFreshSession(LiveModelProposalEnvelope.ForMission(new ProviderMissionProposalMirror(
            "proposal-invalid-commitment",
            [],
            [],
            [
                new(
                    "commitment-live-bad",
                    "RAW_KIND_SHOULD_NOT_LEAK",
                    "RAW_CANDIDATE_DISPLAY_SHOULD_NOT_LEAK",
                    null,
                    null,
                    null,
                    false,
                    false,
                    "high",
                    "user",
                    ["evidence-invalid-commitment"])
            ])));
        var serialized = JsonSerializer.Serialize(new { malformed, unsupported, stale, invalidCommitment }, JsonOptions);

        AssertBlockedNoMutation(malformed.Result, malformed.Before, malformed.Session, LiveSessionConductor.MissionProposalBlockedCode);
        AssertBlockedNoMutation(unsupported.Result, unsupported.Before, unsupported.Session, LiveSessionConductor.UnsupportedLiveOperationCode);
        AssertBlockedNoMutation(stale, staleBefore, staleSession, LiveSessionConductor.StalePacketSessionCode);
        AssertBlockedNoMutation(invalidCommitment.Result, invalidCommitment.Before, invalidCommitment.Session, LiveSessionConductor.MissionProposalBlockedCode);
        Assert.DoesNotContain("RAW_KIND_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_CANDIDATE_DISPLAY_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void ApprovalRequiredAndInvalidCandidateDoNotMutate()
    {
        var approvalSession = SyntheticTripFactory.CreateSession(3);
        approvalSession.MoveTo(HarnessStage.ApprovalQueue);
        var approvalConductor = new LiveMultiTurnSessionConductor(approvalSession);
        approvalConductor.StartInitialPrompt(InitialPrompt(approvalSession, "Approval turn."));
        var approvalBefore = Counts(approvalSession);
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

        var approval = approvalConductor.ApplyModelProposal(new LiveModelProposalApplicationRequest(
            approvalSession.SessionId,
            LiveModelProposalEnvelope.ForRuntimeAction(new ExternalActionProposal(
                "approval-live",
                HarnessAction.RequestApprovalKind,
                document.RootElement.Clone())),
            []));

        var candidateSession = PreparedChoiceSession();
        var candidateConductor = new LiveMultiTurnSessionConductor(candidateSession);
        var slot = ActivitySlot(candidateSession);
        var candidateBefore = Counts(candidateSession);
        var invalidCandidate = candidateConductor.ApplyOptionDecision(new LiveOptionDecisionRequest(
            candidateSession.SessionId,
            slot.SlotId,
            ItinerarySlotDecisionKind.Selected,
            slot.Kind,
            "candidate-missing",
            CandidateKind.Activity,
            FixedAt));
        var serialized = JsonSerializer.Serialize(new { approval, invalidCandidate }, JsonOptions);

        AssertBlockedNoMutation(approval, approvalBefore, approvalSession, LiveSessionConductor.ApprovalRequiredCode);
        AssertBlockedNoMutation(invalidCandidate, candidateBefore, candidateSession, "unknown_candidate");
        Assert.DoesNotContain("RAW_PROMPT_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_APPROVAL_TOKEN_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
    }

    private static LiveInitialPromptRequest InitialPrompt(TripSession session, string prompt)
    {
        return new(session.SessionId, prompt, "en-US", ["vacation"]);
    }

    private static LiveModelProposalEnvelope PendingMissionProposal(LiveModelInputFragment input)
    {
        return LiveModelProposalEnvelope.ForMission(new ProviderMissionProposalMirror(
            "planner-live-pending",
            [
                new("/mission/purpose", "Pending confirmation trip", "user", ["evidence-live-user"]),
                new("/mission/destination_country", "Japan", "strong_model_inference", ["evidence-live-model"])
            ],
            [],
            [])) with
        {
            Correlation = new LiveProposalCorrelation(input.SessionId, input.PacketId, input.Stage)
        };
    }

    private static TripSession PreparedChoiceSession()
    {
        var session = SyntheticTripFactory.CreateSession(3);
        var compile = new ItinerarySlotCompiler().Compile(session, new ItineraryCompilationRequest(
            session.SessionId,
            null,
            null,
            session.MemoryDigest,
            []));
        Assert.True(compile.IsCompiled);
        var slot = ActivitySlot(session);
        session.AssociateItineraryCandidatePool(slot.SlotId, "pool-logistics");
        return session;
    }

    private static ItinerarySlot ActivitySlot(TripSession session)
    {
        return session.LastItineraryCompilation!.Days
            .SelectMany(day => day.Slots)
            .First(slot => slot.Kind == ItinerarySlotKind.Activity);
    }

    private static CandidatePool CandidatePoolFor(string poolId, Candidate candidate)
    {
        return new(poolId, "all", [candidate], [], ObservedAt);
    }

    private static (TripSession Session, LiveMutationCounts Before, LiveMultiTurnSessionResult Result) ApplyProposalToFreshSession(
        LiveModelProposalEnvelope proposal)
    {
        var session = SyntheticTripFactory.CreateSession(3);
        var conductor = new LiveMultiTurnSessionConductor(session);
        conductor.StartInitialPrompt(InitialPrompt(session, "RAW_PROMPT_SHOULD_NOT_LEAK"));
        var before = Counts(session);
        var result = conductor.ApplyModelProposal(new LiveModelProposalApplicationRequest(session.SessionId, proposal, []));
        return (session, before, result);
    }

    private static LiveMutationCounts Counts(TripSession session)
    {
        return new(
            session.Actions.Count,
            session.DecisionLedger.Records.Count,
            session.ItineraryDecisions.Count,
            session.ApprovalTokens.Count,
            session.DeferredSlots.Count,
            session.MemoryDigest?.DigestId,
            session.LastItineraryCompilation?.Code,
            session.Mission);
    }

    private static void AssertBlockedNoMutation(
        LiveMultiTurnSessionResult result,
        LiveMutationCounts before,
        TripSession session,
        string expectedCode)
    {
        Assert.False(result.IsAccepted);
        Assert.True(result.IsBlocked);
        Assert.False(result.DidMutate);
        Assert.Equal(expectedCode, result.Code);
        Assert.Equal(before, Counts(session));
    }

    private sealed record LiveMutationCounts(
        int Actions,
        int Decisions,
        int ItineraryDecisions,
        int Approvals,
        int DeferredSlots,
        string? DigestId,
        string? CompilationCode,
        TripMission Mission);
}
