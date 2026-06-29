using Pch.Core;

namespace Pch.Harness;

public sealed class MissionProposalAdapter
{
    private const int MaxTextLength = 160;
    private const int MaxEvidenceIds = 6;
    private const int MaxFields = 12;
    private const int MaxConstraints = 12;
    private const int MaxCommitments = 12;

    private static readonly IReadOnlySet<string> AllowedFieldPaths = new HashSet<string>(StringComparer.Ordinal)
    {
        "/mission/purpose",
        "/mission/destination_country",
        "/mission/start_date",
        "/mission/end_date"
    };

    private readonly MissionIntakeApplication _intake;

    public MissionProposalAdapter(MissionIntakeApplication? intake = null)
    {
        _intake = intake ?? new MissionIntakeApplication();
    }

    public MissionProposalAdapterResult Apply(TripSession session, ProviderMissionProposalMirror mirror)
    {
        var validation = ValidateAndMap(mirror);
        if (!validation.IsAccepted || validation.Proposal is null)
        {
            return MissionProposalAdapterResult.Rejected(
                validation.Code,
                validation.Summary,
                BuildDigestSnapshot(session));
        }

        var intake = _intake.Apply(session, validation.Proposal);
        return MissionProposalAdapterResult.Accepted(intake.Code, intake.Summary, intake.Digest, intake);
    }

    public MissionProposalAdapterValidation ValidateAndMap(ProviderMissionProposalMirror mirror)
    {
        if (string.IsNullOrWhiteSpace(mirror.ProposalId) || IsOverlong(mirror.ProposalId))
        {
            return Reject("invalid_proposal_id", "Mission proposal failed validation.");
        }

        if (mirror.Fields.Count > MaxFields || mirror.Constraints.Count > MaxConstraints || mirror.Commitments.Count > MaxCommitments)
        {
            return Reject("too_many_items", "Mission proposal exceeded item limits.");
        }

        var fields = new List<MissionFieldProposal>();
        foreach (var field in mirror.Fields)
        {
            if (!AllowedFieldPaths.Contains(field.FieldPath))
            {
                return Reject("unsupported_field_path", "Mission proposal contains an unsupported field path.");
            }

            if (!ValidText(field.Value) || !TryMapSource(field.Source, out var source) || !ValidEvidence(field.EvidenceIds))
            {
                return Reject("invalid_field", "Mission proposal field failed validation.");
            }

            fields.Add(new(field.FieldPath, field.Value, source, field.EvidenceIds));
        }

        var constraints = new List<ConstraintProposal>();
        foreach (var constraint in mirror.Constraints)
        {
            if (!ValidText(constraint.ConstraintId) || !ValidText(constraint.Label) || !ValidText(constraint.Value)
                || !TryMapSource(constraint.Source, out var source) || !ValidEvidence(constraint.EvidenceIds))
            {
                return Reject("invalid_constraint", "Mission proposal constraint failed validation.");
            }

            constraints.Add(new(
                constraint.ConstraintId,
                constraint.Label,
                constraint.Value,
                source,
                constraint.IsHard,
                constraint.EvidenceIds));
        }

        var commitments = new List<CommitmentProposal>();
        foreach (var commitment in mirror.Commitments)
        {
            if (!ValidText(commitment.CommitmentId) || !ValidText(commitment.Title)
                || !TryMapCommitmentKind(commitment.Kind, out var kind)
                || !TryMapPriority(commitment.Priority, out var priority)
                || !TryMapSource(commitment.Source, out var source)
                || !ValidOptionalText(commitment.Location)
                || !ValidEvidence(commitment.EvidenceIds))
            {
                return Reject("invalid_commitment", "Mission proposal commitment failed validation.");
            }

            commitments.Add(new(
                commitment.CommitmentId,
                kind,
                commitment.Title,
                commitment.StartsAt,
                commitment.EndsAt,
                commitment.Location,
                commitment.IsIrreversible,
                commitment.RequiresSpend,
                priority,
                source,
                commitment.EvidenceIds));
        }

        return new(true, "accepted", "Mission proposal mirror accepted.", new MissionIntakeProposal(
            mirror.ProposalId,
            fields,
            constraints,
            commitments));
    }

