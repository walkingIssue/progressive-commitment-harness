namespace Pch.Core;

public enum AuthoritySource
{
    User,
    TrustedTool,
    StrongModelInference,
    SmallModelDraft,
    HarnessDefault,
    CountryPackAssumption
}

public enum CommitmentKind
{
    FixedAnchor,
    Travel,
    Lodging,
    Meal,
    Activity,
    Administrative,
    Downtime
}

public enum CandidateKind
{
    Flight,
    Hotel,
    Restaurant,
    Activity,
    Transit,
    ScheduleBlock
}

public enum EvidenceKind
{
    UserStatement,
    TrustedToolOutput,
    SearchSummary,
    CandidatePoolEvidence,
    CountryPackAssumption,
    ConfirmedStatePatch,
    ModelInferencePendingConfirmation
}

public sealed record TripMission(
    string MissionId,
    string Purpose,
    string DestinationCountry,
    DateOnly StartDate,
    DateOnly EndDate,
    IReadOnlyList<Traveler> Travelers,
    IReadOnlyList<Constraint> Constraints,
    IReadOnlyList<Commitment> Commitments)
{
    public int DayCount => EndDate.DayNumber - StartDate.DayNumber + 1;
}

public sealed record Traveler(
    string TravelerId,
    string DisplayName,
    string? HomeAirportCode,
    IReadOnlyList<string> Needs);

public sealed record Constraint(
    string ConstraintId,
    string Label,
    string Value,
    AuthoritySource Source,
    bool IsHard);

public sealed record Commitment(
    string CommitmentId,
    CommitmentKind Kind,
    string Title,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    string? Location,
    bool IsIrreversible,
    bool RequiresSpend);

public sealed record Candidate(
    string CandidateId,
    CandidateKind Kind,
    string Title,
    string Summary,
    decimal? EstimatedCost,
    string? Currency,
    IReadOnlyList<string> EvidenceIds,
    double RelevanceScore);

public sealed record CandidatePool(
    string PoolId,
    string Stage,
    IReadOnlyList<Candidate> Candidates,
    IReadOnlyList<string> AppliedConstraintIds,
    DateTimeOffset CreatedAt);

public sealed record DecisionLedger(IReadOnlyList<DecisionRecord> Records)
{
    public static DecisionLedger Empty { get; } = new([]);
}

public sealed record DecisionRecord(
    string DecisionId,
    string Stage,
    string ActionKind,
    string Summary,
    AuthoritySource Source,
    DateTimeOffset RecordedAt);

public sealed record EvidenceTrace(IReadOnlyList<EvidenceItem> Items)
{
    public static EvidenceTrace Empty { get; } = new([]);
}

public sealed record EvidenceItem(
    string EvidenceId,
    EvidenceKind Kind,
    string Summary,
    string? SourceUri,
    DateTimeOffset ObservedAt);

public sealed record ClaimLedger(IReadOnlyList<ClaimRecord> Claims)
{
    public static ClaimLedger Empty { get; } = new([]);
}

public sealed record ClaimRecord(
    string ClaimId,
    string Text,
    IReadOnlyList<string> EvidenceIds,
    bool IsUserVisible);
