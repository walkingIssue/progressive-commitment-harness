namespace Pch.UI.Features.EndUserChat;

public sealed record EndUserChatState(
    string ModeLabel,
    string ModeState,
    string RoleStatusOutcome,
    string RoleStatusActiveRole,
    string ProviderOutcome,
    string ProviderHealth,
    string CreditState,
    string LastProviderFailureCode,
    string ApprovalState,
    string RawAbsenceState,
    string Prompt,
    string ComposerState,
    string FinalState,
    string? ErrorCode,
    string? BlockedReason,
    IReadOnlyList<EndUserChatTurn> Turns,
    IReadOnlyList<EndUserTask> Tasks,
    EndUserFormCard? FormCard,
    EndUserChoiceSetCard? ChoiceSet,
    EndUserApprovalPlate? ApprovalPlate,
    EndUserProviderFailureNotice? ProviderFailure,
    IReadOnlyList<EndUserEvidenceItem> Evidence,
    IReadOnlyList<EndUserPlanTrailItem> PlanTrail);

public sealed record EndUserChatTurn(
    string TurnId,
    string Role,
    string Kind,
    string State,
    string Text,
    string? OutcomeCode,
    string? EvidenceId,
    string? ErrorCode,
    string? CandidateId = null,
    string? CandidateCategory = null);

public sealed record EndUserSelectedOptionBubble(
    string CandidateId,
    string Category,
    string Mood,
    string MoodTone,
    string Title,
    string Summary,
    string State,
    IReadOnlyList<string> EvidenceIds);

public sealed record EndUserTask(
    string TaskId,
    string Title,
    string State,
    int Progress,
    string StatusLabel,
    IReadOnlyList<EndUserTaskStep> Steps,
    bool IsExpanded);

public sealed record EndUserTaskStep(
    string StepId,
    string Label,
    string State);

public sealed record EndUserFormCard(
    string FormId,
    string Title,
    string Prompt,
    string State,
    IReadOnlyList<EndUserFormField> Fields,
    IReadOnlyList<string> EvidenceIds);

public sealed record EndUserFormField(
    string FieldId,
    string Label,
    string Value,
    bool IsRequired,
    string State,
    string? EvidenceId);

public sealed record EndUserChoiceSetCard(
    string ChoiceSetId,
    string Title,
    string Prompt,
    string State,
    string? SelectedCandidateId,
    string? DeferredCandidateId,
    IReadOnlyList<EndUserCandidateOption> Candidates);

public sealed record EndUserCandidateOption(
    string CandidateId,
    string Kind,
    string Category,
    string Mood,
    string MoodTone,
    string Title,
    string Summary,
    string State,
    string Source,
    IReadOnlyList<string> EvidenceIds);

public sealed record EndUserApprovalPlate(
    string ApprovalId,
    string Title,
    string State,
    string OutcomeCode,
    string? BlockedReason,
    IReadOnlyList<string> EvidenceIds);

public sealed record EndUserProviderFailureNotice(
    string NoticeId,
    string OutcomeCode,
    string State,
    string Message,
    bool CanRetry,
    bool CanContinueDeterministic);

public sealed record EndUserEvidenceItem(
    string EvidenceId,
    string Label,
    string Kind,
    string OutcomeCode);

public sealed record EndUserPlanTrailItem(
    string TrailId,
    string Kind,
    string State,
    string Label,
    string? CandidateId,
    string? EvidenceId,
    string OutcomeCode);
