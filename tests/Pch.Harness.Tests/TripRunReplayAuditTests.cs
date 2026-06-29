using System.Text.Json;
using Pch.Core;
using Pch.Harness;
using Xunit;

namespace Pch.Harness.Tests;

public sealed class TripRunReplayAuditTests
{
    private static readonly DateTimeOffset FixedAt = new(2027, 4, 1, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ObservedAt = new(2026, 6, 29, 0, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void DefaultCorpusCoversRequiredScenariosAndSnapshotCodes()
    {
        var result = new TripRunReplayAudit().ReplayDefaultCorpus();

        Assert.True(result.IsAccepted);
        Assert.Equal(TripRunReplayAudit.AuditCompleteCode, result.Code);
        Assert.Equal(8, result.CaseCount);
        AssertCase(result, "vacation", TripRunReplayAudit.ReplayCompleteSnapshotCode, TripRunSnapshotBuilder.CompleteCode);
        AssertCase(result, "business", TripRunReplayAudit.ReplayCompleteSnapshotCode, TripRunSnapshotBuilder.CompleteCode);
        AssertCase(result, "funeral_downtime", TripRunReplayAudit.ReplayCompleteSnapshotCode, TripRunSnapshotBuilder.CompleteCode);
        AssertCase(result, "family_support", TripRunReplayAudit.ReplayCompleteSnapshotCode, TripRunSnapshotBuilder.CompleteCode);
        AssertCase(result, "blocked_compiler", TripRunReplayAudit.ReplayBlockedCompilerCode, TripRunSnapshotBuilder.BlockedCompilerCode);
        AssertCase(result, "blocked_candidate", TripRunReplayAudit.ReplayBlockedCandidateCode, TripRunSnapshotBuilder.BlockedCandidateCode);
        AssertCase(result, "pending_confirmation", TripRunReplayAudit.ReplayPendingConfirmationCode, TripRunSnapshotBuilder.PendingConfirmationCode);
        AssertCase(result, "missing_approval", TripRunReplayAudit.ReplayHoldPrepRequiredCode, TripRunSnapshotBuilder.HoldPrepRequiredCode);
        Assert.All(result.Cases, replayCase =>
        {
            Assert.True(replayCase.IsDeterministic);
            Assert.True(replayCase.IsReadOnly);
        });
    }

    [Fact]
    public void DefaultCorpusReplayIsDeterministic()
    {
        var audit = new TripRunReplayAudit();

        var first = audit.ReplayDefaultCorpus();
        var second = audit.ReplayDefaultCorpus();

        Assert.Equal(
            JsonSerializer.Serialize(first, JsonOptions),
            JsonSerializer.Serialize(second, JsonOptions));
        Assert.All(first.Cases, replayCase => Assert.Equal(64, replayCase.SnapshotHash.Length));
    }

    [Fact]
    public void ReplayCaseIsReadOnlyForProvidedSession()
    {
        var session = CompleteSession();
        var before = SnapshotCounts(session);

        var result = new TripRunReplayAudit().ReplayCase("case-read-only", "vacation", session);
        var after = SnapshotCounts(session);

        Assert.Equal(TripRunReplayAudit.ReplayCompleteSnapshotCode, result.Code);
        Assert.True(result.IsReadOnly);
        Assert.Equal(before, after);
    }

    [Fact]
    public void ReplayReferencesAreBounded()
    {
        var session = CompleteSession();
        AddExtraDecisions(session, 20);

        var result = new TripRunReplayAudit().ReplayCase("case-bounds", "vacation", session);

        Assert.Equal(TripRunReplayAudit.ReplayCompleteSnapshotCode, result.Code);
        Assert.True(result.EvidenceReferenceCount > result.EvidenceReferences.Count);
        Assert.True(result.TraceReferenceCount > result.TraceReferences.Count);
        Assert.True(result.EvidenceReferences.Count <= 8);
        Assert.True(result.TraceReferences.Count <= 8);
    }

    [Fact]
    public void ReplayResultOmitsRawPromptProviderApprovalHoldCredentialsAndCandidateText()
    {
        const string providerSentinel = "RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK";
        const string promptSentinel = "RAW_PROMPT_SHOULD_NOT_LEAK";
        const string approvalSentinel = "RAW_APPROVAL_TOKEN_SHOULD_NOT_LEAK";
        const string holdSentinel = "RAW_HOLD_REFERENCE_SHOULD_NOT_LEAK";
        const string credentialSentinel = "RAW_CREDENTIAL_SHOULD_NOT_LEAK";
        const string secretSentinel = "SECRET_API_KEY_SHOULD_NOT_LEAK";
        var session = CompleteSession(
            sessionId: $"session-{providerSentinel}",
            missionId: $"mission-{promptSentinel}",
            missionPurpose: promptSentinel,
            candidateTitle: providerSentinel,
            candidateSummary: credentialSentinel,
            candidateEvidence: "evidence-safe");
        session.RecordApproval(new ApprovalToken("approval-extra", approvalSentinel, FixedAt));
        session.ReplaceMemoryDigest(new StructuredMemoryDigest(
            "digest-safe",
            session.SessionId,
            session.Mission.MissionId,
            [promptSentinel, "purpose: [redacted]"],
            [
                new(
                    "/mission/raw_prompt",
                    promptSentinel,
                    AuthoritySource.SmallModelDraft,
                    "requires_confirmation",
                    [holdSentinel, "evidence-safe"])
            ],
            [providerSentinel, secretSentinel, "evidence-safe"]));

        var result = new TripRunReplayAudit().ReplayCase(
            $"case-{providerSentinel}",
            $"scenario-{promptSentinel}",
            session);
        var serialized = JsonSerializer.Serialize(result, JsonOptions);

        Assert.Equal("[redacted]", result.CaseId);
        Assert.Equal("[redacted]", result.Scenario);
        Assert.DoesNotContain(providerSentinel, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(promptSentinel, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(approvalSentinel, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(holdSentinel, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(credentialSentinel, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(secretSentinel, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("approved-token", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void NullCaseUsesFixedSanitizedFailureCode()
    {
        var result = new TripRunReplayAudit().ReplayCase(
            "case-RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK",
            "scenario-RAW_PROMPT_SHOULD_NOT_LEAK",
            null!);
        var serialized = JsonSerializer.Serialize(result, JsonOptions);

        Assert.Equal(TripRunReplayAudit.ReplayInvalidCaseCode, result.Code);
        Assert.Equal("Trip run replay case failed validation.", result.Summary);
        Assert.DoesNotContain("RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_PROMPT_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
    }

    private static void AssertCase(
        TripRunReplayAuditResult result,
        string scenario,
        string expectedReplayCode,
        string expectedSnapshotCode)
    {
        var replayCase = Assert.Single(result.Cases, item => item.Scenario == scenario);
        Assert.Equal(expectedReplayCode, replayCase.Code);
        Assert.Equal(expectedSnapshotCode, replayCase.SnapshotCode);
    }

    private static TripSession CompleteSession(
        string sessionId = "session-replay-test",
        string missionId = "mission-replay-test",
        string missionPurpose = "Replay vacation",
        string candidateTitle = "Replay activity",
        string candidateSummary = "Trusted replay candidate.",
        string candidateEvidence = "evidence-replay-candidate")
    {
        var mission = new TripMission(
            missionId,
            missionPurpose,
            "Japan",
            new DateOnly(2027, 4, 1),
            new DateOnly(2027, 4, 1),
            [new Traveler("traveler-1", "Primary traveler", "ARN", [])],
            [],
            []);
        var session = new TripSession(
            sessionId,
            mission,
            evidenceTrace: new EvidenceTrace(
            [
                new("evidence-replay-user", EvidenceKind.UserStatement, "Replay fixture user statement.", null, ObservedAt),
                new("evidence-replay-country", EvidenceKind.CountryPackAssumption, "Replay fixture country assumption.", null, ObservedAt)
            ]));
        var compilation = new ItinerarySlotCompiler().Compile(session, new ItineraryCompilationRequest(
            session.SessionId,
            null,
            null,
            null,
            []));
        Assert.True(compilation.IsCompiled);
        var slot = compilation.Days
            .SelectMany(day => day.Slots)
            .First(slot => slot.Kind == ItinerarySlotKind.Activity);
        session.AddItineraryCandidatePool(slot.SlotId, new CandidatePool(
            "pool-replay-activity",
            "all",
            [
                new(
                    "candidate-replay-activity",
                    CandidateKind.Activity,
                    candidateTitle,
                    candidateSummary,
                    null,
                    null,
                    [candidateEvidence],
                    100)
            ],
            [],
            ObservedAt));

        var selected = new ItineraryCandidateApplication().Apply(session, new ItinerarySlotDecisionRequest(
            session.SessionId,
            slot.SlotId,
            ItinerarySlotDecisionKind.Selected,
            slot.Kind,
            "candidate-replay-activity",
            CandidateKind.Activity,
            FixedAt));
        Assert.True(selected.IsAccepted);
        session.RecordApproval(new ApprovalToken("approval-replay-test", "approved-token", FixedAt));
        return session;
    }

    private static (int Decisions, int Actions, int Approvals, int ItineraryDecisions) SnapshotCounts(TripSession session)
    {
        return (
            session.DecisionLedger.Records.Count,
            session.Actions.Count,
            session.ApprovalTokens.Count,
            session.ItineraryDecisions.Count);
    }

    private static void AddExtraDecisions(TripSession session, int count)
    {
        for (var index = 0; index < count; index++)
        {
            session.RecordItineraryDecision(new ItinerarySlotDecision(
                $"extra-decision-{index}",
                $"extra-slot-{index}",
                ItinerarySlotDecisionKind.Deferred,
                ItinerarySlotKind.Meal,
                null,
                null,
                [$"evidence-extra-{index}"]));
            session.RecordDecision(new DecisionRecord(
                $"extra-decision-record-{index}",
                HarnessStage.DaySkeletonGeneration.ToString(),
                "itinerary_defer",
                "Accepted itinerary slot defer decision.",
                AuthoritySource.User,
                FixedAt));
        }
    }
}