    private StructuredMemoryDigest BuildDigestSnapshot(TripSession session)
    {
        return _intake.BuildDigest(session);
    }

    private static MissionProposalAdapterValidation Reject(string code, string summary)
    {
        return new(false, code, summary, null);
    }

    private static bool ValidText(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && !IsOverlong(value);
    }

    private static bool ValidOptionalText(string? value)
    {
        return value is null || !IsOverlong(value);
    }

    private static bool IsOverlong(string value)
    {
        return value.Length > MaxTextLength;
    }

    private static bool ValidEvidence(IReadOnlyList<string> evidenceIds)
    {
        return evidenceIds.Count <= MaxEvidenceIds
            && evidenceIds.All(id => !string.IsNullOrWhiteSpace(id) && !IsOverlong(id));
    }

    private static bool TryMapSource(string value, out AuthoritySource source)
    {
        source = value switch
        {
            "user" => AuthoritySource.User,
            "trusted_tool" => AuthoritySource.TrustedTool,
            "strong_model_inference" => AuthoritySource.StrongModelInference,
            "small_model_draft" => AuthoritySource.SmallModelDraft,
            "harness_default" => AuthoritySource.HarnessDefault,
            "country_pack_assumption" => AuthoritySource.CountryPackAssumption,
            _ => default
        };
        return value is "user" or "trusted_tool" or "strong_model_inference" or "small_model_draft" or "harness_default" or "country_pack_assumption";
    }

    private static bool TryMapPriority(string value, out CommitmentPriority priority)
    {
        priority = value switch
        {
            "normal" => CommitmentPriority.Normal,
            "high" => CommitmentPriority.High,
            _ => default
        };
        return value is "normal" or "high";
    }

    private static bool TryMapCommitmentKind(string value, out CommitmentKind kind)
    {
        kind = value switch
        {
            "fixed_anchor" => CommitmentKind.FixedAnchor,
            "travel" => CommitmentKind.Travel,
            "lodging" => CommitmentKind.Lodging,
            "meal" => CommitmentKind.Meal,
            "activity" => CommitmentKind.Activity,
            "administrative" => CommitmentKind.Administrative,
            "downtime" => CommitmentKind.Downtime,
            _ => default
        };
        return value is "fixed_anchor" or "travel" or "lodging" or "meal" or "activity" or "administrative" or "downtime";
    }
}

public sealed record ProviderMissionProposalMirror(
    string ProposalId,
    IReadOnlyList<ProviderMissionFieldMirror> Fields,
    IReadOnlyList<ProviderConstraintMirror> Constraints,
    IReadOnlyList<ProviderCommitmentMirror> Commitments);

public sealed record ProviderMissionFieldMirror(
    string FieldPath,
    string Value,
    string Source,
    IReadOnlyList<string> EvidenceIds);

public sealed record ProviderConstraintMirror(
    string ConstraintId,
    string Label,
    string Value,
    string Source,
    bool IsHard,
    IReadOnlyList<string> EvidenceIds);

public sealed record ProviderCommitmentMirror(
    string CommitmentId,
    string Kind,
    string Title,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    string? Location,
    bool IsIrreversible,
    bool RequiresSpend,
    string Priority,
    string Source,
    IReadOnlyList<string> EvidenceIds);

public sealed record MissionProposalAdapterValidation(
    bool IsAccepted,
    string Code,
    string Summary,
    MissionIntakeProposal? Proposal);

public sealed record MissionProposalAdapterResult(
    bool IsAccepted,
    string Code,
    string Summary,
    StructuredMemoryDigest Digest,
    MissionIntakeResult? IntakeResult)
{
    public static MissionProposalAdapterResult Accepted(
        string code,
        string summary,
        StructuredMemoryDigest digest,
        MissionIntakeResult intake)
    {
        return new(true, code, summary, digest, intake);
    }

    public static MissionProposalAdapterResult Rejected(
        string code,
        string summary,
        StructuredMemoryDigest digest)
    {
        return new(false, code, summary, digest, null);
    }
}
