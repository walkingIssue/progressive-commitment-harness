namespace Pch.Providers.Media;

public interface IMediaRegistrySource
{
    Task<MediaRegistryResult> ResolveAsync(
        MediaRegistryPacket packet,
        MediaRegistryOptions? options = null,
        CancellationToken cancellationToken = default);
}

public interface IGuardedMediaMetadataClient
{
    MediaSourceClass SourceClass { get; }

    bool IsEnabled { get; }

    Task<MediaRegistryResult> ResolveAsync(
        MediaRegistryPacket packet,
        MediaRegistryOptions? options = null,
        CancellationToken cancellationToken = default);
}

public sealed record GuardedMediaMetadataClientDescriptor(
    MediaSourceClass SourceClass,
    string ProviderName,
    bool IsEnabledByDefault,
    bool RequiresApiKey,
    bool RequiresAttribution,
    string GuardPolicy);

public static class GuardedMediaMetadataClients
{
    public static IReadOnlyList<GuardedMediaMetadataClientDescriptor> Defaults { get; } =
    [
        new(MediaSourceClass.Pexels, "pexels", false, true, true, "key, rate-limit, timeout, malformed-response, and attribution guarded"),
        new(MediaSourceClass.Unsplash, "unsplash", false, true, true, "key, rate-limit, timeout, malformed-response, and attribution guarded"),
        new(MediaSourceClass.Openverse, "openverse", false, false, true, "health, timeout, malformed-response, and license guarded"),
        new(MediaSourceClass.Wikimedia, "wikimedia", false, false, true, "health, timeout, malformed-response, and license guarded")
    ];
}
