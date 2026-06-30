using System.Text.Json;
using System.Text.Json.Serialization;
using Pch.Core;

namespace Pch.Harness;

public sealed class LiveSessionConductor
{
    public const string AcceptedCode = "accepted";
    public const string AwaitingUserInputCode = "awaiting_user_input";
    public const string ProviderModelBlockedCode = "provider_model_blocked";
    public const string DecodeBlockedCode = "decode_blocked";
    public const string IntakeBlockedCode = "intake_blocked";
    public const string MissionProposalBlockedCode = "mission_proposal_blocked";
    public const string ApprovalRequiredCode = "approval_required";
    public const string DeterministicFallbackCode = "deterministic_fallback";
    public const string UnsupportedLiveOperationCode = "unsupported_live_operation";
    public const string StalePacketSessionCode = "stale_packet_or_session";
    public const string LiveMissionProposalOperation = "live_mission_proposal";
    public const string LiveRuntimeActionOperation = "live_runtime_action";
    public const string LiveFallbackOperation = "live_deterministic_fallback";
    public const string LiveBlockedOperation = "live_provider_model_blocked";

    private const int MaxTrace = 8;
    private const int MaxTextLength = 120;

    private readonly PromptPacketBuilder _promptPacketBuilder;
    private readonly MissionProposalAdapter _missionProposalAdapter;
    private readonly RuntimeActionApplication _runtimeActionApplication;
    private readonly ItinerarySlotCompiler _itinerarySlotCompiler;
    private readonly LiveTurnProjector _liveTurnProjector;
    private readonly PlanningEditImpactAnalyzer _editImpactAnalyzer;

    private static readonly string[] UnsafeFragments =
    [
        "RAW_",
        "PROVIDER_PAYLOAD",
        "RAW_PROMPT",
        "APPROVAL_TOKEN",
        "HOLD_REFERENCE",
        "PAYMENT",
        "BOOKING_REF",
        "CANDIDATE_DISPLAY",
        "SECRET",
        "CREDENTIAL",
        "PASSWORD",
        "API_KEY"
    ];

    public LiveSessionConductor(
        PromptPacketBuilder? promptPacketBuilder = null,
        MissionProposalAdapter? missionProposalAdapter = null,
        RuntimeActionApplication? runtimeActionApplication = null,
        ItinerarySlotCompiler? itinerarySlotCompiler = null,
        LiveTurnProjector? liveTurnProjector = null,
        PlanningEditImpactAnalyzer? editImpactAnalyzer = null)
    {
        _promptPacketBuilder = promptPacketBuilder ?? new PromptPacketBuilder();
        _missionProposalAdapter = missionProposalAdapter ?? new MissionProposalAdapter();
        _runtimeActionApplication = runtimeActionApplication ?? new RuntimeActionApplication();
        _itinerarySlotCompiler = itinerarySlotCompiler ?? new ItinerarySlotCompiler();
        _liveTurnProjector = liveTurnProjector ?? new LiveTurnProjector();
        _editImpactAnalyzer = editImpactAnalyzer ?? new PlanningEditImpactAnalyzer();
    }

    public LivePlanningTurnResult RunTurn(TripSession session, LivePlanningTurnRequest request)
    {
        var validation = Validate(session, request);
        if (!validation.IsAccepted)
        {
            return BuildBlocked(validation.Code, validation.Summary, session, null, null, []);
        }

        var prompt = _promptPacketBuilder.Build(session, new PromptIntakeRequest(
            request.SessionId,
            request.TransientRawPrompt,
            session.MemoryDigest,
            request.Locale,
            request.ScenarioHints ?? []));
        if (!prompt.IsAccepted || prompt.Packet is null)
        {
            return BuildBlocked(ProviderModelBlockedCode, "Prompt packet construction was blocked.", session, prompt, null, []);
        }

        var proposalValidation = ValidateProposalCorrelation(session, prompt.Packet, request.Proposal);
        if (!proposalValidation.IsAccepted)
        {
            return BuildBlocked(proposalValidation.Code, proposalValidation.Summary, session, prompt, null, []);
        }

        return request.Proposal.Kind switch
        {
            LiveModelProposalKind.MissionProposal => ApplyMissionProposal(session, request, prompt),
            LiveModelProposalKind.RuntimeAction => ApplyRuntimeAction(session, request, prompt),
            LiveModelProposalKind.ModelBlocked => BuildBlocked(ProviderModelBlockedCode, "Provider or model output was blocked.", session, prompt, null, [Trace("model-blocked", "model", ProviderModelBlockedCode)]),
            LiveModelProposalKind.DeterministicFallback => BuildFallback(session, prompt),
            LiveModelProposalKind.Unsupported => BuildBlocked(UnsupportedLiveOperationCode, "Live operation is unsupported.", session, prompt, null, []),
            _ => BuildBlocked(UnsupportedLiveOperationCode, "Live operation is unsupported.", session, prompt, null, [])
        };
    }

