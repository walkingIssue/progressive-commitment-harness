using Pch.Core;
using Pch.Harness;
using Pch.Providers.MissionPlanning;
using HarnessCommitment = Pch.Harness.CommitmentProposal;
using HarnessConstraint = Pch.Harness.ConstraintProposal;
using HarnessField = Pch.Harness.MissionFieldProposal;
using ProviderCommitment = Pch.Providers.MissionPlanning.MissionCommitmentProposal;
using ProviderConstraint = Pch.Providers.MissionPlanning.MissionConstraintProposal;
using ProviderField = Pch.Providers.MissionPlanning.MissionFieldProposal;

namespace Pch.UI.Features.StageCockpit;

public sealed class RuntimeMissionPlannerService
{
    private const string RawPromptSentinel = "RAW_RAMBLING_PROMPT_SHOULD_NOT_LEAK";
    private const string RawProviderSentinel = "RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK";
    private const string RawPacketSentinel = "RAW_PACKET_ID_SHOULD_NOT_LEAK";
    private readonly MissionIntakeApplication _missionIntakeApplication = new();

    public RuntimeMissionPlannerResult Run(TripSession session, string runId)
    {
        var packet = CreateMissionPlannerPacket(runId);
        if (packet is null)
        {
            return Blocked(
                runId,
                "provider_runtime_unknown_scenario",
                "not_run",
                "planner_unknown_scenario",
                "PCH_UI_MISSION_UNKNOWN_SCENARIO",
                "Mission intake scenario is not recognized.",
                null);
        }

        var planner = CreateMissionPlannerResult(packet, runId);
        var adapter = AdaptMissionPlannerResult(packet, planner);
        if (adapter.IsBlocked)
        {
            return Blocked(
                runId,
                "provider_runtime_accepted",
                adapter.Code,
                "planner_mock_accepted",
                adapter.ErrorCode ?? "PCH_UI_MISSION_ADAPTER_BLOCKED",
                adapter.BlockedReason ?? "Mission planner output was blocked by adapter validation.",
                planner);
        }

        var intake = _missionIntakeApplication.Apply(session, adapter.Proposal!);
        var state = intake.PendingConfirmations.Count > 0 ? "proposed" : "applied";

        return new(
            runId,
            state,
            "provider_runtime_accepted",
            adapter.Code,
            "planner_mock_accepted",
            intake.Code,
            "memory_digest_updated",
            state == "proposed" ? "mission_intake.proposed" : "mission_intake.applied",
            null,
            null,
            planner.Provider,
            planner.Model,
            planner.RequestId,
            ToAppliedFields(intake.AppliedFacts),
            ToPendingFields(intake.PendingConfirmations),
            adapter.HighPriorityCommitments,
            ToDigestFacts(intake.Digest));
    }

    private static RuntimeMissionPlannerResult Blocked(
        string runId,
        string providerRuntimeCode,
        string adapterCode,
        string plannerCode,
        string errorCode,
        string blockedReason,
        MissionPlannerResult? planner)
    {
        return new(
            runId,
            "blocked",
            providerRuntimeCode,
            adapterCode,
            plannerCode,
            "not_run",
            "not_run",
            "mission_intake.blocked",
            errorCode,
            blockedReason,
            planner?.Provider ?? "deterministic-mock",
            planner?.Model ?? "mock-mission-planner",
            planner?.RequestId,
            [],
            [],
            [],
            []);
    }

    private static MissionPlannerPacket? CreateMissionPlannerPacket(string runId)
    {
        var scenario = runId switch
        {
            "mission.vacation" => "vacation",
            "mission.non-vacation-commitment" => "helping_family",
            "mission.pending-confirmation" => "vacation",
            "mission.validation-blocked" => "validation_blocked",
            _ => null
        };

        return scenario is null
            ? null
            : new(
                $"packet-{runId}",
                scenario,
                $"{RawPromptSentinel} I know this is a mess and need help planning.",
                "en-US",
                ["keep diagnostics sanitized"]);
    }

