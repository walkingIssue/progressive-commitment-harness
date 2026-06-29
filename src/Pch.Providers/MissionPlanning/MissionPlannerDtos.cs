namespace Pch.Providers.MissionPlanning;

public sealed record MissionPlannerPacket(
    string PacketId,
    string Scenario,
    string Prompt,
    string Locale,
    IReadOnlyList<string> KnownConstraints);

public sealed record MissionPlannerOptions(
    string? Model = null,
    double Temperature = 0,
    int MaxTokens = 1_200);

public sealed record MissionPlannerResult(
    string PacketId,
    string MissionKind,
    IReadOnlyList<MissionFieldProposal> Fields,
    IReadOnlyList<MissionCommitmentProposal> Commitments,
    IReadOnlyList<MissionConstraintProposal> Constraints,
    IReadOnlyList<string> PendingConfirmations,
    string MemoryDigest,
    int ResponseContentLength,
    string Provider,
    string Model,
    string? RequestId);

public sealed record MissionFieldProposal(
    string FieldPath,
    string Value,
    MissionProposalSource AuthoritySource,
    IReadOnlyList<string> EvidenceIds,
    bool RequiresConfirmation);

public sealed record MissionConstraintProposal(
    string ConstraintId,
    string Label,
    string Value,
    MissionProposalSource AuthoritySource,
    bool IsHard,
    IReadOnlyList<string> EvidenceIds);

public sealed record MissionCommitmentProposal(
    string CommitmentId,
    string CommitmentKind,
    string Title,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    string? Location,
    bool IsIrreversible,
    bool RequiresSpend,
    MissionCommitmentPriority CommitmentPriority,
    MissionProposalSource AuthoritySource,
    IReadOnlyList<string> EvidenceIds);

public enum MissionProposalSource
{
    UserStated,
    ModelInferred
}

public enum MissionCommitmentPriority
{
    Normal,
    High,
    Critical
}

public sealed record MissionPlannerEvalCase(
    string Name,
    MissionPlannerPacket Packet,
    string ExpectedMissionKind);

public sealed record SanitizedMissionPlannerEvalRow(
    string Name,
    string PacketId,
    string Scenario,
    bool Passed,
    string ExpectedMissionKind,
    string? ActualMissionKind,
    string OutcomeCode,
    string? ErrorCode,
    int UserStatedFieldCount,
    int InferredFieldCount,
    int CommitmentCount,
    int ConstraintCount,
    int PendingConfirmationCount,
    int? ResponseContentLength,
    string? Provider,
    string? Model,
    string? RequestId);