    private LivePlanningTurnResult ApplyMissionProposal(
        TripSession session,
        LivePlanningTurnRequest request,
        PromptIntakeResult prompt)
    {
        if (request.Proposal.MissionProposal is null)
        {
            return BuildBlocked(MissionProposalBlockedCode, "Mission proposal was blocked.", session, prompt, null, []);
        }

        var result = _missionProposalAdapter.Apply(session, request.Proposal.MissionProposal);
        if (!result.IsAccepted)
        {
            return BuildBlocked(MissionProposalBlockedCode, "Mission proposal was blocked.", session, prompt, result, []);
        }

        var compilation = _itinerarySlotCompiler.Compile(session, new ItineraryCompilationRequest(
            session.SessionId,
            null,
            null,
            session.MemoryDigest,
            request.ScenarioHints ?? []));
        var pendingConfirmations = result.IntakeResult?.PendingConfirmations ?? [];
        var turn = pendingConfirmations.Count > 0
            ? _liveTurnProjector.FromMissionPendingConfirmations(prompt, pendingConfirmations)
            : _liveTurnProjector.FromPromptIntake(prompt);
        var snapshot = _editImpactAnalyzer.BuildSnapshot(session);
        var code = pendingConfirmations.Count > 0
            ? AwaitingUserInputCode
            : AcceptedCode;

        return new LivePlanningTurnResult(
            IsAccepted: true,
            IsBlocked: false,
            Code: code,
            Summary: code == AwaitingUserInputCode ? "Live planning turn is awaiting user input." : "Live planning turn accepted.",
            Prompt: PromptFragment(prompt),
            Proposal: ProposalFragment(request.Proposal, prompt, code),
            Mission: new LiveMissionFragment(result.Code, result.Summary, result.Digest.DigestId, pendingConfirmations.Count),
            RuntimeAction: null,
            Itinerary: new LiveItineraryFragment(compilation.Code, compilation.Summary, compilation.SlotCount, compilation.ConflictCount),
            TurnProjection: turn,
            PlanningSnapshot: snapshot,
            Trace: [Trace("mission", "mission_proposal", result.Code)]);
    }

    private LivePlanningTurnResult ApplyRuntimeAction(
        TripSession session,
        LivePlanningTurnRequest request,
        PromptIntakeResult prompt)
    {
        if (request.Proposal.RuntimeAction is null)
        {
            return BuildBlocked(DecodeBlockedCode, "Runtime action proposal was blocked before intake.", session, prompt, null, []);
        }

        var runtime = _runtimeActionApplication.Apply(session, request.Proposal.RuntimeAction);
        if (runtime.IsBlocked)
        {
            var code = runtime.IntakeCode switch
            {
                "not_run" => DecodeBlockedCode,
                "approval_required" => ApprovalRequiredCode,
                _ => IntakeBlockedCode
            };
            var projection = _liveTurnProjector.FromRuntimeAction(runtime);
            return new LivePlanningTurnResult(
                IsAccepted: false,
                IsBlocked: true,
                Code: code,
                Summary: code == ApprovalRequiredCode
                    ? "Approval is required before this live turn can continue."
                    : code == DecodeBlockedCode
                        ? "Runtime action proposal was blocked before intake."
                        : "Runtime action intake was blocked.",
                Prompt: PromptFragment(prompt),
                Proposal: ProposalFragment(request.Proposal, prompt, code),
                Mission: null,
                RuntimeAction: new LiveRuntimeActionFragment(runtime.DecodeCode, runtime.IntakeCode, runtime.Stage, runtime.PacketId),
                Itinerary: null,
                TurnProjection: projection,
                PlanningSnapshot: _editImpactAnalyzer.BuildSnapshot(session),
                Trace: CleanTrace(runtime.Trace));
        }

        var acceptedProjection = _liveTurnProjector.FromRuntimeAction(runtime);
        return new LivePlanningTurnResult(
            IsAccepted: true,
            IsBlocked: false,
            Code: AcceptedCode,
            Summary: "Live runtime action accepted.",
            Prompt: PromptFragment(prompt),
            Proposal: ProposalFragment(request.Proposal, prompt, AcceptedCode),
            Mission: null,
            RuntimeAction: new LiveRuntimeActionFragment(runtime.DecodeCode, runtime.IntakeCode, runtime.Stage, runtime.PacketId),
            Itinerary: null,
            TurnProjection: acceptedProjection,
            PlanningSnapshot: _editImpactAnalyzer.BuildSnapshot(session),
            Trace: CleanTrace(runtime.Trace));
    }