    private static MissionPlannerResult CreateMissionPlannerResult(MissionPlannerPacket packet, string runId)
    {
        IReadOnlyList<ProviderField> fields = runId switch
        {
            "mission.vacation" =>
            [
                new ProviderField("/mission/purpose", "vacation", MissionProposalSource.UserStated, ["evidence-user-purpose"], false),
                new ProviderField("/mission/destination_country", "Japan", MissionProposalSource.UserStated, ["evidence-user-destination"], false),
                new ProviderField("/mission/start_date", "2026-10-05", MissionProposalSource.UserStated, ["evidence-user-dates"], false),
                new ProviderField("/mission/end_date", "2026-10-19", MissionProposalSource.UserStated, ["evidence-user-dates"], false)
            ],
            "mission.non-vacation-commitment" =>
            [
                new ProviderField("/mission/purpose", "helping family", MissionProposalSource.UserStated, ["evidence-user-purpose"], false),
                new ProviderField("/mission/destination_country", "Poland", MissionProposalSource.UserStated, ["evidence-user-destination"], false)
            ],
            "mission.pending-confirmation" =>
            [
                new ProviderField("/mission/destination_country", "Japan", MissionProposalSource.UserStated, ["evidence-user-destination"], false),
                new ProviderField("/mission/pace", "balanced", MissionProposalSource.ModelInferred, ["evidence-model-pace"], true),
                new ProviderField("/mission/traveler_need", "low cognitive load", MissionProposalSource.ModelInferred, ["evidence-model-need"], true)
            ],
            "mission.validation-blocked" =>
            [
                new ProviderField("/mission/purpose", "vacation", MissionProposalSource.UserStated, ["evidence-user-purpose"], false)
            ],
            _ => []
        };

        IReadOnlyList<ProviderCommitment> commitments = runId == "mission.non-vacation-commitment"
            ?
            [
                new ProviderCommitment(
                    "commitment.family-anchor",
                    "family_support",
                    "Attend family support appointment",
                    StartsAt: null,
                    EndsAt: null,
                    Location: null,
                    IsIrreversible: false,
                    RequiresSpend: false,
                    MissionCommitmentPriority.High,
                    MissionProposalSource.UserStated,
                    ["evidence-user-commitment"])
            ]
            : [];

        IReadOnlyList<ProviderConstraint> constraints = runId == "mission.pending-confirmation"
            ?
            [
                new ProviderConstraint(
                    "constraint-low-cognitive-load",
                    "Cognitive load",
                    "Keep itinerary easy to process.",
                    MissionProposalSource.ModelInferred,
                    IsHard: true,
                    ["evidence-model-need"])
            ]
            : [];

        var packetId = runId == "mission.validation-blocked" ? RawPacketSentinel : packet.PacketId;
        return new(
            packetId,
            packet.Scenario,
            fields,
            commitments,
            constraints,
            runId == "mission.pending-confirmation" ? ["Confirm inferred pace.", "Confirm traveler needs."] : [],
            RawProviderSentinel,
            ResponseContentLength: 0,
            "deterministic-mock",
            "mock-mission-planner",
            $"mock-{runId}");
    }

    private static MissionAdapterResult AdaptMissionPlannerResult(MissionPlannerPacket packet, MissionPlannerResult planner)
    {
        if (!string.Equals(packet.PacketId, planner.PacketId, StringComparison.Ordinal))
        {
            return MissionAdapterResult.Blocked(
                "adapter_packet_id_mismatch",
                "PCH_UI_MISSION_ADAPTER_PACKET_ID_MISMATCH",
                "Mission planner result did not match the runtime packet.");
        }

        var proposal = new MissionIntakeProposal(
            $"proposal-{planner.PacketId}",
            planner.Fields.Select(ToHarnessField).ToArray(),
            planner.Constraints.Select(ToHarnessConstraint).ToArray(),
            planner.Commitments.Select(ToHarnessCommitment).ToArray());

        var highPriorityCommitments = planner.Commitments
            .Where(commitment => commitment.CommitmentPriority is MissionCommitmentPriority.High or MissionCommitmentPriority.Critical)
            .Select(commitment => new MissionCommitmentFixture(
                commitment.CommitmentId,
                commitment.Title,
                MapCommitmentKind(commitment.CommitmentKind).ToString(),
                "high",
                SourceLabel(commitment.AuthoritySource)))
            .ToArray();

        return MissionAdapterResult.Accepted(proposal, highPriorityCommitments);
    }

    private static HarnessField ToHarnessField(ProviderField field) =>
        new(field.FieldPath, field.Value, MapSource(field.AuthoritySource), field.EvidenceIds);

    private static HarnessConstraint ToHarnessConstraint(ProviderConstraint constraint) =>
        new(
            constraint.ConstraintId,
            constraint.Label,
            constraint.Value,
            MapSource(constraint.AuthoritySource),
            constraint.IsHard,
            constraint.EvidenceIds);

    private static HarnessCommitment ToHarnessCommitment(ProviderCommitment commitment) =>
        new(
            commitment.CommitmentId,
            MapCommitmentKind(commitment.CommitmentKind),
            commitment.Title,
            commitment.StartsAt,
            commitment.EndsAt,
            commitment.Location,
            commitment.IsIrreversible,
            commitment.RequiresSpend,
            commitment.CommitmentPriority is MissionCommitmentPriority.High or MissionCommitmentPriority.Critical
                ? CommitmentPriority.High
                : CommitmentPriority.Normal,
            MapSource(commitment.AuthoritySource),
            commitment.EvidenceIds);

