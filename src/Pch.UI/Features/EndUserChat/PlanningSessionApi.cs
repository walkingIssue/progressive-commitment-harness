using System.Collections.Concurrent;

namespace Pch.UI.Features.EndUserChat;

public sealed record PlanningSessionStartRequest(
    string? Prompt,
    string? SelectedModelRole);

public sealed record PlanningSessionAnswerRequest(
    string? PrimitiveInstanceId,
    IReadOnlyDictionary<string, string>? FieldValues);

public sealed record PlanningSessionApiResponse(
    string SessionId,
    string Status,
    PlanningSessionApiState State,
    EndUserValidatedTurnView? Turn,
    PrimitiveAnswerDto? LastAnswer,
    IReadOnlyList<PrimitiveValidationError> ValidationErrors,
    string RawAbsenceState)
{
    public static PlanningSessionApiResponse Unknown(string sessionId) =>
        new(
            sessionId,
            "planning_session_unknown",
            PlanningSessionApiState.Blocked("PCH_UI_PLANNING_SESSION_UNKNOWN", "planning_session_unknown"),
            null,
            null,
            [new(sessionId, "planning_session_unknown", "Planning session was not found.")],
            EndUserChatService.RawAbsenceState);
}

public sealed record PlanningSessionApiState(
    string ModeState,
    string SelectedModelRole,
    string SelectedProvider,
    string LivePreflightState,
    string LiveProposalState,
    string HarnessValidationState,
    string LatestTurnSource,
    string ProviderRequestState,
    string ProviderOutcome,
    string ProviderHealth,
    string FinalState,
    string? ErrorCode,
    string? BlockedReason,
    IReadOnlyList<EndUserChatTurn> Turns,
    IReadOnlyList<EndUserTask> Tasks,
    IReadOnlyList<EndUserEvidenceItem> Evidence,
    IReadOnlyList<EndUserPlanningTimelineItem> PlanningTimeline)
{
    public static PlanningSessionApiState FromState(EndUserChatState state) =>
        new(
            state.ModeState,
            state.SelectedModelRole,
            state.SelectedProvider,
            state.LivePreflightState,
            state.LiveProposalState,
            state.HarnessValidationState,
            state.LatestTurnSource,
            state.ProviderRequestState,
            state.ProviderOutcome,
            state.ProviderHealth,
            state.FinalState,
            state.ErrorCode,
            state.BlockedReason,
            state.Turns,
            state.Tasks,
            state.Evidence,
            state.PlanningTimeline);

    public static PlanningSessionApiState Blocked(string errorCode, string blockedReason) =>
        new(
            "live-model-blocked",
            EndUserModelRoleSelection.InHarnessActionGenerator,
            "server",
            "not_run",
            "provider_blocked",
            "not_run",
            "provider_blocked",
            "not_attempted",
            blockedReason,
            "blocked",
            "validation_blocked",
            errorCode,
            blockedReason,
            [],
            [],
            [],
            []);
}

public sealed class PlanningSessionStore
{
    private readonly ConcurrentDictionary<string, StoredPlanningSession> _sessions = new(StringComparer.Ordinal);

    public async Task<PlanningSessionApiResponse> StartAsync(
        PlanningSessionService service,
        PlanningSessionStartRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(request);

        var sessionId = $"planning-http-session-{Guid.NewGuid():N}";
        var result = await service
            .StartAsync(
                request.Prompt ?? string.Empty,
                EndUserModelRoleSelection.Normalize(request.SelectedModelRole),
                cancellationToken)
            .ConfigureAwait(false);

        _sessions[sessionId] = new StoredPlanningSession(result.State, result.Turn, result.LastAnswer);
        return ToResponse(sessionId, "started", result);
    }

    public async Task<PlanningSessionApiResponse> AnswerAsync(
        PlanningSessionService service,
        string sessionId,
        PlanningSessionAnswerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(request);

        if (!_sessions.TryGetValue(sessionId, out var stored) || stored.Turn is null)
        {
            return PlanningSessionApiResponse.Unknown(sessionId);
        }

        var primitiveInstanceId = string.IsNullOrWhiteSpace(request.PrimitiveInstanceId)
            ? stored.Turn.Primitives.FirstOrDefault(primitive => primitive.RendererKey == "form")?.InstanceId
            : request.PrimitiveInstanceId;
        if (string.IsNullOrWhiteSpace(primitiveInstanceId))
        {
            return PlanningSessionApiResponse.Unknown(sessionId);
        }

        var answer = new PrimitiveAnswerDto(
            stored.Turn.SessionId,
            stored.Turn.TurnId,
            stored.Turn.GraphRevision,
            primitiveInstanceId,
            request.FieldValues?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                ?? new Dictionary<string, string>(StringComparer.Ordinal));
        var result = await service
            .SubmitAnswer(stored.State, stored.Turn, answer, cancellationToken)
            .ConfigureAwait(false);

        _sessions[sessionId] = new StoredPlanningSession(result.State, result.Turn, result.LastAnswer);
        return ToResponse(sessionId, "answered", result);
    }

    public PlanningSessionApiResponse Get(string sessionId) =>
        _sessions.TryGetValue(sessionId, out var stored)
            ? ToResponse(sessionId, "current", new(stored.State, stored.Turn, stored.LastAnswer, []))
            : PlanningSessionApiResponse.Unknown(sessionId);

    private static PlanningSessionApiResponse ToResponse(
        string sessionId,
        string status,
        PlanningSessionUiResult result) =>
        new(
            sessionId,
            status,
            PlanningSessionApiState.FromState(result.State),
            SanitizeTurn(result.Turn),
            result.LastAnswer,
            result.ValidationErrors,
            EndUserChatService.RawAbsenceState);

    private static EndUserValidatedTurnView? SanitizeTurn(EndUserValidatedTurnView? turn) =>
        turn is null ? null : turn with { CanonicalTurn = null };

    private sealed record StoredPlanningSession(
        EndUserChatState State,
        EndUserValidatedTurnView? Turn,
        PrimitiveAnswerDto? LastAnswer);
}
