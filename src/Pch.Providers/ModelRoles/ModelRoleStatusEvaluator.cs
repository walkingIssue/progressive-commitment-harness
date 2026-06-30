using Pch.Providers.Errors;

namespace Pch.Providers.ModelRoles;

public sealed class ModelRoleStatusEvaluator
{
    public const string RejectedRowName = "model_role_status_rejected";
    public const string RejectedRowPacketId = "model_role_status_packet_redacted";
    public const string OutcomeReady = "model_role_status_ready";
    public const string OutcomeLiveProviderBlocked = "model_role_live_provider_blocked";
    public const string OutcomeFallbackDisabled = "model_role_fallback_disabled";
    public const string OutcomeMalformedConfig = "model_role_malformed_config";
    public const string OutcomePacketMismatch = "model_role_packet_mismatch";
    public const string OutcomeProviderUnavailable = "model_role_provider_unavailable";
    public const string OutcomeError = "model_role_status_error";

    private readonly IModelRoleStatusSource _source;

    public ModelRoleStatusEvaluator(IModelRoleStatusSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public async Task<IReadOnlyList<SanitizedModelRoleStatusEvalRow>> EvaluateAsync(
        IReadOnlyList<ModelRoleStatusEvalCase> cases,
        ModelRoleStatusOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cases);

        var rows = new List<SanitizedModelRoleStatusEvalRow>(cases.Count);
        foreach (var evalCase in cases)
        {
            var malformedPacketRow = ValidateEvalCase(evalCase);
            if (malformedPacketRow is not null)
            {
                rows.Add(malformedPacketRow);
                continue;
            }

            try
            {
                var result = await _source.GetStatusAsync(evalCase.Packet, options, cancellationToken).ConfigureAwait(false);
                rows.Add(ToRow(evalCase, result));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                rows.Add(ExceptionRow(ex));
            }
        }

        return rows;
    }

    private static SanitizedModelRoleStatusEvalRow? ValidateEvalCase(ModelRoleStatusEvalCase? evalCase)
    {
        if (evalCase?.Packet is null ||
            evalCase.Packet.Roles is null ||
            evalCase.Packet.Roles.Count == 0 ||
            !IsSupportedRole(evalCase.Packet.PreferredRole))
        {
            return Rejected(OutcomeMalformedConfig);
        }

        var roles = new HashSet<ModelRoleKind>();
        var defaultCount = 0;
        foreach (var role in evalCase.Packet.Roles)
        {
            if (role is null ||
                !IsSupportedRole(role.Role) ||
                !IsSupportedMode(role.Mode) ||
                !roles.Add(role.Role))
            {
                return Rejected(OutcomeMalformedConfig);
            }

            if (role.IsDefault)
            {
                defaultCount++;
            }
        }

        return roles.Contains(evalCase.Packet.PreferredRole) && defaultCount <= 1
            ? null
            : Rejected(OutcomeMalformedConfig);
    }

