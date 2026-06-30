using Pch.Providers.Errors;
using Pch.Providers.Media;
using Pch.Providers.Mock;
using Xunit;

namespace Pch.Providers.Tests;

public sealed class MediaRegistryTests
{
    private const string RawSearchQuery = "RAW_SEARCH_QUERY_SHOULD_NOT_PERSIST";
    private const string RawProviderPayload = "RAW_PROVIDER_PAYLOAD_SHOULD_NOT_PERSIST";
    private const string ApiKey = "MEDIA_API_KEY_SHOULD_NOT_PERSIST";
    private const string RawImageUrl = "RAW_IMAGE_URL_SHOULD_NOT_PERSIST";
    private const string RawException = "RAW_EXCEPTION_TEXT_SHOULD_NOT_PERSIST";
    private const string CandidateDisplayValue = "CANDIDATE_DISPLAY_VALUE_SHOULD_NOT_PERSIST";
    private const string SecretSentinel = "SECRET_SENTINEL_SHOULD_NOT_PERSIST";

    private static readonly string[] SensitiveSentinels =
    [
        RawSearchQuery,
        RawProviderPayload,
        ApiKey,
        RawImageUrl,
        RawException,
        CandidateDisplayValue,
        SecretSentinel,
        "RAW_SOURCE_URL_SHOULD_NOT_PERSIST",
        "RAW_THUMB_URL_SHOULD_NOT_PERSIST",
        "RAW_ALT_TEXT_SHOULD_NOT_PERSIST"
    ];

    [Fact]
    public async Task MockSourceReturnsDeterministicMediaForAllCandidateCategories()
    {
        var source = new MockMediaRegistrySource();

        var result = await source.ResolveAsync(CreatePacket());

        Assert.Equal("packet-media", result.PacketId);
        Assert.Equal(MockMediaRegistrySource.ProviderName, result.Provider);
        Assert.Equal(MockMediaRegistrySource.ModelName, result.Model);
        Assert.Equal(6, result.CandidateMedia.Count);
        Assert.Contains(result.CandidateMedia, mapping => mapping.Category == MediaCandidateCategory.Flight);
        Assert.Contains(result.CandidateMedia, mapping => mapping.Category == MediaCandidateCategory.Lodging);
        Assert.Contains(result.CandidateMedia, mapping => mapping.Category == MediaCandidateCategory.Activity);
        Assert.Contains(result.CandidateMedia, mapping => mapping.Category == MediaCandidateCategory.Dining);
        Assert.Contains(result.CandidateMedia, mapping => mapping.Category == MediaCandidateCategory.Transit);
        Assert.Contains(result.CandidateMedia, mapping => mapping.Category == MediaCandidateCategory.Downtime);
        Assert.All(result.CandidateMedia, mapping =>
        {
            var asset = Assert.Single(mapping.Assets);
            Assert.StartsWith("media-", asset.MediaId, StringComparison.Ordinal);
            Assert.True(asset.Width > 0);
            Assert.True(asset.Height > 0);
            Assert.NotEqual(MediaLicenseClass.Unknown, asset.License.LicenseClass);
        });
    }

