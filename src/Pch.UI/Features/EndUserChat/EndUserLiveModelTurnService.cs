using Pch.Harness;
using Pch.Providers.LivePreflight;
using Pch.Providers.ModelCompletion;
using Pch.Providers.ModelRoles;

namespace Pch.UI.Features.EndUserChat;

public sealed class EndUserLiveModelTurnService
{
    public const string PreflightDeterministic = "deterministic_default";
    public const string PreflightBlockedByGuard = "blocked_by_guard";
    public const string PreflightBlocked = "preflight_blocked";
    public const string PreflightPassed = "preflight_passed";
    public const string PreflightReady = "preflight_ready";
    public const string LatestDeterministic = "deterministic_fallback";
    public const string LatestLiveAccepted = "live_preflight_accepted";
    public const string ProviderRequestNotAttempted = "not_attempted";
    public const string ProviderRequestAttempted = "attempted";

    private readonly Func<IReadOnlyDictionary<string, string?>> _environmentFactory;
    private readonly LivePreflightEvaluator _preflightEvaluator;
    private readonly LiveSessionConductor _conductor;

    public EndUserLiveModelTurnService()
        : this(ReadProcessEnvironment, CreateDefaultPreflightEvaluator(), new LiveSessionConductor())
    {
    }

    public EndUserLiveModelTurnService(
        Func<IReadOnlyDictionary<string, string?>> environmentFactory,
        LivePreflightEvaluator? preflightEvaluator = null,
        LiveSessionConductor? conductor = null)
    {
        _environmentFactory = environmentFactory ?? throw new ArgumentNullException(nameof(environmentFactory));
        _preflightEvaluator = preflightEvaluator ?? CreateDefaultPreflightEvaluator();
        _conductor = conductor ?? new LiveSessionConductor();
    }

    public EndUserLiveModelSnapshot CreateSnapshot(string selectedRole)
    {
        var normalizedRole = EndUserModelRoleSelection.Normalize(selectedRole);
        if (normalizedRole == EndUserModelRoleSelection.DeterministicOffline)
        {
            return EndUserLiveModelSnapshot.Deterministic();
        }

        var options = LivePreflightOptions.FromEnvironment(_environmentFactory());
        var role = ToLiveRole(normalizedRole);
        var modelId = ModelIdFor(options, role);
        var preflightState = options.Enabled && options.ApiKeyAvailable
            ? PreflightReady
            : PreflightBlockedByGuard;

        return new EndUserLiveModelSnapshot(
            normalizedRole,
            options.Provider,
            preflightState,
            LatestDeterministic,
            ProviderRequestNotAttempted,
            options.Enabled ? "live_config_present" : "live_guard_disabled",
            options.ApiKeyAvailable ? "key_available" : "key_unavailable",
            options.CreditGuardEnabled ? "credit_guard_enabled" : "credit_guard_skipped",
            options.Enabled
                ? options.ApiKeyAvailable ? "none" : LivePreflightRunner.OutcomeKeyMissing
                : LivePreflightRunner.OutcomeDisabled,
            modelId,
            null,
            preflightState == PreflightBlockedByGuard ? "PCH_UI_LIVE_MODEL_GUARDED" : null,
            preflightState == PreflightBlockedByGuard ? "Live model mode is guarded until explicit provider configuration is present." : null,
            null,
            null);
    }

