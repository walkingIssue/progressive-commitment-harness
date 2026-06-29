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
    IReadOnlyList<string> Constraints,
    IReadOnlyList<string> PendingConfirmations,
    string MemoryDigest,
    int ResponseContentLength,
    string Provider,
    string Model,
    string? RequestId);

public sealed record MissionFieldProposal(
    string Name,
    string Value,
    MissionProposalSource Source,
    bool RequiresConfirmation);

public sealed record MissionCommitmentProposal(
    string CommitmentId,
    string Description,
    MissionCommitmentPriority Priority,
    MissionProposalSource Source);

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
    int PendingConfirmationCount,
    int? ResponseContentLength,
    string? Provider,
    string? Model,
    string? RequestId);
