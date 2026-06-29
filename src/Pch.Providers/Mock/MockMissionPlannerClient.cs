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
                UserField("purpose", "vacation"),
                UserField("destination_country", "Japan"),
                InferredField("pace", "balanced", requiresConfirmation: true)
            ],
            [Commitment("commitment-rest", "Protect downtime between sightseeing days.", MissionCommitmentPriority.Normal, MissionProposalSource.ModelInferred)],
            ["Keep one flexible day."],
            ["Confirm travel dates.", "Confirm budget range."],
            "Vacation mission: Japan, balanced pace, dates and budget pending.");

    private static MissionPlannerResult Business(MissionPlannerPacket packet, MissionPlannerOptions? options) =>
        Create(
            packet,
            options,
            "business",
            [
                UserField("purpose", "business"),
                UserField("destination_city", "Berlin"),
                InferredField("meeting_window", "weekday mornings", requiresConfirmation: true)
            ],
            [Commitment("commitment-meeting", "Prioritize client meeting arrival reliability.", MissionCommitmentPriority.High, MissionProposalSource.UserStated)],
            ["Hotel must support work calls."],
            ["Confirm meeting address."],
            "Business mission: Berlin client meeting, reliability and work-call support.");

    private static MissionPlannerResult FuneralDowntime(MissionPlannerPacket packet, MissionPlannerOptions? options) =>
        Create(
            packet,
            options,
            "funeral_downtime",
            [
                UserField("purpose", "funeral logistics and downtime"),
                UserField("destination_region", "Midwest"),
                InferredField("emotional_load", "high", requiresConfirmation: true)
            ],
            [Commitment("commitment-service", "Arrive before the memorial service.", MissionCommitmentPriority.Critical, MissionProposalSource.UserStated)],
            ["Avoid tightly packed itinerary."],
            ["Confirm service time.", "Confirm family lodging preference."],
            "Funeral/downtime mission: critical service arrival, low itinerary pressure.");

    private static MissionPlannerResult HelpingFamily(MissionPlannerPacket packet, MissionPlannerOptions? options) =>
        Create(
            packet,
            options,
            "helping_family",
            [
                UserField("purpose", "helping family"),
                UserField("support_role", "care and errands"),
                InferredField("schedule_flexibility", "high", requiresConfirmation: true)
            ],
            [Commitment("commitment-care", "Keep daytime availability for family support.", MissionCommitmentPriority.High, MissionProposalSource.UserStated)],
            ["Prefer refundable bookings."],
            ["Confirm family address.", "Confirm caregiver schedule."],
            "Helping-family mission: flexible support travel with refundable logistics.");

    private static MissionPlannerResult Generic(MissionPlannerPacket packet, MissionPlannerOptions? options) =>
        Create(
            packet,
            options,
            "general",
            [UserField("purpose", "general travel")],
            [],
            packet.KnownConstraints,
            ["Confirm primary purpose."],
            "General mission: primary purpose pending confirmation.");

    private static MissionPlannerResult Create(
        MissionPlannerPacket packet,
        MissionPlannerOptions? options,
        string missionKind,
        IReadOnlyList<MissionFieldProposal> fields,
        IReadOnlyList<MissionCommitmentProposal> commitments,
        IReadOnlyList<string> constraints,
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

    private static MissionFieldProposal UserField(string name, string value) =>
        new(name, value, MissionProposalSource.UserStated, RequiresConfirmation: false);

    private static MissionFieldProposal InferredField(string name, string value, bool requiresConfirmation) =>
        new(name, value, MissionProposalSource.ModelInferred, requiresConfirmation);

    private static MissionCommitmentProposal Commitment(
        string commitmentId,
        string description,
        MissionCommitmentPriority priority,
        MissionProposalSource source) =>
        new(commitmentId, description, priority, source);
}
