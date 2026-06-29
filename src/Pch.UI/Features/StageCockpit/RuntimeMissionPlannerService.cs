using Pch.Core;
using Pch.Harness;
using Pch.Providers.MissionPlanning;
using ProviderCommitment = Pch.Providers.MissionPlanning.MissionCommitmentProposal;
using ProviderConstraint = Pch.Providers.MissionPlanning.MissionConstraintProposal;
using ProviderField = Pch.Providers.MissionPlanning.MissionFieldProposal;

namespace Pch.UI.Features.StageCockpit;

public sealed class RuntimeMissionPlannerService
{
    private const string RawPromptSentinel = "RAW_RAMBLING_PROMPT_SHOULD_NOT_LEAK";
    private const string RawProviderSentinel = "RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK";
    private const string RawPacketSentinel = "RAW_PACKET_ID_SHOULD_NOT_LEAK";
    private readonly MissionPlannerRuntimeBridge _runtimeBridge = new();
    private readonly MissionProposalAdapter _missionProposalAdapter = new();

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
        var handoff = _runtimeBridge.Bridge(packet, planner);
        if (!handoff.IsAccepted || handoff.RuntimeProposal is null)
        {
            return Blocked(
                runId,
                handoff.DecodeOutcomeCode,
                "not_run",
                "planner_mock_accepted",
                ProviderRuntimeErrorCode(handoff.DecodeOutcomeCode),
                ProviderRuntimeBlockedReason(handoff.DecodeOutcomeCode),
                planner);
        }

        var mirror = ToProviderMissionProposalMirror(handoff.RuntimeProposal);
        var adapter = _missionProposalAdapter.Apply(session, mirror);
        if (!adapter.IsAccepted || adapter.IntakeResult is null)
        {
            return new(
                runId,
                "blocked",
                handoff.DecodeOutcomeCode,
                adapter.Code,
                "planner_mock_accepted",
                "not_run",
                "not_run",
                "mission_intake.blocked",
                AdapterErrorCode(adapter.Code),
                adapter.Summary,
                planner.Provider,
                planner.Model,
                planner.RequestId,
                [],
                [],
                [],
                ToDigestFacts(adapter.Digest));
        }

        var intake = adapter.IntakeResult;
        var state = intake.PendingConfirmations.Count > 0 ? "proposed" : "applied";

