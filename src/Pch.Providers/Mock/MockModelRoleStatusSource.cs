using Pch.Providers.Errors;
using Pch.Providers.ModelRoles;

namespace Pch.Providers.Mock;

public sealed class MockModelRoleStatusSource : IModelRoleStatusSource
{
    public const string ProviderName = "mock-model-role-status";
    public const string ModelName = "mock-model-role-deterministic";

    private readonly MockModelRoleStatusBehavior _behavior;

    public MockModelRoleStatusSource(MockModelRoleStatusBehavior behavior = MockModelRoleStatusBehavior.Ready)
    {
        _behavior = behavior;
    }

    public Task<ModelRoleStatusResult> GetStatusAsync(
        ModelRoleStatusPacket packet,
        ModelRoleStatusOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return _behavior switch
        {
            MockModelRoleStatusBehavior.ProviderUnavailable => Task.FromException<ModelRoleStatusResult>(
                new ProviderUnavailableException(ProviderName, "Mock model role provider unavailable.")),
            MockModelRoleStatusBehavior.PacketMismatch => Task.FromResult(CreateResult(
                packet,
                "RAW_PACKET_ID_SHOULD_NOT_PERSIST",
                ModelRoleStatusResultKind.Ready)),
            MockModelRoleStatusBehavior.MalformedConfig => Task.FromResult(CreateResult(
                packet,
                packet.PacketId,
                ModelRoleStatusResultKind.MalformedConfig)),
            MockModelRoleStatusBehavior.UnknownRole => Task.FromResult(CreateResult(
                packet,
                packet.PacketId,
                ModelRoleStatusResultKind.Ready,
                roleOverride: (ModelRoleKind)999)),
            MockModelRoleStatusBehavior.RawStatusCode => Task.FromResult(CreateResult(
                packet,
                packet.PacketId,
                ModelRoleStatusResultKind.Ready,
                statusCodeOverride: "RAW_PROVIDER_PAYLOAD_SHOULD_NOT_PERSIST")),
            MockModelRoleStatusBehavior.LiveProviderBlocked => Task.FromResult(CreateResult(
                packet,
                packet.PacketId,
                ModelRoleStatusResultKind.LiveProviderBlocked)),
            MockModelRoleStatusBehavior.FallbackDisabled => Task.FromResult(CreateResult(
                packet,
                packet.PacketId,
                ModelRoleStatusResultKind.FallbackDisabled)),
            _ => Task.FromResult(CreateResult(
                packet,
                packet.PacketId,
                ModelRoleStatusResultKind.Ready))
        };
    }

    private static ModelRoleStatusResult CreateResult(
        ModelRoleStatusPacket packet,
        string packetId,
        ModelRoleStatusResultKind kind,
        ModelRoleKind? roleOverride = null,
        string? statusCodeOverride = null)
    {
        var roles = packet.Roles
            .Select(role => new ModelRoleStatusItem(
                roleOverride ?? role.Role,
                role.Mode,
                AvailabilityFor(kind, role),
                statusCodeOverride ?? StatusCodeFor(kind, role)))
            .ToArray();

        return new ModelRoleStatusResult(
            packetId,
            kind,
            packet.PreferredRole,
            roles,
            LiveProviderEnabled: false,
            FallbackEnabled: kind != ModelRoleStatusResultKind.FallbackDisabled && packet.AllowFallback,
            ResponseContentLength: 256,
            ProviderName,
            ModelName,
            $"mock-role-status-{packet.PacketId}");
    }

    private static ModelRoleAvailability AvailabilityFor(
        ModelRoleStatusResultKind kind,
        ModelRoleRequest role)
    {
        if (kind == ModelRoleStatusResultKind.MalformedConfig)
        {
            return ModelRoleAvailability.Malformed;
        }

        if (kind == ModelRoleStatusResultKind.LiveProviderBlocked &&
            role.Mode is ModelRoleProviderMode.HostedSmallModel or ModelRoleProviderMode.HostedStrongModel)
        {
            return ModelRoleAvailability.Blocked;
        }

        if (role.Mode == ModelRoleProviderMode.LiveProviderDisabled)
        {
            return ModelRoleAvailability.Disabled;
        }

        return role.IsEnabled
            ? ModelRoleAvailability.Available
            : ModelRoleAvailability.Disabled;
    }

    private static string StatusCodeFor(
        ModelRoleStatusResultKind kind,
        ModelRoleRequest role)
    {
        if (kind == ModelRoleStatusResultKind.FallbackDisabled)
        {
            return "fallback_disabled";
        }

        if (role.Mode == ModelRoleProviderMode.LiveProviderDisabled ||
            kind == ModelRoleStatusResultKind.LiveProviderBlocked)
        {
            return "live_provider_disabled";
        }

        return role.Role == ModelRoleKind.DeterministicOffline
            ? "offline_deterministic"
            : "role_available";
    }
}

public enum MockModelRoleStatusBehavior
{
    Ready,
    LiveProviderBlocked,
    MalformedConfig,
    FallbackDisabled,
    ProviderUnavailable,
    PacketMismatch,
    UnknownRole,
    RawStatusCode
}