    private static AuthoritySource MapSource(MissionProposalSource source) =>
        source is MissionProposalSource.UserStated
            ? AuthoritySource.User
            : AuthoritySource.StrongModelInference;

    private static CommitmentKind MapCommitmentKind(string kind)
    {
        return kind.Trim().ToLowerInvariant() switch
        {
            "fixed_anchor" or "client_meeting" or "memorial_service" or "family_support" => CommitmentKind.FixedAnchor,
            "travel" => CommitmentKind.Travel,
            "lodging" => CommitmentKind.Lodging,
            "meal" => CommitmentKind.Meal,
            "activity" => CommitmentKind.Activity,
            "downtime" => CommitmentKind.Downtime,
            "administrative" => CommitmentKind.Administrative,
            _ => CommitmentKind.Administrative
        };
    }

    private static IReadOnlyList<MissionFieldFixture> ToAppliedFields(IReadOnlyList<MissionAppliedFact> applied)
    {
        return applied
            .Where(fact => fact.FieldPath.StartsWith("/mission/", StringComparison.Ordinal))
            .Select(fact => new MissionFieldFixture(
                FieldId(fact.FieldPath),
                LabelForPath(fact.FieldPath),
                fact.Value,
                SourceLabel(fact.Source),
                "applied"))
            .ToArray();
    }

    private static IReadOnlyList<MissionFieldFixture> ToPendingFields(IReadOnlyList<MissionPendingConfirmation> pending)
    {
        return pending.Select(item => new MissionFieldFixture(
                FieldId(item.FieldPath),
                LabelForPath(item.FieldPath),
                item.ProposedValue,
                SourceLabel(item.Source),
                "pending-confirmation"))
            .ToArray();
    }

    private static IReadOnlyList<MemoryDigestFactFixture> ToDigestFacts(StructuredMemoryDigest digest)
    {
        return digest.LoadBearingFacts
            .Select((fact, index) => new MemoryDigestFactFixture(
                $"memory.fact.{index + 1}",
                fact,
                "structured-digest",
                digest.DigestId))
            .ToArray();
    }

    private static string FieldId(string path) => path.Trim('/').Replace('/', '.');

    private static string LabelForPath(string path)
    {
        var id = FieldId(path);
        return id switch
        {
            "mission.purpose" => "Purpose",
            "mission.destination_country" => "Destination country",
            "mission.start_date" => "Start date",
            "mission.end_date" => "End date",
            "mission.pace" => "Pace",
            "mission.traveler_need" => "Traveler need",
            "constraints.constraint-flex-day" => "Flexible day",
            "constraints.constraint-refundable" => "Refundable bookings",
            _ => id
        };
    }

    private static string SourceLabel(AuthoritySource source) =>
        source is AuthoritySource.User ? "user-stated" : "model-inferred";

    private static string SourceLabel(MissionProposalSource source) =>
        source is MissionProposalSource.UserStated ? "user-stated" : "model-inferred";

    private sealed record MissionAdapterResult(
        bool IsBlocked,
        string Code,
        MissionIntakeProposal? Proposal,
        IReadOnlyList<MissionCommitmentFixture> HighPriorityCommitments,
        string? ErrorCode,
        string? BlockedReason)
    {
        public static MissionAdapterResult Accepted(
            MissionIntakeProposal proposal,
            IReadOnlyList<MissionCommitmentFixture> highPriorityCommitments) =>
            new(false, "adapter_accepted", proposal, highPriorityCommitments, null, null);

        public static MissionAdapterResult Blocked(string code, string errorCode, string blockedReason) =>
            new(true, code, null, [], errorCode, blockedReason);
    }
}

public sealed record RuntimeMissionPlannerResult(
    string RunId,
    string State,
    string ProviderRuntimeOutcomeCode,
    string AdapterOutcomeCode,
    string PlannerOutcomeCode,
    string IntakeOutcomeCode,
    string MemoryDigestOutcomeCode,
    string TraceOutcome,
    string? ErrorCode,
    string? BlockedReason,
    string Provider,
    string Model,
    string? RequestId,
    IReadOnlyList<MissionFieldFixture> AppliedFields,
    IReadOnlyList<MissionFieldFixture> PendingConfirmations,
    IReadOnlyList<MissionCommitmentFixture> HighPriorityCommitments,
    IReadOnlyList<MemoryDigestFactFixture> MemoryDigestFacts);
