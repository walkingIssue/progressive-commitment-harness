using Pch.Providers.Mock;
using Pch.Providers.ModelRoles;

namespace Pch.UI.Features.EndUserChat;

public sealed class EndUserLiveModelTurnService
{
    public const string PreflightDeterministic = "deterministic_default";
    public const string PreflightBlockedByGuard = "blocked_by_guard";
    public const string PreflightPassed = "preflight_passed";
    public const string LatestDeterministic = "deterministic_fallback";
    public const string LatestLiveAccepted = "live_model_output";
    public const string ProviderRequestNotAttempted = "not_attempted";
    public const string ProviderRequestAttempted = "attempted";

    private readonly Func<IReadOnlyDictionary<string, string?>> _environmentFactory;
    private readonly LiveModelRoleRunner _runner;

    public EndUserLiveModelTurnService()
        : this(ReadProcessEnvironment, new LiveModelRoleRunner(new MockModelCompletionClient(), new MockModelCompletionClient()))
    {
    }

    public EndUserLiveModelTurnService(
        Func<IReadOnlyDictionary<string, string?>> environmentFactory,
        LiveModelRoleRunner? runner = null)
    {
        _environmentFactory = environmentFactory ?? throw new ArgumentNullException(nameof(environmentFactory));
        _runner = runner ?? new LiveModelRoleRunner(new MockModelCompletionClient(), new MockModelCompletionClient());
    }

    public EndUserLiveModelSnapshot CreateSnapshot(string selectedRole)
    {
        var normalizedRole = EndUserModelRoleSelection.Normalize(selectedRole);
        if (normalizedRole == EndUserModelRoleSelection.DeterministicOffline)
        {
            return EndUserLiveModelSnapshot.Deterministic();
        }

        var options = LiveModelRunnerOptions.FromEnvironment(_environmentFactory());
        var role = ToLiveRole(normalizedRole);
        var modelId = ModelIdFor(options, role);
        var preflightState = options.LiveModeEnabled && options.ApiKeyAvailable
            ? PreflightPassed
            : PreflightBlockedByGuard;

        return new EndUserLiveModelSnapshot(
            normalizedRole,
            options.Provider,
            preflightState,
            LatestDeterministic,
            ProviderRequestNotAttempted,
            options.LiveModeEnabled ? "live_config_present" : "live_guard_disabled",
            options.ApiKeyAvailable ? "key_available" : "key_unavailable",
            options.CreditGuardEnabled ? "credit_guard_enabled" : "credit_guard_skipped",
            options.LiveModeEnabled
                ? options.ApiKeyAvailable ? "none" : LiveModelRoleRunner.OutcomeKeyMissing
                : LiveModelRoleRunner.OutcomeLiveModeDisabled,
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

        var options = LiveModelRunnerOptions.FromEnvironment(_environmentFactory());
        var role = ToLiveRole(normalizedRole);
        var modelId = ModelIdFor(options, role);
        if (!options.LiveModeEnabled || !options.ApiKeyAvailable)
        {
            var outcome = options.LiveModeEnabled
                ? LiveModelRoleRunner.OutcomeKeyMissing
                : LiveModelRoleRunner.OutcomeLiveModeDisabled;
            return BlockedByGuard(normalizedRole, options, modelId, outcome);
        }

        var packet = new LiveModelRunPacket(
            "packet-end-user-live-model-turn",
            role,
            prompt,
            ["assistant_work_bubble", "candidate_option_cards", "pending_confirmation"],
            "en-US",
            RequiresFallback: false,
            ContextDigest: "end-user-chat-live-model-read-only");

        var result = await _runner.RunAsync(
            packet,
            new LiveModelRunOptions(options),
            cancellationToken).ConfigureAwait(false);

        if (result.IsAccepted)
        {
            return new EndUserLiveModelSnapshot(
                normalizedRole,
                result.Provider ?? options.Provider,
                PreflightPassed,
                LatestLiveAccepted,
                ProviderRequestAttempted,
                result.OutcomeCode,
                "live_provider_accepted",
                "credit_guard_checked",
                "none",
                result.ModelId ?? modelId,
                result.RequestId,
                null,
                null,
                new EndUserChatTurn(
                    "turn-live-model-run",
                    "provider",
                    "live-model",
                    "applied",
                    "Live model turn returned a sanitized accepted result. Raw completion text is not stored in the UI transcript.",
                    result.OutcomeCode,
                    "evidence-chat-live-model",
                    null),
                null);
        }

        return new EndUserLiveModelSnapshot(
            normalizedRole,
            options.Provider,
            PreflightPassed,
            LatestDeterministic,
            ProviderRequestAttempted,
            result.OutcomeCode,
            "live_provider_blocked",
            "credit_guard_checked",
            result.OutcomeCode,
            result.ModelId ?? modelId,
            result.RequestId,
            "PCH_UI_LIVE_MODEL_SANITIZED_FAILURE",
            result.OutcomeCode,
            new EndUserChatTurn(
                "turn-live-model-run",
                "provider",
                "live-model",
                "blocked",
                "Live model turn was blocked with a sanitized provider outcome. Deterministic fallback remains available.",
                result.OutcomeCode,
                "evidence-chat-live-model",
                "PCH_UI_LIVE_MODEL_SANITIZED_FAILURE"),
            LiveFailureNotice(result.OutcomeCode));
    }

    private static EndUserLiveModelSnapshot BlockedByGuard(
        string normalizedRole,
        LiveModelRunnerOptions options,
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

    private static LiveModelRole ToLiveRole(string selectedRole) =>
        selectedRole == EndUserModelRoleSelection.StrongPlanner
            ? LiveModelRole.StrongPlanner
            : LiveModelRole.InHarnessActionGenerator;

    private static string ModelIdFor(LiveModelRunnerOptions options, LiveModelRole role) =>
        role == LiveModelRole.StrongPlanner ? options.StrongPlannerModelId : options.InHarnessModelId;

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