    private LivePlanningTurnResult BuildFallback(TripSession session, PromptIntakeResult prompt)
    {
        return new LivePlanningTurnResult(
            IsAccepted: true,
            IsBlocked: false,
            Code: DeterministicFallbackCode,
            Summary: "Deterministic fallback turn emitted.",
            Prompt: PromptFragment(prompt),
            Proposal: new LiveProposalFragment(
                LiveFallbackOperation,
                DeterministicFallbackCode,
                prompt.Packet?.SessionId,
                prompt.Packet?.PacketId,
                prompt.Packet?.Stage,
                false,
                0),
            Mission: null,
            RuntimeAction: null,
            Itinerary: null,
            TurnProjection: _liveTurnProjector.DeterministicFallback(),
            PlanningSnapshot: _editImpactAnalyzer.BuildSnapshot(session),
            Trace: [Trace("fallback", "fallback", DeterministicFallbackCode)]);
    }

    private LivePlanningTurnResult BuildBlocked(
        string code,
        string summary,
        TripSession session,
        PromptIntakeResult? prompt,
        MissionProposalAdapterResult? mission,
        IReadOnlyList<LivePlanningTraceEvent> trace)
    {
        return new LivePlanningTurnResult(
            IsAccepted: false,
            IsBlocked: true,
            Code: SafeText(code),
            Summary: SafeText(summary),
            Prompt: prompt is null ? null : PromptFragment(prompt),
            Proposal: prompt is null ? null : new LiveProposalFragment(
                LiveBlockedOperation,
                SafeText(code),
                prompt.Packet?.SessionId,
                prompt.Packet?.PacketId,
                prompt.Packet?.Stage,
                false,
                0),
            Mission: mission is null ? null : new LiveMissionFragment(mission.Code, mission.Summary, mission.Digest.DigestId, mission.IntakeResult?.PendingConfirmations.Count ?? 0),
            RuntimeAction: null,
            Itinerary: null,
            TurnProjection: _liveTurnProjector.ProviderModelFailure(code),
            PlanningSnapshot: _editImpactAnalyzer.BuildSnapshot(session),
            Trace: trace.Take(MaxTrace).Select(SanitizeTrace).ToArray());
    }

    private static LivePlanningValidation Validate(TripSession session, LivePlanningTurnRequest? request)
    {
        if (request is null || request.Proposal is null)
        {
            return LivePlanningValidation.Blocked(UnsupportedLiveOperationCode, "Live planning turn request failed validation.");
        }

        if (string.IsNullOrWhiteSpace(request.SessionId)
            || !string.Equals(request.SessionId, session.SessionId, StringComparison.Ordinal))
        {
            return LivePlanningValidation.Blocked(UnsupportedLiveOperationCode, "Live planning turn request failed validation.");
        }

        if (string.IsNullOrWhiteSpace(request.TransientRawPrompt))
        {
            return LivePlanningValidation.Blocked(ProviderModelBlockedCode, "Prompt input was blocked.");
        }

        if (request.Proposal.Kind is LiveModelProposalKind.MissionProposal && request.Proposal.MissionProposal is null)
        {
            return LivePlanningValidation.Blocked(MissionProposalBlockedCode, "Mission proposal was blocked.");
        }

        if (request.Proposal.Kind is LiveModelProposalKind.RuntimeAction && request.Proposal.RuntimeAction is null)
        {
            return LivePlanningValidation.Blocked(DecodeBlockedCode, "Runtime action proposal was blocked before intake.");
        }

        if (!OperationMatchesKind(request.Proposal.Kind, request.Proposal.OperationCode))
        {
            return LivePlanningValidation.Blocked(UnsupportedLiveOperationCode, "Live operation is unsupported.");
        }

        return LivePlanningValidation.Accepted();
    }

