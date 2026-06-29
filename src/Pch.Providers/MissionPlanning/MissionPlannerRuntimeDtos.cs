using System.Text.Json.Serialization;

namespace Pch.Providers.MissionPlanning;

public sealed record ProviderLocalMissionIntakeProposalMetadata(
    string ProposalId,
    string PacketId,
    string MissionKind,
    IReadOnlyList<string> FieldPaths,
    IReadOnlyList<string> CommitmentIds,
    IReadOnlyList<string> CommitmentKinds,
    IReadOnlyList<string> ConstraintIds,
    int UserStatedFieldCount,
    int InferredFieldCount,
    int CommitmentCount,
    int ConstraintCount,
    int PendingConfirmationCount,
    int ResponseContentLength,
    string Provider,
    string Model,
    string? RequestId);

public sealed record ProviderRuntimeMissionIntakeProposal(
    string ProposalId,
    MissionPlannerResult Result);

public sealed record MissionPlannerRuntimeHandoffResult(
    bool IsAccepted,
    string DecodeOutcomeCode,
    string IntakeOutcomeCode,
    [property: JsonIgnore] ProviderRuntimeMissionIntakeProposal? RuntimeProposal,
    ProviderLocalMissionIntakeProposalMetadata? Proposal);

public sealed record SanitizedMissionPlannerRuntimeEvalRow(
    string Name,
    string PacketId,
    string Scenario,
    bool Passed,
    string ExpectedMissionKind,
    string? ActualMissionKind,
    string DecodeOutcomeCode,
    string IntakeOutcomeCode,
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
