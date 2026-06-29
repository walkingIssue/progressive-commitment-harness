using Pch.Core;

namespace Pch.Harness;

public sealed class MissionIntakeApplication
{
    private const int MaxDigestFacts = 8;
    private const int MaxDigestConfirmations = 8;
    private const int MaxDigestTraceReferences = 8;

    public MissionIntakeResult Apply(TripSession session, MissionIntakeProposal proposal)
    {
        var appliedFacts = new List<MissionAppliedFact>();
        var pending = new List<MissionPendingConfirmation>();
        var traceReferences = new List<string>();

        var mission = session.Mission;
        foreach (var field in proposal.Fields)
        {
            if (!CanApplyMissionField(field))
            {
                pending.Add(new MissionPendingConfirmation(
                    field.FieldPath,
                    field.Value,
                    field.Source,
                    "unsupported_or_invalid_field",
                    field.EvidenceIds));
                traceReferences.AddRange(field.EvidenceIds);
                continue;
            }

            if (CanApplySource(field.Source))
            {
                mission = ApplyMissionField(mission, field);
                appliedFacts.Add(new MissionAppliedFact(field.FieldPath, field.Value, field.Source, field.EvidenceIds));
                traceReferences.AddRange(field.EvidenceIds);
            }
            else
            {
                pending.Add(new MissionPendingConfirmation(
                    field.FieldPath,
                    field.Value,
                    field.Source,
                    "requires_confirmation",
                    field.EvidenceIds));
                traceReferences.AddRange(field.EvidenceIds);
            }
        }

        var constraints = mission.Constraints.ToList();
        foreach (var constraint in proposal.Constraints)
        {
            var item = new Constraint(
                constraint.ConstraintId,
                constraint.Label,
                constraint.Value,
                constraint.Source,
                constraint.IsHard);
            if (CanApplySource(constraint.Source))
            {
                constraints.Add(item);
                appliedFacts.Add(new MissionAppliedFact(
                    $"/constraints/{constraint.ConstraintId}",
                    constraint.Value,
                    constraint.Source,
                    constraint.EvidenceIds));
            }
            else
            {
                pending.Add(new MissionPendingConfirmation(
                    $"/constraints/{constraint.ConstraintId}",
                    constraint.Value,
                    constraint.Source,
                    "requires_confirmation",
                    constraint.EvidenceIds));
            }

            traceReferences.AddRange(constraint.EvidenceIds);
        }

        var commitments = mission.Commitments.ToList();
        foreach (var commitment in proposal.Commitments)
        {
            var item = new Commitment(
                commitment.CommitmentId,
                commitment.Kind,
                commitment.Title,
                commitment.StartsAt,
                commitment.EndsAt,
                commitment.Location,
                commitment.IsIrreversible,
                commitment.RequiresSpend);

            if (commitment.Priority is CommitmentPriority.High || CanApplySource(commitment.Source))
            {
                commitments.Add(item);
                appliedFacts.Add(new MissionAppliedFact(
                    $"/commitments/{commitment.CommitmentId}",
                    commitment.Title,
                    commitment.Source,
                    commitment.EvidenceIds));
            }
            else
            {
                pending.Add(new MissionPendingConfirmation(
                    $"/commitments/{commitment.CommitmentId}",
                    commitment.Title,
                    commitment.Source,
                    "requires_confirmation",
                    commitment.EvidenceIds));
            }

            traceReferences.AddRange(commitment.EvidenceIds);
        }

        mission = mission with
        {
            Constraints = constraints,
            Commitments = commitments
        };
        session.ReplaceMission(mission);

        var digest = BuildDigest(session, appliedFacts, pending, traceReferences);
        return new MissionIntakeResult(
            IsApplied: true,
            Code: "mission_intake_applied",
            Summary: "Mission proposal applied with authority checks.",
            AppliedFacts: appliedFacts,
            PendingConfirmations: pending,
            Digest: digest);
    }

