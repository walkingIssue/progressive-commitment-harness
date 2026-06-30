using System.Text.Json;
using Pch.Core;
using Pch.Harness;
using Xunit;

namespace Pch.Harness.Tests;

public sealed class PlanningEditImpactAnalyzerTests
{
    private static readonly DateTimeOffset FixedAt = new(2027, 4, 1, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ObservedAt = new(2026, 6, 29, 0, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void SelectedCandidateEditReportsAffectedPreviewAndHoldReadiness()
    {
        var fixture = CompleteFixture();
        var analyzer = new PlanningEditImpactAnalyzer();
        var snapshot = analyzer.BuildSnapshot(fixture.Session, [fixture.Preview]);
        var decisionNode = snapshot.Nodes.Single(node => node.Kind == "selected_decision");

        var result = analyzer.Analyze(
            fixture.Session,
            Request(fixture.Session, snapshot, decisionNode.NodeId, PlanningEditKind.SelectedCandidate),
            [fixture.Preview]);

        Assert.True(result.IsAccepted);
        Assert.False(result.IsBlocked);
        Assert.Equal(PlanningEditImpactAnalyzer.RepairRequiredCode, result.Code);
        Assert.Contains(result.AffectedNodes, node => node.Kind == "selected_decision");
        Assert.Contains(result.AffectedNodes, node => node.Kind == "availability_preview");
        Assert.Contains(result.AffectedNodes, node => node.Kind == "mock_hold_readiness");
        Assert.Contains(result.PreservedNodes, node => node.Kind == "mission_fact");
        Assert.True(result.RequiresUserConfirmation);
        Assert.True(result.RequiresModelRepair);
        Assert.Contains(result.MinimalRepairPrompts, prompt => prompt.Code == "repair_selected_candidate");
    }

    [Fact]
    public void SlotEditReportsDownstreamDecisionPreviewAndRepairPrompt()
    {
        var fixture = CompleteFixture();
        var analyzer = new PlanningEditImpactAnalyzer();
        var snapshot = analyzer.BuildSnapshot(fixture.Session, [fixture.Preview]);
        var slotNode = snapshot.Nodes.Single(node => node.NodeId == $"slot:{fixture.Slot.SlotId}");

        var result = analyzer.Analyze(
            fixture.Session,
            Request(fixture.Session, snapshot, slotNode.NodeId, PlanningEditKind.Slot),
            [fixture.Preview]);

        Assert.Equal(PlanningEditImpactAnalyzer.RepairRequiredCode, result.Code);
        Assert.Contains(result.AffectedNodes, node => node.Kind == "slot");
        Assert.Contains(result.AffectedNodes, node => node.Kind == "selected_decision");
        Assert.Contains(result.MinimalRepairPrompts, prompt => prompt.Code == "repair_slot_plan");
        Assert.True(result.RequiresModelRepair);
    }

    [Fact]
    public void DayEditReportsAffectedSlotsAndPreservedMissionFacts()
    {
        var fixture = CompleteFixture();
        var analyzer = new PlanningEditImpactAnalyzer();
        var snapshot = analyzer.BuildSnapshot(fixture.Session, [fixture.Preview]);
        var dayNode = snapshot.Nodes.First(node => node.Kind == "day");

        var result = analyzer.Analyze(
            fixture.Session,
            Request(fixture.Session, snapshot, dayNode.NodeId, PlanningEditKind.Day),
            [fixture.Preview]);

        Assert.Equal(PlanningEditImpactAnalyzer.RepairRequiredCode, result.Code);
        Assert.Contains(result.AffectedNodes, node => node.Kind == "day");
        Assert.Contains(result.AffectedNodes, node => node.Kind == "slot");
        Assert.Contains(result.PreservedNodes, node => node.Kind == "mission_fact");
        Assert.Contains(result.MinimalRepairPrompts, prompt => prompt.Code == "repair_day_plan");
    }

    [Fact]
    public void MissionPurposeEditCanReturnNoImpact()
    {
        var fixture = CompleteFixture();
        var analyzer = new PlanningEditImpactAnalyzer();
        var snapshot = analyzer.BuildSnapshot(fixture.Session, [fixture.Preview]);

        var result = analyzer.Analyze(
            fixture.Session,
            Request(fixture.Session, snapshot, "mission:purpose", PlanningEditKind.MissionFact),
            [fixture.Preview]);

        Assert.True(result.IsAccepted);
        Assert.Equal(PlanningEditImpactAnalyzer.NoImpactCode, result.Code);
        Assert.Single(result.AffectedNodes);
        Assert.Empty(result.MinimalRepairPrompts);
        Assert.False(result.RequiresUserConfirmation);
        Assert.False(result.RequiresModelRepair);
    }

    [Fact]
    public void StaleFingerprintBlocksWithSanitizedStaleContext()
    {
        var fixture = CompleteFixture();
        var analyzer = new PlanningEditImpactAnalyzer();
        var snapshot = analyzer.BuildSnapshot(fixture.Session, [fixture.Preview]);

        var result = analyzer.Analyze(
            fixture.Session,
            new EditImpactRequest(
                fixture.Session.SessionId,
                "stale-fingerprint",
                "mission:date_window",
                PlanningEditKind.MissionFact,
                "change-date"),
            [fixture.Preview]);

        Assert.False(result.IsAccepted);
        Assert.True(result.IsBlocked);
        Assert.Equal(PlanningEditImpactAnalyzer.StaleSnapshotCode, result.Code);
        Assert.NotEqual("stale-fingerprint", result.Fingerprint);
        Assert.NotEmpty(result.StaleContext);
        Assert.Empty(result.AffectedNodes);
        Assert.Contains(snapshot.Nodes.First().NodeId, result.StaleContext.Select(node => node.NodeId));
    }

    [Fact]
    public void UnknownNodeAndUnsupportedEditUseFixedCodes()
    {
        var fixture = CompleteFixture();
        var analyzer = new PlanningEditImpactAnalyzer();
        var snapshot = analyzer.BuildSnapshot(fixture.Session, [fixture.Preview]);

        var unknown = analyzer.Analyze(
            fixture.Session,
            Request(fixture.Session, snapshot, "slot-missing", PlanningEditKind.Slot),
            [fixture.Preview]);
        var unsupported = analyzer.Analyze(
            fixture.Session,
            Request(fixture.Session, snapshot, "mission:date_window", PlanningEditKind.Unsupported),
            [fixture.Preview]);

        Assert.Equal(PlanningEditImpactAnalyzer.UnknownNodeCode, unknown.Code);
        Assert.True(unknown.IsBlocked);
        Assert.Equal(PlanningEditImpactAnalyzer.UnsupportedEditCode, unsupported.Code);
        Assert.True(unsupported.IsBlocked);
    }

    [Fact]
    public void AnalyzeDoesNotMutateSession()
    {
        var fixture = CompleteFixture();
        var before = Counts(fixture.Session);
        var analyzer = new PlanningEditImpactAnalyzer();
        var snapshot = analyzer.BuildSnapshot(fixture.Session, [fixture.Preview]);

        _ = analyzer.Analyze(
            fixture.Session,
            Request(fixture.Session, snapshot, "mission:date_window", PlanningEditKind.MissionFact),
            [fixture.Preview]);

        Assert.Equal(before, Counts(fixture.Session));
    }

    [Fact]
    public void SerializedImpactOmitsRawPromptProviderApprovalSecretHoldAndCandidateDisplaySentinels()
    {
        var fixture = CompleteFixture(
            candidateId: "candidate-RAW_CANDIDATE_DISPLAY_SHOULD_NOT_LEAK",
            evidenceId: "RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK");
        fixture.Session.RecordApproval(new ApprovalToken("approval-edit", "RAW_APPROVAL_TOKEN_SHOULD_NOT_LEAK", FixedAt));
        var analyzer = new PlanningEditImpactAnalyzer();
        var snapshot = analyzer.BuildSnapshot(fixture.Session, [fixture.Preview]);
        var node = snapshot.Nodes.First(item => item.Kind == "selected_decision");

        var result = analyzer.Analyze(
            fixture.Session,
            new EditImpactRequest(
                fixture.Session.SessionId,
                snapshot.Fingerprint,
                node.NodeId,
                PlanningEditKind.SelectedCandidate,
                "RAW_PROMPT_SHOULD_NOT_LEAK"),
            [fixture.Preview]);
        var serialized = JsonSerializer.Serialize(result, JsonOptions);

        Assert.DoesNotContain("RAW_PROMPT_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_APPROVAL_TOKEN_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_SECRET_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_HOLD_REFERENCE_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_CANDIDATE_DISPLAY_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
    }

    private static EditImpactRequest Request(
        TripSession session,
        PlanningDependencySnapshot snapshot,
        string nodeId,
        PlanningEditKind kind)
    {
        return new(session.SessionId, snapshot.Fingerprint, nodeId, kind, "change_requested");
    }

    private static (int Actions, int Decisions, int ItineraryDecisions, int Approvals, int Deferred) Counts(TripSession session)
    {
        return (
            session.Actions.Count,
            session.DecisionLedger.Records.Count,
            session.ItineraryDecisions.Count,
            session.ApprovalTokens.Count,
            session.DeferredSlots.Count);
    }

    private static PlanningFixture CompleteFixture(
        string candidateId = "candidate-edit-activity",
        string evidenceId = "evidence-edit-candidate")
    {
        var session = SyntheticTripFactory.CreateSession(1);
        var compilation = new ItinerarySlotCompiler().Compile(session, new ItineraryCompilationRequest(
            session.SessionId,
            null,
            null,
            session.MemoryDigest,
            []));
        var slot = compilation.Days
            .SelectMany(day => day.Slots)
            .First(slot => slot.Kind == ItinerarySlotKind.Activity);
        session.AddItineraryCandidatePool(slot.SlotId, new CandidatePool(
            "pool-edit-activity",
            "activity",
            [
                new(
                    candidateId,
                    CandidateKind.Activity,
                    "RAW_CANDIDATE_DISPLAY_SHOULD_NOT_LEAK",
                    "RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK",
                    null,
                    null,
                    [evidenceId],
                    90)
            ],
            [],
            ObservedAt));

        var decision = new ItineraryCandidateApplication().Apply(session, new ItinerarySlotDecisionRequest(
            session.SessionId,
            slot.SlotId,
            ItinerarySlotDecisionKind.Selected,
            slot.Kind,
            candidateId,
            CandidateKind.Activity,
            FixedAt));
        Assert.True(decision.IsAccepted);

        var availability = new AvailabilityQuotePreviewApplication();
        var context = availability.CurrentContext(session);
        var preview = availability.Preview(session, new AvailabilityQuotePreviewRequest(
            session.SessionId,
            slot.SlotId,
            candidateId,
            slot.Kind,
            CandidateKind.Activity,
            AvailabilityQuoteKind.Availability,
            context.CompilationFingerprint,
            context.SnapshotId,
            FixedAt));
        Assert.True(preview.IsAccepted);

        return new(session, slot, preview);
    }

    private sealed record PlanningFixture(
        TripSession Session,
        ItinerarySlot Slot,
        AvailabilityQuotePreviewResult Preview);
}