    private static bool OperationMatchesKind(LiveModelProposalKind kind, string? operationCode)
    {
        return kind switch
        {
            LiveModelProposalKind.MissionProposal => string.Equals(operationCode, LiveMissionProposalOperation, StringComparison.Ordinal),
            LiveModelProposalKind.RuntimeAction => string.Equals(operationCode, LiveRuntimeActionOperation, StringComparison.Ordinal),
            LiveModelProposalKind.ModelBlocked => string.Equals(operationCode, LiveBlockedOperation, StringComparison.Ordinal),
            LiveModelProposalKind.DeterministicFallback => string.Equals(operationCode, LiveFallbackOperation, StringComparison.Ordinal),
            LiveModelProposalKind.Unsupported => true,
            _ => false
        };
    }

    private static LivePlanningValidation ValidateProposalCorrelation(
        TripSession session,
        MissionPlannerPromptPacket packet,
        LiveModelProposalEnvelope proposal)
    {
        if (proposal.Correlation is null)
        {
            return LivePlanningValidation.Accepted();
        }

        if (!string.Equals(proposal.Correlation.SessionId, session.SessionId, StringComparison.Ordinal)
            || !string.Equals(proposal.Correlation.PacketId, packet.PacketId, StringComparison.Ordinal)
            || !string.Equals(proposal.Correlation.Stage, packet.Stage, StringComparison.Ordinal))
        {
            return LivePlanningValidation.Blocked(StalePacketSessionCode, "Live proposal references stale packet or session context.");
        }

        return LivePlanningValidation.Accepted();
    }

    private static LivePromptFragment PromptFragment(PromptIntakeResult prompt)
    {
        return new(
            prompt.Code,
            prompt.Prompt.Length,
            prompt.Prompt.Sha256,
            prompt.Prompt.Category,
            prompt.Packet?.PacketId,
            prompt.Packet?.Stage);
    }

    private static LiveProposalFragment ProposalFragment(
        LiveModelProposalEnvelope proposal,
        PromptIntakeResult prompt,
        string resultCode)
    {
        return new(
            SafeText(proposal.OperationCode),
            SafeText(resultCode),
            proposal.Correlation?.SessionId ?? prompt.Packet?.SessionId,
            proposal.Correlation?.PacketId ?? prompt.Packet?.PacketId,
            proposal.Correlation?.Stage ?? prompt.Packet?.Stage,
            proposal.Kind is LiveModelProposalKind.MissionProposal,
            proposal.MissionProposal?.Fields?.Count ?? 0);
    }

    private static IReadOnlyList<LivePlanningTraceEvent> CleanTrace(IReadOnlyList<SessionTraceEvent> trace)
    {
        return trace
            .Take(MaxTrace)
            .Select(item => Trace(item.EventId, item.Kind, item.Outcome))
            .ToArray();
    }

    private static LivePlanningTraceEvent Trace(string id, string kind, string code)
    {
        return new(SafeId(id), SafeId(kind), SafeText(code));
    }

    private static LivePlanningTraceEvent SanitizeTrace(LivePlanningTraceEvent trace)
    {
        return new(SafeId(trace.TraceId), SafeId(trace.Kind), SafeText(trace.Code));
    }

    private static string SafeId(string? value)
    {
        var text = SafeText(value);
        if (text == "redacted")
        {
            return text;
        }

        var normalized = new string(text
            .Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or ':' ? character : '-')
            .ToArray());
        return string.IsNullOrWhiteSpace(normalized) ? "redacted" : normalized;
    }

    private static string SafeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "redacted";
        }

        if (UnsafeFragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            return "redacted";
        }

        var trimmed = value.Trim();
        return trimmed.Length <= MaxTextLength ? trimmed : trimmed[..MaxTextLength];
    }
}