    [Fact]
    public async Task AcceptedRowsPersistOnlyTrustedCandidateIdsMediaMetadataAndAttribution()
    {
        var evaluator = new MediaRegistryEvaluator(new MockMediaRegistrySource());

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new MediaRegistryEvalCase("media-ready", CreatePacket())]));

        Assert.True(row.Passed);
        Assert.Equal("media-ready", row.Name);
        Assert.Equal("packet-media", row.PacketId);
        Assert.Equal(MediaRegistryEvaluator.OutcomeAccepted, row.OutcomeCode);
        Assert.Null(row.ErrorCode);
        Assert.Equal(6, row.CandidateCount);
        Assert.Equal(6, row.TotalMediaAssetCount);
        Assert.Equal(MockMediaRegistrySource.ProviderName, row.Provider);
        Assert.Equal(MockMediaRegistrySource.ModelName, row.Model);
        Assert.NotNull(row.ResponseContentLength);
        Assert.Contains(row.Candidates, candidate => candidate is
        {
            SlotId: "slot-dining",
            CandidateId: "candidate-dining",
            Category: MediaCandidateCategory.Dining
        });
        Assert.Contains(row.Candidates.SelectMany(candidate => candidate.Assets), asset =>
            asset.SourceClass == MediaSourceClass.Pexels &&
            asset.LicenseClass == MediaLicenseClass.FreeCommercial &&
            asset.Width == 1200 &&
            asset.Height == 800 &&
            asset.AuthorName == "Mock Author 4");

        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
        var serialized = SanitizedEvalArtifactAssert.Serialize(row);
        Assert.DoesNotContain("https://images.example", serialized);
        Assert.DoesNotContain("soft_nature", serialized);
    }

    [Theory]
    [InlineData(MockMediaRegistryBehavior.PacketMismatch, "media_registry_packet_mismatch", null)]
    [InlineData(MockMediaRegistryBehavior.CandidateMismatch, "media_registry_candidate_mismatch", null)]
    [InlineData(MockMediaRegistryBehavior.UnsupportedSource, "media_registry_unsupported_source", null)]
    [InlineData(MockMediaRegistryBehavior.UnsupportedLicense, "media_registry_unsupported_license", null)]
    [InlineData(MockMediaRegistryBehavior.MalformedResult, "media_registry_malformed_result", null)]
    [InlineData(MockMediaRegistryBehavior.ProviderTimeout, "media_registry_timeout", "timeout")]
    [InlineData(MockMediaRegistryBehavior.ProviderUnavailable, "media_registry_provider_unavailable", "provider_error")]
    public async Task BlockedMockBehaviorsUseFixedCodesWithoutRawProviderValues(
        MockMediaRegistryBehavior behavior,
        string expectedOutcome,
        string? expectedErrorCode)
    {
        var evaluator = new MediaRegistryEvaluator(new MockMediaRegistrySource(behavior));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new MediaRegistryEvalCase($"{RawSearchQuery}-{ApiKey}", CreatePacket())]));

        AssertRejected(row, expectedOutcome, expectedErrorCode);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
        Assert.DoesNotContain("RAW_PACKET_ID_SHOULD_NOT_PERSIST", SanitizedEvalArtifactAssert.Serialize(row));
        Assert.DoesNotContain("RAW_CANDIDATE_ID_SHOULD_NOT_PERSIST", SanitizedEvalArtifactAssert.Serialize(row));
    }

    [Fact]
    public async Task MissingResultCandidateBlocksWithoutPersistingPartialMediaOrProviderMetadata()
    {
        var packet = CreatePacket();
        var result = CreateResult(packet) with
        {
            CandidateMedia = CreateResult(packet).CandidateMedia.Take(5).ToArray(),
            Provider = ApiKey,
            Model = RawProviderPayload,
            RequestId = SecretSentinel
        };
        var evaluator = new MediaRegistryEvaluator(new StaticMediaRegistrySource(result));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new MediaRegistryEvalCase($"{RawSearchQuery}-{ApiKey}", packet)]));

        AssertRejected(row, MediaRegistryEvaluator.OutcomeCandidateMismatch);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task DuplicateResultCandidateBlocksWithoutPersistingMediaValues()
    {
        var packet = CreatePacket();
        var first = CreateResult(packet).CandidateMedia[0];
        var result = CreateResult(packet) with
        {
            CandidateMedia = [first, first],
            Provider = ApiKey,
            Model = RawProviderPayload,
            RequestId = SecretSentinel
        };
        var evaluator = new MediaRegistryEvaluator(new StaticMediaRegistrySource(result));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new MediaRegistryEvalCase("duplicate", packet)]));

        AssertRejected(row, MediaRegistryEvaluator.OutcomeCandidateMismatch);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task MalformedPacketsBlockBeforeSourceInvocationWithRedactedIdentifiers()
    {
        var source = new CountingMediaRegistrySource();
        var evaluator = new MediaRegistryEvaluator(source);
        var packet = CreatePacket() with
        {
            PacketId = $"{RawSearchQuery}-{RawProviderPayload}-{SecretSentinel}",
            Candidates = []
        };

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new MediaRegistryEvalCase($"{CandidateDisplayValue}-{ApiKey}", packet)]));

        AssertRejected(row, MediaRegistryEvaluator.OutcomeMalformedPacket);
        Assert.Equal(0, source.CallCount);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task NullResultAndNullResultMappingsAreMalformedWithoutRawValues()
    {
        var nullResultRow = Assert.Single(await new MediaRegistryEvaluator(new NullMediaRegistrySource())
            .EvaluateAsync([new MediaRegistryEvalCase("null-result", CreatePacket())]));
        var nullMappingsRow = Assert.Single(await new MediaRegistryEvaluator(new StaticMediaRegistrySource(
                CreateResult(CreatePacket()) with { CandidateMedia = null!, Provider = ApiKey }))
            .EvaluateAsync([new MediaRegistryEvalCase("null-mappings", CreatePacket())]));

        AssertRejected(nullResultRow, MediaRegistryEvaluator.OutcomeMalformedResult);
        AssertRejected(nullMappingsRow, MediaRegistryEvaluator.OutcomeMalformedResult);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(nullResultRow, SensitiveSentinels);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(nullMappingsRow, SensitiveSentinels);
    }

    [Fact]
    public async Task SourceExceptionsUseFixedCodesWithoutRawExceptionText()
    {
        var evaluator = new MediaRegistryEvaluator(new ThrowingMediaRegistrySource(
            new InvalidOperationException($"{RawException} {RawProviderPayload} {ApiKey} {RawImageUrl}")));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new MediaRegistryEvalCase($"{CandidateDisplayValue}-{ApiKey}", CreatePacket())]));

        AssertRejected(row, MediaRegistryEvaluator.OutcomeError, "media_registry_error");
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public void OptionalProviderDescriptorsAreDisabledAndGuardedByDefault()
    {
        var descriptors = GuardedMediaMetadataClients.Defaults;

        Assert.Contains(descriptors, descriptor => descriptor.SourceClass == MediaSourceClass.Pexels);
        Assert.Contains(descriptors, descriptor => descriptor.SourceClass == MediaSourceClass.Unsplash);
        Assert.Contains(descriptors, descriptor => descriptor.SourceClass == MediaSourceClass.Openverse);
        Assert.Contains(descriptors, descriptor => descriptor.SourceClass == MediaSourceClass.Wikimedia);
        Assert.All(descriptors, descriptor =>
        {
            Assert.False(descriptor.IsEnabledByDefault);
            Assert.Contains("timeout", descriptor.GuardPolicy, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("malformed", descriptor.GuardPolicy, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static MediaRegistryPacket CreatePacket() =>
        new(
            "packet-media",
            [
                new MediaRegistryCandidate("slot-flight", "candidate-flight", MediaCandidateCategory.Flight, "logistics"),
                new MediaRegistryCandidate("slot-lodging", "candidate-lodging", MediaCandidateCategory.Lodging, "calm_morning"),
                new MediaRegistryCandidate("slot-activity", "candidate-activity", MediaCandidateCategory.Activity, "reflective_culture"),
                new MediaRegistryCandidate("slot-dining", "candidate-dining", MediaCandidateCategory.Dining, "lively_food"),
                new MediaRegistryCandidate("slot-transit", "candidate-transit", MediaCandidateCategory.Transit, "logistics"),
                new MediaRegistryCandidate("slot-downtime", "candidate-downtime", MediaCandidateCategory.Downtime, "restorative_downtime")
            ],
            "en-US",
            $"{RawSearchQuery} {RawProviderPayload} {ApiKey} {RawImageUrl} {CandidateDisplayValue} {SecretSentinel}");

    private static MediaRegistryResult CreateResult(MediaRegistryPacket packet) =>
        new(
            packet.PacketId,
            packet.Candidates
                .Select(candidate => new CandidateMediaMapping(
                    candidate.SlotId,
                    candidate.CandidateId,
                    candidate.Category,
                    [
                        new MediaAsset(
                            $"media-{candidate.CandidateId}",
                            new MediaSource("source-safe", MediaSourceClass.Pexels, "pexels", "https://source.example"),
                            new MediaLicense(MediaLicenseClass.FreeCommercial, "Pexels license", "https://license.example", true, true),
                            new MediaAttribution("Safe Author", "https://author.example", "Safe attribution"),
                            1200,
                            800,
                            ImageUrl: $"https://image.example/{RawImageUrl}.jpg",
                            ThumbnailUrl: $"https://thumb.example/{SecretSentinel}.jpg",
                            AltText: CandidateDisplayValue)
                    ]))
                .ToArray(),
            321,
            "provider-safe-unless-rejected",
            "model-safe-unless-rejected",
            "request-safe-unless-rejected");

    private static void AssertRejected(
        SanitizedMediaRegistryEvalRow row,
        string expectedOutcome,
        string? expectedErrorCode = null)
    {
        Assert.False(row.Passed);
        Assert.Equal(MediaRegistryEvaluator.RejectedRowName, row.Name);
        Assert.Equal(MediaRegistryEvaluator.RejectedRowPacketId, row.PacketId);
        Assert.Equal(expectedOutcome, row.OutcomeCode);
        Assert.Equal(expectedErrorCode, row.ErrorCode);
        Assert.Empty(row.Candidates);
        Assert.Equal(0, row.CandidateCount);
        Assert.Equal(0, row.TotalMediaAssetCount);
        Assert.Null(row.ResponseContentLength);
        Assert.Null(row.Provider);
        Assert.Null(row.Model);
        Assert.Null(row.RequestId);
    }

    private sealed class StaticMediaRegistrySource(MediaRegistryResult result) : IMediaRegistrySource
    {
        public Task<MediaRegistryResult> ResolveAsync(
            MediaRegistryPacket packet,
            MediaRegistryOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class NullMediaRegistrySource : IMediaRegistrySource
    {
        public Task<MediaRegistryResult> ResolveAsync(
            MediaRegistryPacket packet,
            MediaRegistryOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<MediaRegistryResult>(null!);
    }

    private sealed class CountingMediaRegistrySource : IMediaRegistrySource
    {
        public int CallCount { get; private set; }

        public Task<MediaRegistryResult> ResolveAsync(
            MediaRegistryPacket packet,
            MediaRegistryOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(CreateResult(packet));
        }
    }

    private sealed class ThrowingMediaRegistrySource(Exception exception) : IMediaRegistrySource
    {
        public Task<MediaRegistryResult> ResolveAsync(
            MediaRegistryPacket packet,
            MediaRegistryOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<MediaRegistryResult>(exception);
    }
}
