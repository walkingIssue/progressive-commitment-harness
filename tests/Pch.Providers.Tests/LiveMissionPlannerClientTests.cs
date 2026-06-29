using System.Net;
using System.Text;
using System.Text.Json;
using Pch.Providers.Errors;
using Pch.Providers.MissionPlanning;
using Pch.Providers.OpenRouter;
using Xunit;

namespace Pch.Providers.Tests;

public sealed class LiveMissionPlannerClientTests
{
    [Fact]
    public async Task OpenRouterBackedPlannerReturnsStructuredMissionResult()
    {
        var handler = new QueueHandler(
            Json(HttpStatusCode.OK, """{"data":{"total_credits":40,"total_usage":1}}"""),
            Json(HttpStatusCode.OK, CompletionEnvelope(SuccessPlannerContent())));
        var planner = CreatePlanner(handler);

        var result = await planner.PlanAsync(CreatePacket("packet-live"));

        Assert.Equal("packet-live", result.PacketId);
        Assert.Equal("vacation", result.MissionKind);
        Assert.Equal(OpenRouterModelCompletionClient.ProviderName, result.Provider);
        Assert.Equal(OpenRouterOptions.DefaultModel, result.Model);
        Assert.Equal("request-live", result.RequestId);
        var field = Assert.Single(result.Fields);
        Assert.Equal("/mission/purpose", field.FieldPath);
        Assert.Equal(MissionProposalSource.UserStated, field.AuthoritySource);
        var commitment = Assert.Single(result.Commitments);
        Assert.Equal(MissionCommitmentPriority.Normal, commitment.CommitmentPriority);
        Assert.Single(result.Constraints);

        var payload = JsonDocument.Parse(handler.RequestBodies[1]).RootElement;
        Assert.Equal(OpenRouterOptions.DefaultModel, payload.GetProperty("model").GetString());
        Assert.Equal(ModelCompletionMissionPlannerClient.JsonSchemaName, payload
            .GetProperty("response_format")
            .GetProperty("json_schema")
            .GetProperty("name")
            .GetString());
        Assert.Equal("Bearer", handler.Requests[1].Headers.Authorization?.Scheme);
        Assert.Equal("unit-test-key", handler.Requests[1].Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task RuntimeEvalRowsForLivePlannerPersistOnlySanitizedMetadata()
    {
        const string promptSentinel = "PROMPT_SENTINEL_SHOULD_NOT_LEAK";
        var handler = new QueueHandler(
            Json(HttpStatusCode.OK, """{"data":{"total_credits":40,"total_usage":1}}"""),
            Json(HttpStatusCode.OK, CompletionEnvelope(SuccessPlannerContent(
                fieldValue: "FIELD_VALUE_SENTINEL_SHOULD_NOT_LEAK",
                commitmentTitle: "COMMITMENT_TITLE_SENTINEL_SHOULD_NOT_LEAK",
                constraintValue: "CONSTRAINT_VALUE_SENTINEL_SHOULD_NOT_LEAK",
                memoryDigest: "MEMORY_DIGEST_SENTINEL_SHOULD_NOT_LEAK"))));
        var runner = new SanitizedMissionPlannerRuntimeEvalRunner(CreatePlanner(handler));

        var row = Assert.Single(await runner.EvaluateAsync(
            [new MissionPlannerEvalCase("live-sanitized", CreatePacket("packet-live", promptSentinel), "vacation")]));

        Assert.True(row.Passed);
        Assert.Equal(MissionPlannerRuntimeBridge.DecodeAccepted, row.DecodeOutcomeCode);
        Assert.Equal(OpenRouterModelCompletionClient.ProviderName, row.Provider);
        Assert.Equal(OpenRouterOptions.DefaultModel, row.Model);
        Assert.Equal("request-live", row.RequestId);
        Assert.Equal(1, row.UserStatedFieldCount);
        Assert.Equal(1, row.CommitmentCount);
        Assert.Equal(1, row.ConstraintCount);

        var serialized = JsonSerializer.Serialize(row, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain(promptSentinel, serialized);
        Assert.DoesNotContain("FIELD_VALUE_SENTINEL_SHOULD_NOT_LEAK", serialized);
        Assert.DoesNotContain("COMMITMENT_TITLE_SENTINEL_SHOULD_NOT_LEAK", serialized);
        Assert.DoesNotContain("CONSTRAINT_VALUE_SENTINEL_SHOULD_NOT_LEAK", serialized);
        Assert.DoesNotContain("MEMORY_DIGEST_SENTINEL_SHOULD_NOT_LEAK", serialized);
        Assert.DoesNotContain("unit-test-key", serialized);
    }

    [Fact]
    public async Task EmptyContentIsBlocked()
    {
        var planner = CreatePlanner(new QueueHandler(
            Json(HttpStatusCode.OK, """{"data":{"total_credits":40,"total_usage":1}}"""),
            Json(HttpStatusCode.OK, """{"choices":[{"message":{"content":""}}]}""")));

        await Assert.ThrowsAsync<ProviderEmptyResponseException>(() => planner.PlanAsync(CreatePacket("packet-live")));
    }

    [Fact]
    public async Task MalformedPlannerJsonIsBlockedWithoutRawPayloadInMessage()
    {
        const string sentinel = "RAW_PROVIDER_PAYLOAD_SENTINEL_SHOULD_NOT_LEAK";
        var planner = CreatePlanner(new QueueHandler(
            Json(HttpStatusCode.OK, """{"data":{"total_credits":40,"total_usage":1}}"""),
            Json(HttpStatusCode.OK, CompletionEnvelope("{not-json " + sentinel))));

        var ex = await Assert.ThrowsAsync<ProviderMalformedResponseException>(() => planner.PlanAsync(CreatePacket("packet-live")));

        Assert.DoesNotContain(sentinel, ex.Message);
    }

    [Fact]
    public async Task UnsupportedMissionKindIsBlockedWithoutEchoingRawKind()
    {
        const string sentinel = "RAW_MISSION_KIND_SENTINEL_SHOULD_NOT_LEAK";
        var planner = CreatePlanner(new QueueHandler(
            Json(HttpStatusCode.OK, """{"data":{"total_credits":40,"total_usage":1}}"""),
            Json(HttpStatusCode.OK, CompletionEnvelope(SuccessPlannerContent(missionKind: sentinel)))));

        var ex = await Assert.ThrowsAsync<ProviderMalformedResponseException>(() => planner.PlanAsync(CreatePacket("packet-live")));

        Assert.DoesNotContain(sentinel, ex.Message);
    }

    [Fact]
    public async Task PacketIdMismatchIsBlockedWithoutEchoingIds()
    {
        const string sentinel = "RAW_PACKET_ID_SENTINEL_SHOULD_NOT_LEAK";
        var planner = CreatePlanner(new QueueHandler(
            Json(HttpStatusCode.OK, """{"data":{"total_credits":40,"total_usage":1}}"""),
            Json(HttpStatusCode.OK, CompletionEnvelope(SuccessPlannerContent(packetId: sentinel)))));

        var ex = await Assert.ThrowsAsync<ProviderMalformedResponseException>(() => planner.PlanAsync(CreatePacket("packet-live")));

        Assert.DoesNotContain(sentinel, ex.Message);
        Assert.DoesNotContain("packet-live", ex.Message);
    }

    [Fact]
    public async Task TimeoutMapsToTypedProviderUnavailable()
    {
        var planner = CreatePlanner(new TimeoutHandler(), timeout: TimeSpan.FromMilliseconds(1));

        await Assert.ThrowsAsync<ProviderUnavailableException>(() => planner.PlanAsync(CreatePacket("packet-live")));
    }

    [Fact]
    public async Task ProviderFailureMapsToTypedProviderUnavailable()
    {
        var planner = CreatePlanner(new QueueHandler(
            Json(HttpStatusCode.OK, """{"data":{"total_credits":40,"total_usage":1}}"""),
            Text(HttpStatusCode.BadGateway, "upstream unavailable")));

        await Assert.ThrowsAsync<ProviderUnavailableException>(() => planner.PlanAsync(CreatePacket("packet-live")));
    }

    [Fact]
    public async Task CreditOrProviderHealthBlockedBeforeCompletion()
    {
        var planner = CreatePlanner(new QueueHandler(
            Json(HttpStatusCode.OK, """{"data":{"total_credits":40,"total_usage":40}}""")));

        await Assert.ThrowsAsync<ProviderCreditExhaustedException>(() => planner.PlanAsync(CreatePacket("packet-live")));
    }

    private static ModelCompletionMissionPlannerClient CreatePlanner(
        HttpMessageHandler handler,
        TimeSpan? timeout = null)
    {
        var options = new OpenRouterOptions
        {
            BaseUri = new Uri("https://openrouter.test"),
            Timeout = timeout ?? TimeSpan.FromSeconds(5)
        };
        var completion = new OpenRouterModelCompletionClient(new HttpClient(handler), options, () => "unit-test-key");

        return new ModelCompletionMissionPlannerClient(completion);
    }

    private static MissionPlannerPacket CreatePacket(string packetId, string? prompt = null) =>
        new(
            packetId,
            "vacation",
            prompt ?? "Plan a vacation.",
            "en-US",
            ["keep diagnostics sanitized"]);

    private static string SuccessPlannerContent(
        string packetId = "packet-live",
        string missionKind = "vacation",
        string fieldValue = "vacation",
        string commitmentTitle = "Protect downtime.",
        string constraintValue = "Keep one flexible day.",
        string memoryDigest = "Vacation mission with downtime.")
    {
        return JsonSerializer.Serialize(new
        {
            packet_id = packetId,
            mission_kind = missionKind,
            fields = new[]
            {
                new
                {
                    field_path = "/mission/purpose",
                    value = fieldValue,
                    authority_source = "user_stated",
                    evidence_ids = new[] { "evidence-user-purpose" },
                    requires_confirmation = false
                }
            },
            commitments = new[]
            {
                new
                {
                    commitment_id = "commitment-rest",
                    commitment_kind = "downtime",
                    title = commitmentTitle,
                    starts_at = (string?)null,
                    ends_at = (string?)null,
                    location = (string?)null,
                    is_irreversible = false,
                    requires_spend = false,
                    commitment_priority = "normal",
                    authority_source = "model_inferred",
                    evidence_ids = new[] { "evidence-model-pace" }
                }
            },
            constraints = new[]
            {
                new
                {
                    constraint_id = "constraint-flex-day",
                    label = "Flexible day",
                    value = constraintValue,
                    authority_source = "model_inferred",
                    is_hard = false,
                    evidence_ids = new[] { "evidence-model-pace" }
                }
            },
            pending_confirmations = new[] { "Confirm dates." },
            memory_digest = memoryDigest
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static string CompletionEnvelope(string content)
    {
        return JsonSerializer.Serialize(new
        {
            id = "request-live",
            model = OpenRouterOptions.DefaultModel,
            choices = new[]
            {
                new { message = new { content } }
            },
            usage = new { prompt_tokens = 1, completion_tokens = 2, total_tokens = 3 }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string body) =>
        new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private static HttpResponseMessage Text(HttpStatusCode statusCode, string body) =>
        new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/plain")
        };

    private sealed class QueueHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(CloneRequest(request));
            RequestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));

            return _responses.Dequeue();
        }

        private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            clone.Headers.Authorization = request.Headers.Authorization;
            return clone;
        }
    }

    private sealed class TimeoutHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return Json(HttpStatusCode.OK, "{}");
        }
    }
}