public sealed record LivePlanningTurnRequest(
    string SessionId,
    string TransientRawPrompt,
    string? Locale,
    IReadOnlyList<string> ScenarioHints,
    LiveModelProposalEnvelope Proposal);

public sealed record LivePlanningTurnResult(
    bool IsAccepted,
    bool IsBlocked,
    string Code,
    string Summary,
    LivePromptFragment? Prompt,
    LiveProposalFragment? Proposal,
    LiveMissionFragment? Mission,
    LiveRuntimeActionFragment? RuntimeAction,
    LiveItineraryFragment? Itinerary,
    LiveTurnProjectionResult TurnProjection,
    PlanningDependencySnapshot PlanningSnapshot,
    IReadOnlyList<LivePlanningTraceEvent> Trace);

public sealed record LiveModelProposalEnvelope(
    LiveModelProposalKind Kind,
    string OperationCode,
    string ResultCode,
    LiveProposalCorrelation? Correlation,
    ProviderMissionProposalMirror? MissionProposal,
    ExternalActionProposal? RuntimeAction,
    string? ModelOutcomeCode)
{
    public static LiveModelProposalEnvelope ForMission(ProviderMissionProposalMirror mission)
    {
        return new(
            LiveModelProposalKind.MissionProposal,
            LiveSessionConductor.LiveMissionProposalOperation,
            LiveSessionConductor.AcceptedCode,
            null,
            mission,
            null,
            null);
    }

    public static LiveModelProposalEnvelope ForRuntimeAction(ExternalActionProposal action)
    {
        return new(
            LiveModelProposalKind.RuntimeAction,
            LiveSessionConductor.LiveRuntimeActionOperation,
            LiveSessionConductor.AcceptedCode,
            null,
            null,
            action,
            null);
    }

    public static LiveModelProposalEnvelope Blocked(string code)
    {
        return new(
            LiveModelProposalKind.ModelBlocked,
            LiveSessionConductor.LiveBlockedOperation,
            LiveSessionConductor.ProviderModelBlockedCode,
            null,
            null,
            null,
            code);
    }

    public static LiveModelProposalEnvelope Fallback()
    {
        return new(
            LiveModelProposalKind.DeterministicFallback,
            LiveSessionConductor.LiveFallbackOperation,
            LiveSessionConductor.DeterministicFallbackCode,
            null,
            null,
            null,
            null);
    }
}

public sealed record LiveProposalCorrelation(
    string SessionId,
    string PacketId,
    string Stage);

[JsonConverter(typeof(JsonStringEnumConverter<LiveModelProposalKind>))]
public enum LiveModelProposalKind
{
    MissionProposal,
    RuntimeAction,
    ModelBlocked,
    DeterministicFallback,
    Unsupported
}

public sealed record LivePromptFragment(
    string Code,
    int PromptLength,
    string PromptSha256,
    string PromptCategory,
    string? PacketId,
    string? Stage);

public sealed record LiveProposalFragment(
    string OperationCode,
    string ResultCode,
    string? SessionId,
    string? PacketId,
    string? Stage,
    bool HasMissionProposal,
    int FieldCount);

public sealed record LiveMissionFragment(
    string Code,
    string Summary,
    string DigestId,
    int PendingConfirmationCount);

public sealed record LiveRuntimeActionFragment(
    string DecodeCode,
    string IntakeCode,
    string Stage,
    string PacketId);

public sealed record LiveItineraryFragment(
    string Code,
    string Summary,
    int SlotCount,
    int ConflictCount);

public sealed record LivePlanningTraceEvent(
    string TraceId,
    string Kind,
    string Code);

public sealed record LivePlanningValidation(
    bool IsAccepted,
    string Code,
    string Summary)
{
    public static LivePlanningValidation Accepted() => new(true, LiveSessionConductor.AcceptedCode, "Live planning turn request accepted.");

    public static LivePlanningValidation Blocked(string code, string summary) => new(false, code, summary);
}
