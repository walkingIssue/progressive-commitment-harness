namespace Pch.UI.Features.EndUserChat;

public sealed record EndUserChatState(
    string ModeLabel,
    string ModeState,
    string RawAbsenceState,
    string Prompt,
    string FinalState,
    string? ErrorCode,
    string? BlockedReason,
    IReadOnlyList<EndUserChatTurn> Turns);

public sealed record EndUserChatTurn(
    string TurnId,
    string Role,
    string Kind,
    string State,
    string Text,
    string? OutcomeCode,
    string? EvidenceId,
    string? ErrorCode);

public sealed record EndUserChatResult(
    string RunId,
    string FinalState,
    string? ErrorCode,
    string? BlockedReason,
    IReadOnlyList<EndUserChatTurn> Turns);
