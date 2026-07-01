namespace Pch.UI.Features.EndUserChat;

public sealed record ValidatedTurnView(
    string TurnId,
    string SessionId,
    int GraphRevision,
    string Source,
    string OutcomeCode,
    string ManifestVersion,
    IReadOnlyList<ValidatedPrimitive> Primitives,
    IReadOnlyList<ValidatedTaskPrimitive> Tasks,
    IReadOnlyList<EndUserPlanningTimelineItem> Timeline,
    IReadOnlyList<EndUserEvidenceItem> Evidence,
    string ProviderRequestState,
    string ProviderOutcome,
    string RawAbsenceState);

public sealed record ValidatedPrimitive(
    string InstanceId,
    string PrimitiveId,
    string RendererKey,
    string Title,
    string Prompt,
    string MoodToken,
    string MediaToken,
    string State,
    IReadOnlyList<ValidatedPrimitiveField> Fields,
    IReadOnlyList<EndUserCandidateOption> Candidates,
    IReadOnlyList<string> EvidenceIds,
    string? ErrorCode = null,
    string? BlockedReason = null);

public sealed record ValidatedPrimitiveField(
    string FieldId,
    string Label,
    string PrimitiveId,
    string RendererKey,
    string Value,
    bool IsRequired,
    string State,
    string? EvidenceId,
    IReadOnlyList<string> AllowedValues);

public sealed record ValidatedTaskPrimitive(
    string TaskId,
    string Title,
    string State,
    int Progress,
    string StatusLabel,
    IReadOnlyList<EndUserTaskStep> Steps);

public sealed record PrimitiveAnswerDto(
    string SessionId,
    string TurnId,
    int GraphRevision,
    string PrimitiveInstanceId,
    IReadOnlyDictionary<string, string> FieldValues);

public sealed record PlanningSessionUiResult(
    EndUserChatState State,
    ValidatedTurnView? Turn,
    PrimitiveAnswerDto? LastAnswer,
    IReadOnlyList<PrimitiveValidationError> ValidationErrors);

public sealed record PrimitiveValidationError(
    string FieldId,
    string ErrorCode,
    string Message);
