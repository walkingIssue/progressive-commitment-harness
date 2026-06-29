using Pch.Providers.AvailabilityPreview;
using Pch.Providers.Errors;

namespace Pch.Providers.Mock;

public sealed class MockAvailabilityPreviewAdapter : IAvailabilityPreviewAdapter
{
    public const string ProviderName = "mock-availability-preview";
    public const string ModelName = "mock-availability-preview-deterministic";

    private readonly MockAvailabilityPreviewBehavior _behavior;

    public MockAvailabilityPreviewAdapter(MockAvailabilityPreviewBehavior behavior = MockAvailabilityPreviewBehavior.QuoteReady)
    {
        _behavior = behavior;
    }

    public Task<AvailabilityPreviewResult> PreviewAsync(
        AvailabilityPreviewPacket packet,
        AvailabilityPreviewOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return _behavior switch
        {
            MockAvailabilityPreviewBehavior.ProviderTimeout => Task.FromException<AvailabilityPreviewResult>(
                new TimeoutException("Mock availability preview provider timed out.")),
            MockAvailabilityPreviewBehavior.ProviderUnavailable => Task.FromException<AvailabilityPreviewResult>(
                new ProviderUnavailableException(ProviderName, "Mock availability preview provider unavailable.")),
            MockAvailabilityPreviewBehavior.PacketMismatch => Task.FromResult(CreateResult(
                packet,
                "RAW_PACKET_ID_SHOULD_NOT_PERSIST",
                AvailabilityPreviewResultKind.QuoteReady,
                AvailabilityPreviewCandidateStatus.QuoteReady)),
            MockAvailabilityPreviewBehavior.MalformedResult => Task.FromResult(CreateResult(
                packet,
                packet.PacketId,
                AvailabilityPreviewResultKind.Malformed,
                AvailabilityPreviewCandidateStatus.Unsupported)),
            MockAvailabilityPreviewBehavior.UnsupportedResult => Task.FromResult(CreateResult(
                packet,
                packet.PacketId,
                AvailabilityPreviewResultKind.Unsupported,
                AvailabilityPreviewCandidateStatus.Unsupported)),
            MockAvailabilityPreviewBehavior.UnsupportedCategory => Task.FromResult(CreateResult(
                packet,
                packet.PacketId,
                AvailabilityPreviewResultKind.QuoteReady,
                AvailabilityPreviewCandidateStatus.QuoteReady,
                categoryOverride: (AvailabilityPreviewCategory)999)),
            MockAvailabilityPreviewBehavior.CandidateMismatch => Task.FromResult(CreateResult(
                packet,
                packet.PacketId,
                AvailabilityPreviewResultKind.QuoteReady,
                AvailabilityPreviewCandidateStatus.QuoteReady,
                candidateIdOverride: "RAW_CANDIDATE_ID_SHOULD_NOT_PERSIST")),
            MockAvailabilityPreviewBehavior.Unavailable => Task.FromResult(CreateResult(
                packet,
                packet.PacketId,
                AvailabilityPreviewResultKind.Unavailable,
                AvailabilityPreviewCandidateStatus.Unavailable)),
            _ => Task.FromResult(CreateResult(
                packet,
                packet.PacketId,
                AvailabilityPreviewResultKind.QuoteReady,
                AvailabilityPreviewCandidateStatus.QuoteReady))
        };
    }

    private static AvailabilityPreviewResult CreateResult(
        AvailabilityPreviewPacket packet,
        string packetId,
        AvailabilityPreviewResultKind kind,
        AvailabilityPreviewCandidateStatus status,
        AvailabilityPreviewCategory? categoryOverride = null,
        string? candidateIdOverride = null)
    {
        var candidates = packet.Candidates.Select((candidate, index) => new AvailabilityPreviewCandidateResult(
                candidate.SlotId,
                candidateIdOverride ?? candidate.CandidateId,
                categoryOverride ?? candidate.Category,
                status,
                QuoteAmount: kind == AvailabilityPreviewResultKind.QuoteReady ? 100 + index : null,
                Currency: kind == AvailabilityPreviewResultKind.QuoteReady ? "USD" : null,
                ExpiresAt: kind == AvailabilityPreviewResultKind.QuoteReady
                    ? DateTimeOffset.Parse("2026-07-01T12:00:00Z")
                    : null,
                ProviderQuoteReference: "RAW_PROVIDER_QUOTE_REFERENCE_SHOULD_NOT_PERSIST"))
            .ToArray();

        return new AvailabilityPreviewResult(
            packetId,
            kind,
            candidates,
            ResponseContentLength: 512,
            ProviderName,
            ModelName,
            $"mock-availability-{packet.PacketId}");
    }
}

public enum MockAvailabilityPreviewBehavior
{
    QuoteReady,
    Unavailable,
    PacketMismatch,
    ProviderTimeout,
    MalformedResult,
    UnsupportedResult,
    UnsupportedCategory,
    CandidateMismatch,
    ProviderUnavailable
}