    public StructuredMemoryDigest BuildDigest(
        TripSession session,
        IReadOnlyList<MissionAppliedFact>? appliedFacts = null,
        IReadOnlyList<MissionPendingConfirmation>? pendingConfirmations = null,
        IEnumerable<string>? traceReferences = null)
    {
        var facts = new List<string>
        {
            $"purpose: {session.Mission.Purpose}",
            $"destination_country: {session.Mission.DestinationCountry}",
            $"date_window: {session.Mission.StartDate:yyyy-MM-dd}/{session.Mission.EndDate:yyyy-MM-dd}",
            $"traveler_count: {session.Mission.Travelers.Count}"
        };
        facts.AddRange(session.Mission.Commitments
            .OrderBy(commitment => commitment.StartsAt)
            .ThenBy(commitment => commitment.CommitmentId, StringComparer.Ordinal)
            .Select(commitment => $"commitment: {commitment.Title}"));
        facts.AddRange(session.Mission.Constraints
            .OrderBy(constraint => constraint.ConstraintId, StringComparer.Ordinal)
            .Select(constraint => $"constraint: {constraint.Label}={constraint.Value}"));

        var pending = pendingConfirmations ?? [];
        var traces = traceReferences?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray() ?? [];

        return new StructuredMemoryDigest(
            DigestId: $"digest-{session.SessionId}-{session.Mission.MissionId}",
            SessionId: session.SessionId,
            MissionId: session.Mission.MissionId,
            LoadBearingFacts: facts.Take(MaxDigestFacts).ToArray(),
            PendingConfirmations: pending.Take(MaxDigestConfirmations).ToArray(),
            TraceReferences: traces.Take(MaxDigestTraceReferences).ToArray());
    }

    private static bool CanApplySource(AuthoritySource source)
    {
        return source is AuthoritySource.User or AuthoritySource.TrustedTool or AuthoritySource.HarnessDefault;
    }

    private static string? ReadMissionField(TripMission mission, string fieldPath)
    {
        return fieldPath switch
        {
            "/mission/purpose" => mission.Purpose,
            "/mission/destination_country" => mission.DestinationCountry,
            "/mission/start_date" => mission.StartDate.ToString("yyyy-MM-dd"),
            "/mission/end_date" => mission.EndDate.ToString("yyyy-MM-dd"),
            _ => null
        };
    }

    private static bool CanApplyMissionField(MissionFieldProposal field)
    {
        return field.FieldPath switch
        {
            "/mission/purpose" or "/mission/destination_country" => !string.IsNullOrWhiteSpace(field.Value),
            "/mission/start_date" or "/mission/end_date" => DateOnly.TryParse(field.Value, out _),
            _ => false
        };
    }

    private static TripMission ApplyMissionField(TripMission mission, MissionFieldProposal field)
    {
        return field.FieldPath switch
        {
            "/mission/purpose" => mission with { Purpose = field.Value },
            "/mission/destination_country" => mission with { DestinationCountry = field.Value },
            "/mission/start_date" when DateOnly.TryParse(field.Value, out var date) => mission with { StartDate = date },
            "/mission/end_date" when DateOnly.TryParse(field.Value, out var date) => mission with { EndDate = date },
            _ => mission
        };
    }
}

public sealed record MissionIntakeProposal(
    string ProposalId,
    IReadOnlyList<MissionFieldProposal> Fields,
    IReadOnlyList<ConstraintProposal> Constraints,
    IReadOnlyList<CommitmentProposal> Commitments);

public sealed record MissionFieldProposal(
    string FieldPath,
    string Value,
    AuthoritySource Source,
    IReadOnlyList<string> EvidenceIds);

public sealed record ConstraintProposal(
    string ConstraintId,
    string Label,
    string Value,
    AuthoritySource Source,
    bool IsHard,
    IReadOnlyList<string> EvidenceIds);

public sealed record CommitmentProposal(
    string CommitmentId,
    CommitmentKind Kind,
    string Title,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    string? Location,
    bool IsIrreversible,
    bool RequiresSpend,
    CommitmentPriority Priority,
    AuthoritySource Source,
    IReadOnlyList<string> EvidenceIds);

public enum CommitmentPriority
{
    Normal,
    High
}

public sealed record MissionAppliedFact(
    string FieldPath,
    string Value,
    AuthoritySource Source,
    IReadOnlyList<string> EvidenceIds);

public sealed record MissionPendingConfirmation(
    string FieldPath,
    string ProposedValue,
    AuthoritySource Source,
    string ReasonCode,
    IReadOnlyList<string> EvidenceIds);

public sealed record MissionIntakeResult(
    bool IsApplied,
    string Code,
    string Summary,
    IReadOnlyList<MissionAppliedFact> AppliedFacts,
    IReadOnlyList<MissionPendingConfirmation> PendingConfirmations,
    StructuredMemoryDigest Digest);

public sealed record StructuredMemoryDigest(
    string DigestId,
    string SessionId,
    string MissionId,
    IReadOnlyList<string> LoadBearingFacts,
    IReadOnlyList<MissionPendingConfirmation> PendingConfirmations,
    IReadOnlyList<string> TraceReferences);
