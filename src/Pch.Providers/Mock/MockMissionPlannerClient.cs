using Pch.Providers.MissionPlanning;

namespace Pch.Providers.Mock;

public sealed class MockMissionPlannerClient : IMissionPlannerClient
{
    public const string ProviderName = "mock-mission-planner";

    public Task<MissionPlannerResult> PlanAsync(
        MissionPlannerPacket packet,
        MissionPlannerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);
        cancellationToken.ThrowIfCancellationRequested();

        var result = packet.Scenario.Trim().ToLowerInvariant() switch
        {
            "vacation" => Vacation(packet, options),
            "business" => Business(packet, options),
            "funeral_downtime" => FuneralDowntime(packet, options),
            "helping_family" => HelpingFamily(packet, options),
            _ => Generic(packet, options)
        };

        return Task.FromResult(result);
    }

    private static MissionPlannerResult Vacation(MissionPlannerPacket packet, MissionPlannerOptions? options) =>
        Create(
            packet,
            options,
            "vacation",
            [
                UserField("/mission/purpose", "vacation", "evidence-user-purpose"),
                UserField("/mission/destination_country", "Japan", "evidence-user-destination"),
                InferredField("/mission/pace", "balanced", requiresConfirmation: true, "evidence-model-pace")
            ],
            [
                Commitment(
                    "commitment-rest",
                    "downtime",
                    "Protect downtime between sightseeing days.",
                    MissionCommitmentPriority.Normal,
                    MissionProposalSource.ModelInferred,
                    requiresSpend: false,
                    evidenceId: "evidence-model-pace")
            ],
            [Constraint("constraint-flex-day", "Flexible day", "Keep one flexible day.", MissionProposalSource.ModelInferred, isHard: false, "evidence-model-pace")],
            ["Confirm travel dates.", "Confirm budget range."],
            "Vacation mission: Japan, balanced pace, dates and budget pending.");

    private static MissionPlannerResult Business(MissionPlannerPacket packet, MissionPlannerOptions? options) =>
        Create(
            packet,
            options,
            "business",
            [
                UserField("/mission/purpose", "business", "evidence-user-purpose"),
                UserField("/mission/destination_city", "Berlin", "evidence-user-destination"),
                InferredField("/mission/meeting_window", "weekday mornings", requiresConfirmation: true, "evidence-model-window")
            ],
            [
                Commitment(
                    "commitment-meeting",
                    "client_meeting",
                    "Prioritize client meeting arrival reliability.",
                    MissionCommitmentPriority.High,
                    MissionProposalSource.UserStated,
                    requiresSpend: true,
                    evidenceId: "evidence-user-purpose")
            ],
            [Constraint("constraint-work-calls", "Work-call support", "Hotel must support work calls.", MissionProposalSource.UserStated, isHard: true, "evidence-user-purpose")],
            ["Confirm meeting address."],
            "Business mission: Berlin client meeting, reliability and work-call support.");

    private static MissionPlannerResult FuneralDowntime(MissionPlannerPacket packet, MissionPlannerOptions? options) =>
        Create(
            packet,
            options,
            "funeral_downtime",
            [
                UserField("/mission/purpose", "funeral logistics and downtime", "evidence-user-purpose"),
                UserField("/mission/destination_region", "Midwest", "evidence-user-destination"),
                InferredField("/mission/emotional_load", "high", requiresConfirmation: true, "evidence-model-care")
            ],
            [
                Commitment(
                    "commitment-service",
                    "memorial_service",
                    "Arrive before the memorial service.",
                    MissionCommitmentPriority.Critical,
                    MissionProposalSource.UserStated,
                    requiresSpend: true,
                    evidenceId: "evidence-user-purpose",
                    isIrreversible: true)
            ],
            [Constraint("constraint-low-pressure", "Low-pressure itinerary", "Avoid tightly packed itinerary.", MissionProposalSource.ModelInferred, isHard: true, "evidence-model-care")],
            ["Confirm service time.", "Confirm family lodging preference."],
            "Funeral/downtime mission: critical service arrival, low itinerary pressure.");

    private static MissionPlannerResult HelpingFamily(MissionPlannerPacket packet, MissionPlannerOptions? options) =>
        Create(
            packet,
            options,
            "helping_family",
            [
                UserField("/mission/purpose", "helping family", "evidence-user-purpose"),
                UserField("/mission/support_role", "care and errands", "evidence-user-support"),
                InferredField("/mission/schedule_flexibility", "high", requiresConfirmation: true, "evidence-model-flexibility")
            ],
            [
                Commitment(
                    "commitment-care",
                    "family_support",
                    "Keep daytime availability for family support.",
                    MissionCommitmentPriority.High,
                    MissionProposalSource.UserStated,
                    requiresSpend: false,
                    evidenceId: "evidence-user-support")
            ],
            [Constraint("constraint-refundable", "Refundable bookings", "Prefer refundable bookings.", MissionProposalSource.ModelInferred, isHard: false, "evidence-model-flexibility")],
            ["Confirm family address.", "Confirm caregiver schedule."],
            "Helping-family mission: flexible support travel with refundable logistics.");

    private static MissionPlannerResult Generic(MissionPlannerPacket packet, MissionPlannerOptions? options) =>
        Create(
            packet,
            options,
            "general",
            [UserField("/mission/purpose", "general travel", "evidence-user-purpose")],
            [],
            packet.KnownConstraints
                .Select((constraint, index) => Constraint(
                    $"constraint-known-{index + 1}",
                    $"Known constraint {index + 1}",
                    constraint,
                    MissionProposalSource.UserStated,
                    isHard: false,
                    $"evidence-known-{index + 1}"))
                .ToArray(),
            ["Confirm primary purpose."],
            "General mission: primary purpose pending confirmation.");

    private static MissionPlannerResult Create(
        MissionPlannerPacket packet,
        MissionPlannerOptions? options,
        string missionKind,
        IReadOnlyList<MissionFieldProposal> fields,
        IReadOnlyList<MissionCommitmentProposal> commitments,
        IReadOnlyList<MissionConstraintProposal> constraints,
        IReadOnlyList<string> pendingConfirmations,
        string memoryDigest)
    {
        return new MissionPlannerResult(
            packet.PacketId,
            missionKind,
            fields,
            commitments,
            constraints,
            pendingConfirmations,
            memoryDigest,
            ResponseContentLength: 0,
            ProviderName,
            options?.Model ?? "mock-mission-planner-deterministic",
            $"mock-mission-{packet.PacketId}");
    }

    private static MissionFieldProposal UserField(string fieldPath, string value, string evidenceId) =>
        new(fieldPath, value, MissionProposalSource.UserStated, [evidenceId], RequiresConfirmation: false);

    private static MissionFieldProposal InferredField(
        string fieldPath,
        string value,
        bool requiresConfirmation,
        string evidenceId) =>
        new(fieldPath, value, MissionProposalSource.ModelInferred, [evidenceId], requiresConfirmation);

    private static MissionConstraintProposal Constraint(
        string constraintId,
        string label,
        string value,
        MissionProposalSource source,
        bool isHard,
        string evidenceId) =>
        new(constraintId, label, value, source, isHard, [evidenceId]);

    private static MissionCommitmentProposal Commitment(
        string commitmentId,
        string commitmentKind,
        string title,
        MissionCommitmentPriority priority,
        MissionProposalSource source,
        bool requiresSpend,
        string evidenceId,
        bool isIrreversible = false) =>
        new(
            commitmentId,
            commitmentKind,
            title,
            null,
            null,
            null,
            isIrreversible,
            requiresSpend,
            priority,
            source,
            [evidenceId]);
}
