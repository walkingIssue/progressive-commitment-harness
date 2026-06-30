using Pch.Harness;
using Pch.Providers.Mock;
using Pch.Providers.ModelRoles;

namespace Pch.UI.Features.EndUserChat;

public sealed class EndUserChatService
{
    public const string DefaultPrompt = "Plan a calm family trip to Japan with one quiet day and no real bookings.";
    public const string ModeLabel = "Deterministic offline";
    public const string ModeState = "offline-deterministic";
    public const string RawAbsenceState = "verified";

    private const string PendingConfirmationCode = "end_user_chat_pending_confirmation";

    private readonly GoldenTurnTraceRunner _traceRunner;
    private readonly ModelRoleStatusEvaluator _roleStatusEvaluator;

    public EndUserChatService()
        : this(
            new GoldenTurnTraceRunner(),
            new ModelRoleStatusEvaluator(new MockModelRoleStatusSource()))
    {
    }

    public EndUserChatService(
        GoldenTurnTraceRunner traceRunner,
        ModelRoleStatusEvaluator roleStatusEvaluator)
    {
        _traceRunner = traceRunner ?? throw new ArgumentNullException(nameof(traceRunner));
        _roleStatusEvaluator = roleStatusEvaluator ?? throw new ArgumentNullException(nameof(roleStatusEvaluator));
    }

    public EndUserChatState CreateInitialState() => new(
        ModeLabel,
        ModeState,
        ModelRoleStatusEvaluator.OutcomeReady,
        ActiveRoleMarker(ModelRoleKind.DeterministicOffline),
        RawAbsenceState,
        DefaultPrompt,
        "idle",
        null,
        null,
        [
            new(
                "turn-system-ready",
                "system",
                "mode",
                "ready",
                "Deterministic offline mode is on. This preview never contacts live providers or creates bookings.",
                ModeState,
                null,
                null),
            new(
                "turn-assistant-start",
                "assistant",
                "guidance",
                "ready",
                "Describe the trip you want to test, then send it through the deterministic planner.",
                null,
                null,
                null)
        ]);

    public async Task<EndUserChatState> SendAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var normalizedPrompt = NormalizePrompt(prompt);
        var scenario = SelectScenario(normalizedPrompt);
        var trace = _traceRunner.Run(ScriptFor(scenario));
        var roleStatus = await EvaluateRoleStatus(cancellationToken).ConfigureAwait(false);
        var turns = BuildTurns(normalizedPrompt, scenario, trace, roleStatus);
        var finalState = FinalStateFor(scenario, trace);
        var errorCode = ErrorCodeFor(scenario, trace);
        var blockedReason = trace.IsBlocked ? trace.Code : null;