        return new(
            runId,
            state,
            handoff.DecodeOutcomeCode,
            "adapter_accepted",
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
            ToHighPriorityCommitments(handoff.RuntimeProposal.Result.Commitments),
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
            "mission.adapter-blocked" => "vacation",
            "mission.unknown-commitment-kind" => "helping_family",
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
                new ProviderField("/mission/destination_country", "Japan", MissionProposalSource.ModelInferred, ["evidence-model-destination"], true),
                new ProviderField("/mission/start_date", "2026-10-05", MissionProposalSource.ModelInferred, ["evidence-model-dates"], true)
            ],
            "mission.validation-blocked" =>
            [
                new ProviderField("/mission/purpose", "vacation", MissionProposalSource.UserStated, ["evidence-user-purpose"], false)
            ],
            "mission.adapter-blocked" =>
            [
                new ProviderField("/mission/freeform_secret_note", "do not persist", MissionProposalSource.UserStated, ["evidence-user-purpose"], false)
            ],
            _ => []
        };

        IReadOnlyList<ProviderCommitment> commitments = runId switch
        {
            "mission.non-vacation-commitment" =>
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
            ],
            "mission.unknown-commitment-kind" =>
            [
                new ProviderCommitment(
                    "commitment.unknown-kind",
                    "RAW_UNKNOWN_KIND_SHOULD_NOT_LEAK",
                    "RAW_UNKNOWN_COMMITMENT_TITLE_SHOULD_NOT_LEAK",
                    StartsAt: null,
                    EndsAt: null,
                    Location: null,
                    IsIrreversible: false,
                    RequiresSpend: false,
                    MissionCommitmentPriority.High,
                    MissionProposalSource.UserStated,
                    ["evidence-user-commitment"])
            ],
            _ => []
        };

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

    private static ProviderMissionProposalMirror ToProviderMissionProposalMirror(ProviderRuntimeMissionIntakeProposal runtimeProposal) =>
        new(
            runtimeProposal.ProposalId,
            runtimeProposal.Result.Fields.Select(ToMirrorField).ToArray(),
            runtimeProposal.Result.Constraints.Select(ToMirrorConstraint).ToArray(),
            runtimeProposal.Result.Commitments.Select(ToMirrorCommitment).ToArray());

    private static ProviderMissionFieldMirror ToMirrorField(ProviderField field) =>
        new(field.FieldPath, field.Value, SourceCode(field.AuthoritySource), field.EvidenceIds);

    private static ProviderConstraintMirror ToMirrorConstraint(ProviderConstraint constraint) =>
        new(
            constraint.ConstraintId,
            constraint.Label,
            constraint.Value,
            SourceCode(constraint.AuthoritySource),
            constraint.IsHard,
            constraint.EvidenceIds);

    private static ProviderCommitmentMirror ToMirrorCommitment(ProviderCommitment commitment) =>
        new(
            commitment.CommitmentId,
            MapCommitmentKindCode(commitment.CommitmentKind),
            commitment.Title,
            commitment.StartsAt,
            commitment.EndsAt,
            commitment.Location,
            commitment.IsIrreversible,
            commitment.RequiresSpend,
            commitment.CommitmentPriority is MissionCommitmentPriority.High or MissionCommitmentPriority.Critical
                ? "high"
                : "normal",
            SourceCode(commitment.AuthoritySource),
            commitment.EvidenceIds);

    private static string SourceCode(MissionProposalSource source) =>
        source is MissionProposalSource.UserStated
            ? "user"
            : "strong_model_inference";

    private static string MapCommitmentKindCode(string kind)
    {
        return kind.Trim().ToLowerInvariant() switch
        {
            "fixed_anchor" or "client_meeting" or "memorial_service" or "family_support" => "fixed_anchor",
            "travel" => "travel",
            "lodging" => "lodging",
            "meal" => "meal",
            "activity" => "activity",
            "downtime" => "downtime",
            "administrative" => "administrative",
            _ => "unsupported_commitment_kind"
        };
    }

    private static IReadOnlyList<MissionCommitmentFixture> ToHighPriorityCommitments(IReadOnlyList<ProviderCommitment> commitments)
    {
        return commitments
            .Where(commitment => commitment.CommitmentPriority is MissionCommitmentPriority.High or MissionCommitmentPriority.Critical)
            .Select(commitment => new MissionCommitmentFixture(
                commitment.CommitmentId,
                commitment.Title,
                MapCommitmentKindCode(commitment.CommitmentKind),
                "high",
                SourceLabel(commitment.AuthoritySource)))
            .ToArray();
    }

    private static string ProviderRuntimeErrorCode(string decodeOutcomeCode) =>
        decodeOutcomeCode switch
        {
            MissionPlannerRuntimeBridge.DecodePacketIdMismatch => "PCH_UI_MISSION_PROVIDER_PACKET_ID_MISMATCH",
            MissionPlannerRuntimeBridge.DecodeMalformedResult => "PCH_UI_MISSION_PROVIDER_MALFORMED_RESULT",
            MissionPlannerRuntimeBridge.DecodeUnsupportedMissionKind => "PCH_UI_MISSION_PROVIDER_UNSUPPORTED_KIND",
            _ => "PCH_UI_MISSION_PROVIDER_RUNTIME_BLOCKED"
        };

    private static string ProviderRuntimeBlockedReason(string decodeOutcomeCode) =>
        decodeOutcomeCode switch
        {
            MissionPlannerRuntimeBridge.DecodePacketIdMismatch => "Mission planner runtime blocked a packet/result mismatch.",
            MissionPlannerRuntimeBridge.DecodeMalformedResult => "Mission planner runtime blocked malformed output.",
            MissionPlannerRuntimeBridge.DecodeUnsupportedMissionKind => "Mission planner runtime blocked an unsupported mission kind.",
            _ => "Mission planner runtime blocked the provider result."
        };

    private static string AdapterErrorCode(string adapterCode) =>
        adapterCode switch
        {
            "unsupported_field_path" => "PCH_UI_MISSION_ADAPTER_UNSUPPORTED_FIELD_PATH",
            "too_many_items" => "PCH_UI_MISSION_ADAPTER_TOO_MANY_ITEMS",
            "invalid_field" => "PCH_UI_MISSION_ADAPTER_INVALID_FIELD",
            "invalid_constraint" => "PCH_UI_MISSION_ADAPTER_INVALID_CONSTRAINT",
            "invalid_commitment" => "PCH_UI_MISSION_ADAPTER_INVALID_COMMITMENT",
            _ => "PCH_UI_MISSION_ADAPTER_BLOCKED"
        };

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