    private static SanitizedModelRoleStatusEvalRow ToRow(
        ModelRoleStatusEvalCase evalCase,
        ModelRoleStatusResult? result)
    {
        if (result is null ||
            result.Roles is null ||
            result.Kind == ModelRoleStatusResultKind.MalformedConfig)
        {
            return Rejected(OutcomeMalformedConfig);
        }

        if (!string.Equals(evalCase.Packet.PacketId, result.PacketId, StringComparison.Ordinal))
        {
            return Rejected(OutcomePacketMismatch);
        }

        var validationOutcome = ValidateResultRoles(evalCase.Packet, result);
        if (validationOutcome is not null)
        {
            return Rejected(validationOutcome);
        }

        var outcome = result.Kind switch
        {
            ModelRoleStatusResultKind.Ready => OutcomeReady,
            ModelRoleStatusResultKind.LiveProviderBlocked => OutcomeLiveProviderBlocked,
            ModelRoleStatusResultKind.FallbackDisabled => OutcomeFallbackDisabled,
            ModelRoleStatusResultKind.MalformedConfig => OutcomeMalformedConfig,
            _ => OutcomeMalformedConfig
        };

        if (outcome == OutcomeMalformedConfig)
        {
            return Rejected(outcome);
        }

        var roleRows = evalCase.Packet.Roles
            .Select(role =>
            {
                var resultRole = result.Roles.Single(item => item.Role == role.Role);
                return new SanitizedModelRoleRow(
                    role.Role,
                    role.Mode,
                    resultRole.Availability,
                    SanitizeStatusCode(resultRole.StatusCode));
            })
            .OrderBy(role => role.Role)
            .ToArray();

        if (outcome != OutcomeReady)
        {
            return Blocked(outcome, roleRows, result.ActiveRole, result.LiveProviderEnabled, result.FallbackEnabled);
        }

        return new SanitizedModelRoleStatusEvalRow(
            evalCase.Name,
            evalCase.Packet.PacketId,
            true,
            outcome,
            null,
            result.ActiveRole,
            roleRows,
            result.LiveProviderEnabled,
            result.FallbackEnabled,
            result.ResponseContentLength,
            result.Provider,
            result.Model,
            result.RequestId);
    }

    private static string? ValidateResultRoles(
        ModelRoleStatusPacket packet,
        ModelRoleStatusResult result)
    {
        if (result.ActiveRole is not null && !packet.Roles.Any(role => role.Role == result.ActiveRole))
        {
            return OutcomeMalformedConfig;
        }

        var packetRoles = packet.Roles.ToDictionary(role => role.Role);
        var resultRoles = new HashSet<ModelRoleKind>();
        foreach (var role in result.Roles)
        {
            if (role is null ||
                !IsSupportedRole(role.Role) ||
                !IsSupportedMode(role.Mode) ||
                !IsSupportedAvailability(role.Availability) ||
                string.IsNullOrWhiteSpace(role.StatusCode) ||
                !packetRoles.TryGetValue(role.Role, out var packetRole) ||
                packetRole.Mode != role.Mode ||
                !resultRoles.Add(role.Role))
            {
                return OutcomeMalformedConfig;
            }
        }

        return resultRoles.SetEquals(packetRoles.Keys)
            ? null
            : OutcomeMalformedConfig;
    }

    private static SanitizedModelRoleStatusEvalRow ExceptionRow(Exception exception) =>
        exception switch
        {
            ProviderException => Rejected(OutcomeProviderUnavailable, "provider_error"),
            _ => Rejected(OutcomeError, "model_role_status_error")
        };

    private static SanitizedModelRoleStatusEvalRow Blocked(
        string outcomeCode,
        IReadOnlyList<SanitizedModelRoleRow> roles,
        ModelRoleKind? activeRole,
        bool liveProviderEnabled,
        bool fallbackEnabled) =>
        new(
            RejectedRowName,
            RejectedRowPacketId,
            false,
            outcomeCode,
            null,
            activeRole,
            roles,
            liveProviderEnabled,
            fallbackEnabled,
            null,
            null,
            null,
            null);

    private static SanitizedModelRoleStatusEvalRow Rejected(
        string outcomeCode,
        string? errorCode = null) =>
        new(
            RejectedRowName,
            RejectedRowPacketId,
            false,
            outcomeCode,
            errorCode,
            null,
            [],
            false,
            false,
            null,
            null,
            null,
            null);

    private static string SanitizeStatusCode(string statusCode) =>
        statusCode switch
        {
            "role_available" => statusCode,
            "live_provider_disabled" => statusCode,
            "fallback_disabled" => statusCode,
            "offline_deterministic" => statusCode,
            _ => "role_status_unspecified"
        };

    private static bool IsSupportedRole(ModelRoleKind role) =>
        Enum.IsDefined(role);

    private static bool IsSupportedMode(ModelRoleProviderMode mode) =>
        Enum.IsDefined(mode);

    private static bool IsSupportedAvailability(ModelRoleAvailability availability) =>
        Enum.IsDefined(availability);
}