        return new(
            ModeLabel,
            ModeState,
            roleStatus.OutcomeCode,
            ActiveRoleMarker(roleStatus.ActiveRole),
            RawAbsenceState,
            string.Empty,
            finalState,
            errorCode,
            blockedReason,
            turns);
    }

    public EndUserChatState Send(string prompt) =>
        SendAsync(prompt).GetAwaiter().GetResult();

    private static IReadOnlyList<EndUserChatTurn> BuildTurns(
        string normalizedPrompt,
        EndUserChatScenario scenario,
        GoldenTurnTraceResult trace,
        SanitizedModelRoleStatusEvalRow roleStatus)
    {
        var turns = new List<EndUserChatTurn>
        {
            new(
                "turn-user-1",
                "user",
                "prompt",
                "submitted",
                PromptSummary(normalizedPrompt),
                "prompt_received",
                null,
                null),
            new(
                "turn-provider-role-status",
                "provider",
                "role-status",
                "applied",
                RoleStatusText(roleStatus),
                roleStatus.OutcomeCode,
                null,
                roleStatus.ErrorCode)
        };

        turns.AddRange(trace.Turns.Select(turn => new EndUserChatTurn(
            turn.TurnId,
            turn.Actor,
            turn.Kind,
            StateFor(turn, scenario, trace),
            turn.Summary,
            turn.Code,
            turn.EvidenceReferences.FirstOrDefault(),
            trace.IsBlocked && turn.Kind == "blocked" ? trace.Code : null)));

        turns.Add(new(
            "turn-assistant-final",
            "assistant",
            trace.IsBlocked ? "blocked" : "final",
            FinalStateFor(scenario, trace),
            FinalText(scenario, trace),
            scenario == EndUserChatScenario.PendingConfirmation ? PendingConfirmationCode : trace.Code,
            trace.EvidenceReferences.FirstOrDefault(),
            ErrorCodeFor(scenario, trace)));

        return turns;
    }

    private async Task<SanitizedModelRoleStatusEvalRow> EvaluateRoleStatus(CancellationToken cancellationToken)
    {
        var row = await _roleStatusEvaluator.EvaluateAsync(
            [new ModelRoleStatusEvalCase("end-user-chat-model-role", CreateRoleStatusPacket())],
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return row.Single();
    }

    private static ModelRoleStatusPacket CreateRoleStatusPacket() =>
        new(
            "packet-end-user-chat-role-status",
            [
                new ModelRoleRequest(ModelRoleKind.DeterministicOffline, ModelRoleProviderMode.OfflineDeterministic, true, true),
                new ModelRoleRequest(ModelRoleKind.SmallModel, ModelRoleProviderMode.HostedSmallModel, false, false),
                new ModelRoleRequest(ModelRoleKind.StrongModel, ModelRoleProviderMode.HostedStrongModel, false, false),
                new ModelRoleRequest(ModelRoleKind.LiveProviderDisabled, ModelRoleProviderMode.LiveProviderDisabled, false, false)
            ],
            ModelRoleKind.DeterministicOffline,
            AllowFallback: false,
            Locale: "en-US",
            ContextDigest: "end-user-chat-offline-deterministic");

    private static EndUserChatScenario SelectScenario(string normalizedPrompt)
    {
        if (normalizedPrompt.Contains("block", StringComparison.OrdinalIgnoreCase)
            || normalizedPrompt.Contains("safety", StringComparison.OrdinalIgnoreCase)
            || normalizedPrompt.Contains("approval", StringComparison.OrdinalIgnoreCase)
            || normalizedPrompt.Contains("payment", StringComparison.OrdinalIgnoreCase))
        {
            return EndUserChatScenario.BlockedSafety;
        }

        if (normalizedPrompt.Contains("maybe", StringComparison.OrdinalIgnoreCase)
            || normalizedPrompt.Contains("confirm", StringComparison.OrdinalIgnoreCase))
        {
            return EndUserChatScenario.PendingConfirmation;
        }

        return EndUserChatScenario.HappyPath;
    }

    private static GoldenTurnTraceScript ScriptFor(EndUserChatScenario scenario) =>
        scenario == EndUserChatScenario.BlockedSafety
            ? GoldenTurnTraceRunner.BlockedSafetyScript
            : GoldenTurnTraceRunner.HappyPathScript;

    private static string StateFor(
        GoldenTurnTraceTurn turn,
        EndUserChatScenario scenario,
        GoldenTurnTraceResult trace)
    {
        if (trace.IsBlocked && turn.Kind == "blocked")
        {
            return "blocked";
        }

        if (scenario == EndUserChatScenario.PendingConfirmation && turn.Stage is "mission_intake" or "itinerary_compile")
        {
            return "pending";
        }

        return turn.Kind switch
        {
            "user" or "assistant" => "applied",
            "blocked" => "blocked",
            _ => trace.IsBlocked ? "blocked" : "applied"
        };
    }

    private static string FinalStateFor(EndUserChatScenario scenario, GoldenTurnTraceResult trace)
    {
        if (trace.IsBlocked)
        {
            return "blocked";
        }

        return scenario == EndUserChatScenario.PendingConfirmation ? "pending" : "applied";
    }

    private static string? ErrorCodeFor(EndUserChatScenario scenario, GoldenTurnTraceResult trace)
    {
        if (trace.IsBlocked)
        {
            return trace.Code;
        }

        return scenario == EndUserChatScenario.PendingConfirmation ? PendingConfirmationCode : null;
    }

    private static string NormalizePrompt(string prompt)
    {
        var trimmed = string.IsNullOrWhiteSpace(prompt) ? DefaultPrompt : prompt.Trim();
        return trimmed.Length <= 280 ? trimmed : trimmed[..280];
    }

    private static string PromptSummary(string prompt) =>
        $"Trip request accepted with {prompt.Length} characters. Raw prompt text is kept out of transcript storage.";

    private static string RoleStatusText(SanitizedModelRoleStatusEvalRow roleStatus) =>
        roleStatus.Passed
            ? "Offline deterministic model role is active; live provider roles are disabled for this run."
            : $"Model role posture blocked with {roleStatus.OutcomeCode}.";

    private static string FinalText(EndUserChatScenario scenario, GoldenTurnTraceResult trace)
    {
        if (trace.IsBlocked)
        {
            return "Blocked by the deterministic safety gate before any live provider or booking step.";
        }

        return scenario == EndUserChatScenario.PendingConfirmation
            ? "Pending confirmation before final itinerary and availability steps."
            : "Final deterministic trip plan is ready with canonical evidence markers.";
    }

    private static string ActiveRoleMarker(ModelRoleKind? role) =>
        role switch
        {
            ModelRoleKind.DeterministicOffline => "deterministic-offline",
            ModelRoleKind.SmallModel => "small-model",
            ModelRoleKind.StrongModel => "strong-model",
            ModelRoleKind.LiveProviderDisabled => "live-provider-disabled",
            _ => "none"
        };

    private enum EndUserChatScenario
    {
        HappyPath,
        BlockedSafety,
        PendingConfirmation
    }
}
