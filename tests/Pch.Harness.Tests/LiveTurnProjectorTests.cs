using System.Text.Json;
using Pch.Core;
using Pch.Harness;
using Xunit;

namespace Pch.Harness.Tests;

public sealed class LiveTurnProjectorTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    [Fact]
    public void PendingConfirmationTraceIsCanonicalAndDoesNotSerializeRawPrompt()
    {
        var result = new LiveTurnProjector().BuildPendingConfirmationTrace();
        var serialized = JsonSerializer.Serialize(result, JsonOptions);

        Assert.True(result.IsAccepted);
        Assert.False(result.IsBlocked);
        Assert.Equal(LiveTurnProjector.AwaitingUserInputCode, result.Code);
        Assert.Equal("pending_confirmation", result.Transcript.Scenario);
        Assert.Contains(result.Transcript.Turns, turn => turn.Kind == "form");
        Assert.Contains(result.Transcript.Turns.SelectMany(turn => turn.WorkItem?.Fields ?? []), field => field.FieldType == "confirmation");
        Assert.Equal(64, result.Transcript.TranscriptHash.Length);
        Assert.DoesNotContain("RAW_PROMPT_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("user wants a trip", serialized, StringComparison.Ordinal);
        AssertFixture(result, "pending_confirmation.json");
    }

    [Fact]
    public void SessionTurnProjectionCoversFormChoiceApprovalSummaryEvidenceAndBlockedTurns()
    {
        var projector = new LiveTurnProjector();
        var session = SyntheticTripFactory.CreateSession(1);
        var packet = new ProjectionService().Project(session, session.Stage);

        var form = projector.FromSessionTurn(SessionTurnResult.Continued(
            session.Stage,
            packet,
            new EmitFormAction("action-form", new FormRequest(
                "form-live",
                "Trip basics",
                "Continue",
                [new("destination", "Destination", "text", true, null, [])]))));
        var choice = projector.FromSessionTurn(SessionTurnResult.Continued(
            session.Stage,
            packet,
            new EmitChoiceSetAction(
                "action-choice",
                "RAW_CANDIDATE_DISPLAY_SHOULD_NOT_LEAK",
                [new("candidate-safe", "Activity", "RAW_CANDIDATE_DISPLAY_SHOULD_NOT_LEAK", "RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK", ["evidence-choice"])],
                1)));
        var approval = projector.FromSessionTurn(SessionTurnResult.Continued(
            HarnessStage.ApprovalQueue,
            packet,
            new RequestApprovalAction("action-approval", new ApprovalRequest(
                "approval-live",
                "booking-preview",
                "RAW_PROMPT_SHOULD_NOT_LEAK",
                ["booking"],
                25,
                "USD",
                "APPROVAL_TOKEN_SHOULD_NOT_LEAK"))));
        var summary = projector.FromSessionTurn(SessionTurnResult.Continued(
            session.Stage,
            packet,
            new SummarizeAction("action-summary", "user", ["claim-purpose"])));
        var evidence = projector.FromSessionTurn(SessionTurnResult.Continued(
            session.Stage,
            packet,
            null!,
            trace: [new("trace-live", "Intake", "summary", "accepted", "Accepted.")]));
        var blocked = projector.FromSessionTurn(SessionTurnResult.Blocked(
            session.Stage,
            packet,
            "RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK",
            [new("trace-blocked-live", "Intake", "handoff", "action_not_allowed_for_stage", "Rejected.")]));
        var fallback = projector.DeterministicFallback();

        Assert.Equal("form", Assert.Single(form.Transcript.Turns).Kind);
        Assert.Equal("choice", Assert.Single(choice.Transcript.Turns).Kind);
        Assert.Equal("approval", Assert.Single(approval.Transcript.Turns).Kind);
        Assert.Equal("summary", Assert.Single(summary.Transcript.Turns).Kind);
        Assert.Equal("evidence", Assert.Single(evidence.Transcript.Turns).Kind);
        Assert.Equal("blocked", Assert.Single(blocked.Transcript.Turns).Kind);
        Assert.Equal(LiveTurnProjector.DeterministicFallbackCode, fallback.Code);

        var serialized = JsonSerializer.Serialize(new[] { form, choice, approval, summary, evidence, blocked, fallback }, JsonOptions);
        Assert.DoesNotContain("RAW_CANDIDATE_DISPLAY_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_PROMPT_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("APPROVAL_TOKEN_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.Contains("candidate-safe", serialized, StringComparison.Ordinal);
        Assert.Contains("activity", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeDecodeAndIntakeBlocksDoNotMutateOrLeakRawInput()
    {
        var projector = new LiveTurnProjector();
        var runtime = new RuntimeActionApplication();
        var decodeBlockedSession = SyntheticTripFactory.CreateSession(7);
        var intakeBlockedSession = SyntheticTripFactory.CreateSession(7);

        var decodeBlocked = runtime.ApplyJson(
            decodeBlockedSession,
            "action-live",
            HarnessAction.DeferSlotKind,
            """{ "slot_id": "RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK" """);
        var projectedDecode = projector.FromRuntimeAction(decodeBlocked);

        var intakeBlocked = runtime.ApplyJson(
            intakeBlockedSession,
            "action-handoff",
            HarnessAction.HandoffKind,
            """{ "target": "strong-model-auditor", "reason": "RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK" }""");
        var projectedIntake = projector.FromRuntimeAction(intakeBlocked);

        Assert.True(projectedDecode.IsBlocked);
        Assert.Equal(LiveTurnProjector.ProviderModelBlockedCode, projectedDecode.Code);
        Assert.True(projectedIntake.IsBlocked);
        Assert.Equal(LiveTurnProjector.IntakeBlockedCode, projectedIntake.Code);
        Assert.Empty(decodeBlockedSession.Actions);
        Assert.Empty(intakeBlockedSession.Actions);
        Assert.Empty(intakeBlockedSession.Handoffs);

        var serialized = JsonSerializer.Serialize(new[] { projectedDecode, projectedIntake }, JsonOptions);
        Assert.DoesNotContain("RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProviderSuppliedApprovalTokenProjectsAsApprovalRequiredWithoutTokenLeakOrMutation()
    {
        const string sentinelToken = "APPROVAL_TOKEN_SHOULD_NOT_LEAK";
        var session = SyntheticTripFactory.CreateSession(7);
        session.MoveTo(HarnessStage.ApprovalQueue);

        var runtime = new RuntimeActionApplication().ApplyJson(
            session,
            "action-approval",
            HarnessAction.RequestApprovalKind,
            $$"""
            {
              "approval_id": "approval-review",
              "approval_action_id": "mock-booking",
              "prompt": "RAW_PROMPT_SHOULD_NOT_LEAK",
              "risk_flags": ["booking"],
              "approval_token": "{{sentinelToken}}"
            }
            """);
        var projected = new LiveTurnProjector().FromRuntimeAction(runtime);
        var serialized = JsonSerializer.Serialize(projected, JsonOptions);

        Assert.True(projected.IsBlocked);
        Assert.Equal(LiveTurnProjector.ApprovalRequiredCode, projected.Code);
        Assert.Empty(session.Actions);
        Assert.Empty(session.ApprovalTokens);
        Assert.DoesNotContain(sentinelToken, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_PROMPT_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void ItinerarySelectionProjectionEmitsSafeUserChoiceEchoWithTrustedMetadata()
    {
        var session = SyntheticTripFactory.CreateSession(1);
        var compilation = new ItinerarySlotCompiler().Compile(session, new ItineraryCompilationRequest(
            session.SessionId,
            null,
            null,
            session.MemoryDigest,
            []));
        var slot = compilation.Days.SelectMany(day => day.Slots).First(slot => slot.Kind == ItinerarySlotKind.Meal);
        session.AddItineraryCandidatePool(slot.SlotId, new CandidatePool(
            "pool-live-meal",
            "meal",
            [
                new(
                    "candidate-live-meal",
                    CandidateKind.Restaurant,
                    "RAW_CANDIDATE_DISPLAY_SHOULD_NOT_LEAK",
                    "RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK",
                    null,
                    null,
                    ["evidence-live-meal"],
                    90)
            ],
            [],
            new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero)));

        var decision = new ItineraryCandidateApplication().Apply(session, new ItinerarySlotDecisionRequest(
            session.SessionId,
            slot.SlotId,
            ItinerarySlotDecisionKind.Selected,
            slot.Kind,
            "candidate-live-meal",
            CandidateKind.Restaurant,
            new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero)));
        var mediaManifest = new CardMediaProvenanceBoundary().BuildJapanMoodMediaManifest();
        var projected = new LiveTurnProjector().FromItineraryDecision(decision, mediaManifest);
        var serialized = JsonSerializer.Serialize(projected, JsonOptions);

        Assert.True(projected.IsAccepted);
        var turn = Assert.Single(projected.Transcript.Turns);
        Assert.Equal("choice_echo", turn.Kind);
        Assert.Equal("user", turn.Actor);
        Assert.Equal("candidate-live-meal", Assert.Single(turn.WorkItem!.Choices).CandidateId);
        Assert.Equal("meal", Assert.Single(turn.WorkItem.Choices).GroupFeel);
        Assert.NotNull(Assert.Single(turn.WorkItem.Choices).Media);
        Assert.Equal("japan-lively_food", Assert.Single(turn.WorkItem.Choices).Media!.MediaId);
        Assert.DoesNotContain("RAW_CANDIDATE_DISPLAY_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void PlanTrailProjectionCarriesSelectedAndDeferredCardMediaWithoutMutatingSession()
    {
        var session = SyntheticTripFactory.CreateSession(1);
        var compilation = new ItinerarySlotCompiler().Compile(session, new ItineraryCompilationRequest(
            session.SessionId,
            null,
            null,
            session.MemoryDigest,
            []));
        var mealSlot = compilation.Days.SelectMany(day => day.Slots).First(slot => slot.Kind == ItinerarySlotKind.Meal);
        var downtimeSlot = compilation.Days.SelectMany(day => day.Slots).First(slot => slot.Kind == ItinerarySlotKind.Downtime);
        session.AddItineraryCandidatePool(mealSlot.SlotId, new CandidatePool(
            "pool-plan-meal",
            "meal",
            [
                new(
                    "candidate-plan-meal",
                    CandidateKind.Restaurant,
                    "RAW_CANDIDATE_DISPLAY_SHOULD_NOT_LEAK",
                    "RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK",
                    null,
                    null,
                    ["evidence-plan-meal"],
                    90)
            ],
            [],
            new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero)));
        var selected = new ItineraryCandidateApplication().Apply(session, new ItinerarySlotDecisionRequest(
            session.SessionId,
            mealSlot.SlotId,
            ItinerarySlotDecisionKind.Selected,
            mealSlot.Kind,
            "candidate-plan-meal",
            CandidateKind.Restaurant,
            new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero)));
        var deferred = new ItineraryCandidateApplication().Apply(session, new ItinerarySlotDecisionRequest(
            session.SessionId,
            downtimeSlot.SlotId,
            ItinerarySlotDecisionKind.Deferred,
            downtimeSlot.Kind,
            null,
            null,
            new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero)));
        var actionCount = session.Actions.Count;
        var decisionCount = session.DecisionLedger.Records.Count;

        var trail = new LiveTurnProjector().BuildPlanTrail(
            [selected, deferred],
            new CardMediaProvenanceBoundary().BuildJapanMoodMediaManifest());
        var serialized = JsonSerializer.Serialize(trail, JsonOptions);

        Assert.True(trail.IsAccepted);
        Assert.Equal(2, trail.Items.Count);
        var selectedItem = Assert.Single(trail.Items, item => item.Kind == "selected_card");
        var deferredItem = Assert.Single(trail.Items, item => item.Kind == "deferred_card");
        Assert.Equal("candidate-plan-meal", selectedItem.CandidateId);
        Assert.NotNull(selectedItem.Media);
        Assert.Null(deferredItem.CandidateId);
        Assert.Null(deferredItem.Media);
        Assert.Equal(actionCount, session.Actions.Count);
        Assert.Equal(decisionCount, session.DecisionLedger.Records.Count);
        Assert.DoesNotContain("RAW_CANDIDATE_DISPLAY_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void AvailabilityApprovalRequiredProjectionUsesFixedCodeAndSanitizedEvidence()
    {
        var preview = new AvailabilityQuotePreviewResult(
            IsAccepted: false,
            IsBlocked: true,
            Code: AvailabilityQuotePreviewApplication.ApprovalRequiredCode,
            Summary: "RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK",
            Context: new AvailabilityQuotePreviewContext("fingerprint-live", "snapshot-live"),
            Preview: null,
            EvidenceReferences: ["evidence-preview", "RAW_HOLD_REFERENCE_SHOULD_NOT_LEAK"],
            Trace: [new("trace-preview", "Availability", "quote", "approval_required_preview", "Rejected.")]);

        var projected = new LiveTurnProjector().FromAvailabilityPreview(preview);
        var serialized = JsonSerializer.Serialize(projected, JsonOptions);

        Assert.True(projected.IsBlocked);
        Assert.Equal(LiveTurnProjector.ApprovalRequiredCode, projected.Code);
        Assert.Contains("evidence-preview", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_HOLD_REFERENCE_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
    }

    private static void AssertFixture(LiveTurnProjectionResult result, string fileName)
    {
        var fixturePath = Path.Combine(FindRepoRoot(), "tests", "fixtures", "live-turn-traces", fileName);
        var expected = File.ReadAllText(fixturePath).ReplaceLineEndings("\n").Trim();
        var actual = JsonSerializer.Serialize(result, JsonOptions).ReplaceLineEndings("\n").Trim();
        Assert.Equal(expected, actual);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "tests", "fixtures", "live-turn-traces")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate live-turn-traces fixtures.");
    }
}
