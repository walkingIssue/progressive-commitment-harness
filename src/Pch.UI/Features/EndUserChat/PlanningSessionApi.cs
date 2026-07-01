using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pch.Harness;
using Pch.Providers.PlannerPrimitives;

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
    IReadOnlyList<PrimitiveAnswerDto> Answers,
    IReadOnlyList<PlanningSessionTraceEntry> Trace,
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
            [],
            [],
            [new(sessionId, "planning_session_unknown", "Planning session was not found.")],
            EndUserChatService.RawAbsenceState);
}

public sealed record PlanningSessionTraceEntry(
    string TraceId,
    string ProviderRequestState,
    string ProviderOutcome,
    string? Provider,
    string? Model,
    string? RequestId,
    string ValidatedTurnId,
    string ValidationCode,
    string PrimitiveHash,
    IReadOnlyList<string> PrimitiveInstanceIds,
    IReadOnlyList<string> TaskIds,
    IReadOnlyList<string> AnswerIds)
{
    public static PlanningSessionTraceEntry FromModelRun(
        PlannerModelRun model,
        EndUserValidatedTurnView turn,
        PrimitiveAnswerDto? answer)
    {
        var primitiveIds = turn.Primitives.Select(primitive => primitive.InstanceId).ToArray();
        var taskIds = turn.Tasks.Select(task => task.TaskId).ToArray();
        var answerIds = answer is null ? [] : answer.FieldValues.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();
        var hashInput = JsonSerializer.Serialize(new
        {
            turn.TurnId,
            turn.OutcomeCode,
            Primitives = turn.Primitives.Select(primitive => new
            {
                primitive.InstanceId,
                primitive.PrimitiveId,
                primitive.RendererKey,
                primitive.Title,
                primitive.Prompt,
                Fields = primitive.Fields.Select(field => new
                {
                    field.FieldId,
                    field.Label,
                    field.Value,
                    field.AllowedValues
                }),
                Candidates = primitive.Candidates.Select(candidate => new
                {
                    candidate.CandidateId,
                    candidate.Title,
                    candidate.Mood
                })
            })
        });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput)))[..16].ToLowerInvariant();

        return new(
            $"trace-{turn.TurnId}",
            model.ProviderRequestState,
            model.ProviderOutcome,
            model.Result?.Provider,
            model.Result?.Model,
            model.Result?.RequestId,
            turn.TurnId,
            turn.OutcomeCode,
            hash,
            primitiveIds,
            taskIds,
            answerIds);
    }
}

public sealed class PlanningSessionContext
{
    private readonly List<PrimitiveAnswerDto> _answers = [];
    private readonly List<PlanningSessionTraceEntry> _trace = [];

    public PlanningSessionContext(TripSession session)
    {
        HarnessContext = new Pch.Harness.PlanningSessionContext(session ?? throw new ArgumentNullException(nameof(session)));
    }

    public Pch.Harness.PlanningSessionContext HarnessContext { get; }

    public TripSession Session => HarnessContext.TripSession;

    public EndUserValidatedTurnView? LastTurn { get; set; }

    public IReadOnlyList<PrimitiveAnswerDto> Answers => _answers;

    public IReadOnlyList<PlanningSessionTraceEntry> Trace => _trace;

    public void RecordAnswer(PrimitiveAnswerDto answer) => _answers.Add(answer);

    public void AddTrace(PlanningSessionTraceEntry entry) => _trace.Add(entry);
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
        var context = service.CreateContext(sessionId);
        var result = await service
            .StartAsync(
                context,
                request.Prompt ?? string.Empty,
                EndUserModelRoleSelection.Normalize(request.SelectedModelRole),
                cancellationToken)
            .ConfigureAwait(false);

        _sessions[sessionId] = new StoredPlanningSession(context, result.State, result.Turn, result.LastAnswer);
        return ToResponse(sessionId, "started", context, result);
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
            .SubmitAnswer(stored.Context, stored.State, stored.Turn, answer, cancellationToken)
            .ConfigureAwait(false);

        _sessions[sessionId] = stored with { State = result.State, Turn = result.Turn, LastAnswer = result.LastAnswer };
        return ToResponse(sessionId, "answered", stored.Context, result);
    }

    public PlanningSessionApiResponse Get(string sessionId) =>
        _sessions.TryGetValue(sessionId, out var stored)
            ? ToResponse(sessionId, "current", stored.Context, new(stored.State, stored.Turn, stored.LastAnswer, []))
            : PlanningSessionApiResponse.Unknown(sessionId);

    private static PlanningSessionApiResponse ToResponse(
        string sessionId,
        string status,
        PlanningSessionContext context,
        PlanningSessionUiResult result) =>
        new(
            sessionId,
            status,
            PlanningSessionApiState.FromState(result.State),
            SanitizeTurn(result.Turn),
            result.LastAnswer,
            context.Answers,
            context.Trace,
            result.ValidationErrors,
            EndUserChatService.RawAbsenceState);

    private static EndUserValidatedTurnView? SanitizeTurn(EndUserValidatedTurnView? turn) =>
        turn is null ? null : turn with { CanonicalTurn = null };

    private sealed record StoredPlanningSession(
        PlanningSessionContext Context,
        EndUserChatState State,
        EndUserValidatedTurnView? Turn,
        PrimitiveAnswerDto? LastAnswer);
}