    public async Task<EndUserLiveModelSnapshot> TryRunAsync(
        string prompt,
        string selectedRole,
        CancellationToken cancellationToken = default)
    {
        var normalizedRole = EndUserModelRoleSelection.Normalize(selectedRole);
        if (normalizedRole == EndUserModelRoleSelection.DeterministicOffline)
        {
            return EndUserLiveModelSnapshot.Deterministic();
        }

        var options = LivePreflightOptions.FromEnvironment(_environmentFactory());
        var role = ToLiveRole(normalizedRole);
        var modelId = ModelIdFor(options, role);
        if (!options.Enabled || !options.ApiKeyAvailable)
        {
            var outcome = options.Enabled
                ? LivePreflightRunner.OutcomeKeyMissing
                : LivePreflightRunner.OutcomeDisabled;
            return BlockedByGuard(normalizedRole, options, modelId, outcome);
        }

        var packet = new LivePreflightPacket(
            "packet-end-user-live-preflight",
            [new LivePreflightRoleProbe(role, $"probe-{normalizedRole}")],
            "en-US",
            ContextDigest: "end-user-chat-live-model-read-only");

        var row = (await _preflightEvaluator.EvaluateAsync(
            [new LivePreflightEvalCase("end-user-live-preflight", packet)],
            options,
            cancellationToken).ConfigureAwait(false)).Single();

        if (row.Passed)
        {
            var conductor = RunConductorFallback(prompt);
            return new EndUserLiveModelSnapshot(
                normalizedRole,
                row.Provider ?? options.Provider,
                PreflightPassed,
                LatestLiveAccepted,
                ProviderRequestAttempted,
                row.OutcomeCode,
                $"harness_conductor_{conductor.Code}",
                CreditStateFor(row.OutcomeCode, options),
                "none",
                row.Model ?? modelId,
                row.RequestId,
                null,
                null,
                new EndUserChatTurn(
                    "turn-live-model-run",
                    "provider",
                    "live-model",
                    "applied",
                    "Live provider preflight accepted structured output; the harness conductor kept this planning turn on deterministic fallback.",
                    row.OutcomeCode,
                    "evidence-chat-live-model",
                    null),
                null);
        }

        return new EndUserLiveModelSnapshot(
            normalizedRole,
            options.Provider,
            IsGuardOutcome(row.OutcomeCode) ? PreflightBlockedByGuard : PreflightBlocked,
            LatestDeterministic,
            ProviderRequestStateFor(row.OutcomeCode),
            row.OutcomeCode,
            "live_preflight_blocked",
            CreditStateFor(row.OutcomeCode, options),
            row.OutcomeCode,
            row.Model ?? modelId,
            row.RequestId,
            "PCH_UI_LIVE_MODEL_SANITIZED_FAILURE",
            row.OutcomeCode,
            new EndUserChatTurn(
                "turn-live-model-run",
                "provider",
                "live-model",
                "blocked",
                "Live provider preflight was blocked with a sanitized outcome. Deterministic fallback remains available.",
                row.OutcomeCode,
                "evidence-chat-live-model",
                "PCH_UI_LIVE_MODEL_SANITIZED_FAILURE"),
            LiveFailureNotice(row.OutcomeCode));
    }

    private static EndUserLiveModelSnapshot BlockedByGuard(
        string normalizedRole,
        LivePreflightOptions options,
        string modelId,
        string outcome) =>
        new(
            normalizedRole,
            options.Provider,
            PreflightBlockedByGuard,
            LatestDeterministic,
            ProviderRequestNotAttempted,
            outcome,
            "live_guard_blocked",
            options.CreditGuardEnabled ? "credit_guard_not_run" : "credit_guard_skipped",
            outcome,
            modelId,
            null,
            "PCH_UI_LIVE_MODEL_GUARDED",
            outcome,
            new EndUserChatTurn(
                "turn-live-model-run",
                "provider",
                "live-model",
                "blocked",
                "Live model mode is guarded until explicit provider configuration is present. The planner continued with deterministic fallback.",
                outcome,
                "evidence-chat-live-model",
                "PCH_UI_LIVE_MODEL_GUARDED"),
            LiveFailureNotice(outcome));

    private static EndUserProviderFailureNotice LiveFailureNotice(string outcome) =>
        new(
            "notice-live-model-guard",
            outcome,
            "blocked",
            "Live model output was not used. The deterministic planner path stayed active with sanitized failure markers.",
            CanRetry: false,
            CanContinueDeterministic: true);

