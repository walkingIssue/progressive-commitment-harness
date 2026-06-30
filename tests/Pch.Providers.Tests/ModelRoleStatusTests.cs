using Pch.Providers.Errors;
using Pch.Providers.Mock;
using Pch.Providers.ModelRoles;
using Xunit;

namespace Pch.Providers.Tests;

public sealed class ModelRoleStatusTests
{
    private const string RawPrompt = "RAW_PROMPT_TEXT_SHOULD_NOT_PERSIST";
    private const string RawProviderPayload = "RAW_PROVIDER_PAYLOAD_SHOULD_NOT_PERSIST";
    private const string Credential = "sk-credential-sentinel-should-not-persist";
    private const string ApprovalToken = "APPROVAL_TOKEN_SHOULD_NOT_PERSIST";
    private const string RawError = "RAW_EXCEPTION_MESSAGE_SHOULD_NOT_PERSIST";
    private const string SecretSentinel = "SECRET_SENTINEL_SHOULD_NOT_PERSIST";

    private static readonly string[] SensitiveSentinels =
    [
        RawPrompt,
        RawProviderPayload,
        Credential,
        ApprovalToken,
        RawError,
        SecretSentinel
    ];

    [Fact]
    public async Task ReadyRowsExposeOfflineFirstRolePostureWithoutSecrets()
    {
        var evaluator = new ModelRoleStatusEvaluator(new MockModelRoleStatusSource());

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new ModelRoleStatusEvalCase("model-role-ready", CreatePacket())]));

        Assert.True(row.Passed);
        Assert.Equal("model-role-ready", row.Name);
        Assert.Equal("packet-model-role", row.PacketId);
        Assert.Equal(ModelRoleStatusEvaluator.OutcomeReady, row.OutcomeCode);
        Assert.Null(row.ErrorCode);
        Assert.Equal(ModelRoleKind.DeterministicOffline, row.ActiveRole);
        Assert.False(row.LiveProviderEnabled);
        Assert.False(row.FallbackEnabled);
        Assert.NotNull(row.ResponseContentLength);
        Assert.Equal(MockModelRoleStatusSource.ProviderName, row.Provider);
        Assert.Equal(MockModelRoleStatusSource.ModelName, row.Model);
        Assert.Contains(row.Roles, role => role is
        {
            Role: ModelRoleKind.DeterministicOffline,
            Mode: ModelRoleProviderMode.OfflineDeterministic,
            Availability: ModelRoleAvailability.Available,
            StatusCode: "offline_deterministic"
        });
        Assert.Contains(row.Roles, role => role is
        {
            Role: ModelRoleKind.LiveProviderDisabled,
            Mode: ModelRoleProviderMode.LiveProviderDisabled,
            Availability: ModelRoleAvailability.Disabled,
            StatusCode: "live_provider_disabled"
        });

        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task LiveProviderBlockedRowsUseFixedIdentifiersAndSafeRoleStatuses()
    {
        var packet = CreatePacket() with
        {
            PacketId = $"{RawPrompt}-{RawProviderPayload}-{SecretSentinel}",
            ContextDigest = $"{Credential}-{ApprovalToken}"
        };
        var evaluator = new ModelRoleStatusEvaluator(new MockModelRoleStatusSource(
            MockModelRoleStatusBehavior.LiveProviderBlocked));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new ModelRoleStatusEvalCase($"{RawPrompt}-{Credential}", packet)]));

        AssertBlockedRow(row, ModelRoleStatusEvaluator.OutcomeLiveProviderBlocked);
        Assert.Equal(ModelRoleKind.DeterministicOffline, row.ActiveRole);
        Assert.Contains(row.Roles, role => role is
        {
            Role: ModelRoleKind.SmallModel,
            Availability: ModelRoleAvailability.Blocked,
            StatusCode: "live_provider_disabled"
        });
        Assert.Contains(row.Roles, role => role is
        {
            Role: ModelRoleKind.StrongModel,
            Availability: ModelRoleAvailability.Blocked,
            StatusCode: "live_provider_disabled"
        });

        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task FallbackDisabledRowsUseFixedIdentifiersWithoutProviderMetadata()
    {
        var packet = CreatePacket(allowFallback: true) with
        {
            PacketId = $"{RawPrompt}-{SecretSentinel}",
            ContextDigest = $"{RawProviderPayload}-{Credential}"
        };
        var evaluator = new ModelRoleStatusEvaluator(new MockModelRoleStatusSource(
            MockModelRoleStatusBehavior.FallbackDisabled));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new ModelRoleStatusEvalCase($"{RawPrompt}-{Credential}", packet)]));

        AssertBlockedRow(row, ModelRoleStatusEvaluator.OutcomeFallbackDisabled);
        Assert.False(row.FallbackEnabled);
        Assert.All(row.Roles, role => Assert.Equal("fallback_disabled", role.StatusCode));
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Theory]
    [InlineData(MockModelRoleStatusBehavior.MalformedConfig, "model_role_malformed_config", null)]
    [InlineData(MockModelRoleStatusBehavior.ProviderUnavailable, "model_role_provider_unavailable", "provider_error")]
    [InlineData(MockModelRoleStatusBehavior.PacketMismatch, "model_role_packet_mismatch", null)]
    [InlineData(MockModelRoleStatusBehavior.UnknownRole, "model_role_malformed_config", null)]
    public async Task BlockedMockBehaviorsUseFixedCodesAndNoRawValues(
        MockModelRoleStatusBehavior behavior,
        string expectedOutcome,
        string? expectedErrorCode)
    {
        var evaluator = new ModelRoleStatusEvaluator(new MockModelRoleStatusSource(behavior));
        var packet = CreatePacket() with
        {
            PacketId = $"{RawPrompt}-{RawProviderPayload}-{SecretSentinel}",
            ContextDigest = $"{Credential}-{ApprovalToken}"
        };

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new ModelRoleStatusEvalCase($"{RawPrompt}-{Credential}", packet)]));

        Assert.False(row.Passed);
        Assert.Equal(ModelRoleStatusEvaluator.RejectedRowName, row.Name);
        Assert.Equal(ModelRoleStatusEvaluator.RejectedRowPacketId, row.PacketId);
        Assert.Equal(expectedOutcome, row.OutcomeCode);
        Assert.Equal(expectedErrorCode, row.ErrorCode);
        Assert.Null(row.ResponseContentLength);
        Assert.Null(row.Provider);
        Assert.Null(row.Model);
        Assert.Null(row.RequestId);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
        Assert.DoesNotContain("RAW_PACKET_ID_SHOULD_NOT_PERSIST", SanitizedEvalArtifactAssert.Serialize(row));
    }

    [Fact]
    public async Task RawProviderStatusCodesAreSanitizedInReadyRows()
    {
        var evaluator = new ModelRoleStatusEvaluator(new MockModelRoleStatusSource(
            MockModelRoleStatusBehavior.RawStatusCode));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new ModelRoleStatusEvalCase("raw-status-code", CreatePacket())]));

        Assert.True(row.Passed);
        Assert.All(row.Roles, role => Assert.Equal("role_status_unspecified", role.StatusCode));
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Theory]
    [MemberData(nameof(MalformedPackets))]
    public async Task MalformedPacketsBlockBeforeSourceInvocation(ModelRoleStatusPacket packet)
    {
        var source = new CountingModelRoleStatusSource();
        var evaluator = new ModelRoleStatusEvaluator(source);

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new ModelRoleStatusEvalCase($"{RawPrompt}-{Credential}", packet)]));

        AssertRejectedRow(row, ModelRoleStatusEvaluator.OutcomeMalformedConfig);
        Assert.Equal(0, source.CallCount);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task NullResultAndNullResultRolesAreMalformed()
    {
        var nullResultRow = Assert.Single(await new ModelRoleStatusEvaluator(new NullModelRoleStatusSource())
            .EvaluateAsync([new ModelRoleStatusEvalCase("null-result", CreatePacket())]));
        var nullRolesRow = Assert.Single(await new ModelRoleStatusEvaluator(new StaticModelRoleStatusSource(
                CreateResult(CreatePacket()) with { Roles = null!, Provider = Credential }))
            .EvaluateAsync([new ModelRoleStatusEvalCase("null-roles", CreatePacket())]));

        AssertRejectedRow(nullResultRow, ModelRoleStatusEvaluator.OutcomeMalformedConfig);
        AssertRejectedRow(nullRolesRow, ModelRoleStatusEvaluator.OutcomeMalformedConfig);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(nullResultRow, SensitiveSentinels);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(nullRolesRow, SensitiveSentinels);
    }

    [Fact]
    public async Task SourceExceptionsUseFixedCodesWithoutRawExceptionText()
    {
        var evaluator = new ModelRoleStatusEvaluator(new ThrowingModelRoleStatusSource(
            new InvalidOperationException($"{RawError} {RawPrompt} {RawProviderPayload} {Credential} {ApprovalToken}")));
        var packet = CreatePacket() with
        {
            PacketId = $"{RawPrompt}-{SecretSentinel}",
            ContextDigest = RawProviderPayload
        };

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new ModelRoleStatusEvalCase($"{RawPrompt}-{Credential}", packet)]));

        AssertRejectedRow(row, ModelRoleStatusEvaluator.OutcomeError, "model_role_status_error");
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task ProviderExceptionsUseFixedProviderUnavailableCode()
    {
        var evaluator = new ModelRoleStatusEvaluator(new ThrowingModelRoleStatusSource(
            new ProviderUnavailableException("provider", $"{RawError} {Credential}")));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new ModelRoleStatusEvalCase($"{RawPrompt}-{Credential}", CreatePacket())]));

        AssertRejectedRow(row, ModelRoleStatusEvaluator.OutcomeProviderUnavailable, "provider_error");
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    public static TheoryData<ModelRoleStatusPacket> MalformedPackets() =>
        new()
        {
            CreatePacket() with { PacketId = $"{RawPrompt}-{SecretSentinel}", Roles = [] },
            CreatePacket() with { PacketId = $"{RawPrompt}-{SecretSentinel}", Roles = null! },
            CreatePacket() with { PacketId = $"{RawPrompt}-{SecretSentinel}", Roles = [null!] },
            CreatePacket() with
            {
                PacketId = $"{RawPrompt}-{SecretSentinel}",
                Roles = [new ModelRoleRequest((ModelRoleKind)999, ModelRoleProviderMode.OfflineDeterministic, true, true)]
            },
            CreatePacket() with
            {
                PacketId = $"{RawPrompt}-{SecretSentinel}",
                Roles = [new ModelRoleRequest(ModelRoleKind.DeterministicOffline, (ModelRoleProviderMode)999, true, true)]
            },
            CreatePacket() with
            {
                PacketId = $"{RawPrompt}-{SecretSentinel}",
                PreferredRole = ModelRoleKind.StrongModel,
                Roles = [new ModelRoleRequest(ModelRoleKind.DeterministicOffline, ModelRoleProviderMode.OfflineDeterministic, true, true)]
            },
            CreatePacket() with
            {
                PacketId = $"{RawPrompt}-{SecretSentinel}",
                Roles =
                [
                    new ModelRoleRequest(ModelRoleKind.DeterministicOffline, ModelRoleProviderMode.OfflineDeterministic, true, true),
                    new ModelRoleRequest(ModelRoleKind.DeterministicOffline, ModelRoleProviderMode.OfflineDeterministic, true, false)
                ]
            },
            CreatePacket() with
            {
                PacketId = $"{RawPrompt}-{SecretSentinel}",
                Roles =
                [
                    new ModelRoleRequest(ModelRoleKind.DeterministicOffline, ModelRoleProviderMode.OfflineDeterministic, true, true),
                    new ModelRoleRequest(ModelRoleKind.SmallModel, ModelRoleProviderMode.HostedSmallModel, true, true)
                ]
            }
        };

    private static ModelRoleStatusPacket CreatePacket(bool allowFallback = false) =>
        new(
            "packet-model-role",
            [
                new ModelRoleRequest(ModelRoleKind.DeterministicOffline, ModelRoleProviderMode.OfflineDeterministic, true, true),
                new ModelRoleRequest(ModelRoleKind.SmallModel, ModelRoleProviderMode.HostedSmallModel, true, false),
                new ModelRoleRequest(ModelRoleKind.StrongModel, ModelRoleProviderMode.HostedStrongModel, true, false),
                new ModelRoleRequest(ModelRoleKind.LiveProviderDisabled, ModelRoleProviderMode.LiveProviderDisabled, false, false)
            ],
            ModelRoleKind.DeterministicOffline,
            allowFallback,
            "en-US",
            $"{RawPrompt} {RawProviderPayload} {Credential} {ApprovalToken} {SecretSentinel}");

    private static ModelRoleStatusResult CreateResult(ModelRoleStatusPacket packet) =>
        new(
            packet.PacketId,
            ModelRoleStatusResultKind.Ready,
            packet.PreferredRole,
            packet.Roles.Select(role => new ModelRoleStatusItem(
                    role.Role,
                    role.Mode,
                    role.IsEnabled ? ModelRoleAvailability.Available : ModelRoleAvailability.Disabled,
                    "role_available"))
                .ToArray(),
            LiveProviderEnabled: false,
            FallbackEnabled: packet.AllowFallback,
            ResponseContentLength: 128,
            "provider-safe-unless-rejected",
            "model-safe-unless-rejected",
            "request-safe-unless-rejected");

    private static void AssertBlockedRow(SanitizedModelRoleStatusEvalRow row, string expectedOutcome)
    {
        Assert.False(row.Passed);
        Assert.Equal(ModelRoleStatusEvaluator.RejectedRowName, row.Name);
        Assert.Equal(ModelRoleStatusEvaluator.RejectedRowPacketId, row.PacketId);
        Assert.Equal(expectedOutcome, row.OutcomeCode);
        Assert.Null(row.ErrorCode);
        Assert.NotEmpty(row.Roles);
        Assert.Null(row.ResponseContentLength);
        Assert.Null(row.Provider);
        Assert.Null(row.Model);
        Assert.Null(row.RequestId);
    }

    private static void AssertRejectedRow(
        SanitizedModelRoleStatusEvalRow row,
        string expectedOutcome,
        string? expectedErrorCode = null)
    {
        Assert.False(row.Passed);
        Assert.Equal(ModelRoleStatusEvaluator.RejectedRowName, row.Name);
        Assert.Equal(ModelRoleStatusEvaluator.RejectedRowPacketId, row.PacketId);
        Assert.Equal(expectedOutcome, row.OutcomeCode);
        Assert.Equal(expectedErrorCode, row.ErrorCode);
        Assert.Null(row.ActiveRole);
        Assert.Empty(row.Roles);
        Assert.False(row.LiveProviderEnabled);
        Assert.False(row.FallbackEnabled);
        Assert.Null(row.ResponseContentLength);
        Assert.Null(row.Provider);
        Assert.Null(row.Model);
        Assert.Null(row.RequestId);
    }

    private sealed class StaticModelRoleStatusSource(ModelRoleStatusResult result) : IModelRoleStatusSource
    {
        public Task<ModelRoleStatusResult> GetStatusAsync(
            ModelRoleStatusPacket packet,
            ModelRoleStatusOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class NullModelRoleStatusSource : IModelRoleStatusSource
    {
        public Task<ModelRoleStatusResult> GetStatusAsync(
            ModelRoleStatusPacket packet,
            ModelRoleStatusOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ModelRoleStatusResult>(null!);
    }

    private sealed class CountingModelRoleStatusSource : IModelRoleStatusSource
    {
        public int CallCount { get; private set; }

        public Task<ModelRoleStatusResult> GetStatusAsync(
            ModelRoleStatusPacket packet,
            ModelRoleStatusOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(CreateResult(packet));
        }
    }

    private sealed class ThrowingModelRoleStatusSource(Exception exception) : IModelRoleStatusSource
    {
        public Task<ModelRoleStatusResult> GetStatusAsync(
            ModelRoleStatusPacket packet,
            ModelRoleStatusOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ModelRoleStatusResult>(exception);
    }
}
