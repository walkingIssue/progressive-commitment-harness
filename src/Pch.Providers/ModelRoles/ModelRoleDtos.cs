namespace Pch.Providers.ModelRoles;

public sealed record ModelRoleStatusPacket(
    string PacketId,
    IReadOnlyList<ModelRoleRequest> Roles,
    ModelRoleKind PreferredRole,
    bool AllowFallback,
    string Locale,
    string? ContextDigest = null);

public sealed record ModelRoleRequest(
    ModelRoleKind Role,
    ModelRoleProviderMode Mode,
    bool IsEnabled,
    bool IsDefault);

public sealed record ModelRoleStatusOptions(
    string? Model = null);

public sealed record ModelRoleStatusResult(
    string PacketId,
    ModelRoleStatusResultKind Kind,
    ModelRoleKind? ActiveRole,
    IReadOnlyList<ModelRoleStatusItem> Roles,
    bool LiveProviderEnabled,
    bool FallbackEnabled,
    int ResponseContentLength,
    string Provider,
    string Model,
    string? RequestId);

public sealed record ModelRoleStatusItem(
    ModelRoleKind Role,
    ModelRoleProviderMode Mode,
    ModelRoleAvailability Availability,
    string StatusCode);

public enum ModelRoleKind
{
    DeterministicOffline,
    SmallModel,
    StrongModel,
    LiveProviderDisabled
}

public enum ModelRoleProviderMode
{
    OfflineDeterministic,
    HostedSmallModel,
    HostedStrongModel,
    LiveProviderDisabled
}

public enum ModelRoleAvailability
{
    Available,
    Disabled,
    Blocked,
    Malformed
}

public enum ModelRoleStatusResultKind
{
    Ready,
    LiveProviderBlocked,
    FallbackDisabled,
    MalformedConfig
}

public sealed record ModelRoleStatusEvalCase(
    string Name,
    ModelRoleStatusPacket Packet);

public sealed record SanitizedModelRoleStatusEvalRow(
    string Name,
    string PacketId,
    bool Passed,
    string OutcomeCode,
    string? ErrorCode,
    ModelRoleKind? ActiveRole,
    IReadOnlyList<SanitizedModelRoleRow> Roles,
    bool LiveProviderEnabled,
    bool FallbackEnabled,
    int? ResponseContentLength,
    string? Provider,
    string? Model,
    string? RequestId);

public sealed record SanitizedModelRoleRow(
    ModelRoleKind Role,
    ModelRoleProviderMode Mode,
    ModelRoleAvailability Availability,
    string StatusCode);