    private LivePlanningTurnResult RunConductorFallback(string prompt)
    {
        var session = SyntheticTripFactory.CreateSession(3);
        return _conductor.RunTurn(session, new LivePlanningTurnRequest(
            session.SessionId,
            prompt,
            "en-US",
            ["end-user-chat"],
            LiveModelProposalEnvelope.Fallback()));
    }

    private static LiveModelRole ToLiveRole(string selectedRole) =>
        selectedRole == EndUserModelRoleSelection.StrongPlanner
            ? LiveModelRole.StrongPlanner
            : LiveModelRole.InHarnessActionGenerator;

    private static string ModelIdFor(LivePreflightOptions options, LiveModelRole role) =>
        role == LiveModelRole.StrongPlanner ? options.StrongPlannerModelId : options.InHarnessModelId;

    private static string ProviderRequestStateFor(string outcome) =>
        IsGuardOutcome(outcome)
            ? ProviderRequestNotAttempted
            : ProviderRequestAttempted;

    private static string CreditStateFor(string outcome, LivePreflightOptions options)
    {
        if (!options.CreditGuardEnabled)
        {
            return "credit_guard_skipped";
        }

        return outcome switch
        {
            LivePreflightRunner.OutcomeDisabled or
            LivePreflightRunner.OutcomeKeyMissing or
            LivePreflightRunner.OutcomeSchemaUnsupported or
            LivePreflightRunner.OutcomeFallbackDisabled => "credit_guard_not_run",
            LivePreflightRunner.OutcomeCreditExhausted => "credit_exhausted",
            _ => "credit_guard_checked"
        };
    }

    private static bool IsGuardOutcome(string outcome) =>
        outcome is LivePreflightRunner.OutcomeDisabled
            or LivePreflightRunner.OutcomeKeyMissing
            or LivePreflightRunner.OutcomeSchemaUnsupported
            or LivePreflightRunner.OutcomeFallbackDisabled;

    private static LivePreflightEvaluator CreateDefaultPreflightEvaluator()
    {
        var client = new DisabledLivePreflightClient();
        return new LivePreflightEvaluator(new LivePreflightRunner(client, client));
    }

    private static IReadOnlyDictionary<string, string?> ReadProcessEnvironment()
    {
        var values = Environment.GetEnvironmentVariables();
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var key in values.Keys.OfType<string>())
        {
            environment[key] = values[key]?.ToString();
        }

        return environment;
    }

    private sealed class DisabledLivePreflightClient : IModelCompletionClient, IProviderCreditClient
    {
        public Task<ModelCompletionResponse> CompleteAsync(
            ModelCompletionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ModelCompletionResponse>(
                new InvalidOperationException("Live preflight client is not configured."));

        public Task<ProviderCreditStatus> GetCreditStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProviderCreditStatus(null, null, null, IsExhausted: true));
    }
}

public sealed record EndUserLiveModelSnapshot(
    string SelectedModelRole,
    string SelectedProvider,
    string LivePreflightState,
    string LatestTurnSource,
    string ProviderRequestState,
    string ProviderOutcome,
    string ProviderHealth,
    string CreditState,
    string LastProviderFailureCode,
    string? ModelId,
    string? RequestId,
    string? ErrorCode,
    string? BlockedReason,
    EndUserChatTurn? Turn,
    EndUserProviderFailureNotice? FailureNotice)
{
    public bool IsLiveAccepted() =>
        LatestTurnSource == EndUserLiveModelTurnService.LatestLiveAccepted;

    public static EndUserLiveModelSnapshot Deterministic() =>
        new(
            EndUserModelRoleSelection.DeterministicOffline,
            "none",
            EndUserLiveModelTurnService.PreflightDeterministic,
            EndUserLiveModelTurnService.LatestDeterministic,
            EndUserLiveModelTurnService.ProviderRequestNotAttempted,
            EndUserChatService.ProviderOutcomeFallback,
            "offline_ready",
            "credits_not_used",
            "none",
            null,
            null,
            null,
            null,
            null,
            null);
}
