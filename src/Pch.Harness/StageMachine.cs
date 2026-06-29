using Pch.Core;

namespace Pch.Harness;

public enum HarnessStage
{
    Intake,
    SlotCollection,
    Posture,
    DaySkeletonGeneration,
    Logistics,
    Meals,
    ActivitiesDowntime,
    ConflictVerify,
    ApprovalQueue,
    MockedBooking,
    EvidencePacket
}

public sealed class StageMachine
{
    private static readonly IReadOnlyDictionary<HarnessStage, HarnessStage> NextStages =
        new Dictionary<HarnessStage, HarnessStage>
        {
            [HarnessStage.Intake] = HarnessStage.SlotCollection,
            [HarnessStage.SlotCollection] = HarnessStage.Posture,
            [HarnessStage.Posture] = HarnessStage.DaySkeletonGeneration,
            [HarnessStage.DaySkeletonGeneration] = HarnessStage.Logistics,
            [HarnessStage.Logistics] = HarnessStage.Meals,
            [HarnessStage.Meals] = HarnessStage.ActivitiesDowntime,
            [HarnessStage.ActivitiesDowntime] = HarnessStage.ConflictVerify,
            [HarnessStage.ConflictVerify] = HarnessStage.ApprovalQueue,
            [HarnessStage.ApprovalQueue] = HarnessStage.MockedBooking,
            [HarnessStage.MockedBooking] = HarnessStage.EvidencePacket
        };

    public HarnessStage Advance(TripSession session, HarnessAction? completedAction = null)
    {
        if (completedAction is not null)
        {
            session.RecordAction(completedAction);
        }

        if (NextStages.TryGetValue(session.Stage, out var next))
        {
            session.MoveTo(next);
        }

        return session.Stage;
    }

    public HarnessAction NextSkeletonAction(TripSession session)
    {
        return session.Stage switch
        {
            HarnessStage.Intake or HarnessStage.SlotCollection => new EmitFormAction(
                $"action-{session.Stage.ToString().ToLowerInvariant()}-form",
                new FormRequest(
                    session.Stage == HarnessStage.Intake ? "mission-intake" : "slot-collection",
                    session.Stage == HarnessStage.Intake ? "Mission intake" : "Slot collection",
                    "Continue",
                    [
                        new("destination_country", "Destination country", "text", true, session.Mission.DestinationCountry, []),
                        new("purpose", "Purpose", "textarea", true, session.Mission.Purpose, [])
                    ])),
            HarnessStage.ApprovalQueue => new RequestApprovalAction(
                "action-approval-review",
                new ApprovalRequest(
                    "approval-review",
                    "mock-booking",
                    "Approve mocked booking or spend action.",
                    ["booking", "spend"],
                    null,
                    null,
                    null)),
            HarnessStage.EvidencePacket => new SummarizeAction(
                "action-evidence-summary",
                "traveler",
                session.ClaimLedger.Claims.Where(claim => claim.IsUserVisible).Select(claim => claim.ClaimId).ToArray()),
            _ => new EmitChoiceSetAction(
                $"action-{session.Stage.ToString().ToLowerInvariant()}-choices",
                $"{session.Stage} choices",
                session.CandidatePools.SelectMany(pool => pool.Candidates).Take(5).Select(ProjectionService.ToSummary).ToArray(),
                1)
        };
    }
}
