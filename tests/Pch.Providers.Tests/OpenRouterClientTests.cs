using System.Net;
using System.Text;
using System.Text.Json;
using Pch.Providers.Errors;
using Pch.Providers.ModelCompletion;
using Pch.Providers.OpenRouter;
using Xunit;

namespace Pch.Providers.Tests;

public sealed class OpenRouterClientTests
{
    [Fact]
    public async Task CompleteUsesDefaultQwenModelAndStructuredResponseFormat()
    {
        var handler = new QueueHandler(
            Json(HttpStatusCode.OK, """{"data":{"total_credits":40,"total_usage":14}}"""),
            Json(HttpStatusCode.OK, """{"id":"abc","model":"qwen/qwen3-14b","choices":[{"message":{"content":"{\"ok\":true}"}}],"usage":{"prompt_tokens":1,"completion_tokens":2,"total_tokens":3}}"""));
        var client = CreateClient(handler);

        var response = await client.CompleteAsync(new ModelCompletionRequest(
            [new ModelMessage(ModelMessageRole.User, "return json")],
            JsonSchemaName: "test_schema",
            JsonSchema: """{"type":"object","properties":{"ok":{"type":"boolean"}},"required":["ok"],"additionalProperties":false}"""));

        Assert.Equal(OpenRouterOptions.DefaultModel, response.Model);
        Assert.Equal("""{"ok":true}""", response.Content);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);

        var payload = JsonDocument.Parse(handler.RequestBodies[1]).RootElement;
        Assert.Equal(OpenRouterOptions.DefaultModel, payload.GetProperty("model").GetString());
        Assert.Equal("json_schema", payload.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Equal("Bearer", handler.Requests[1].Headers.Authorization?.Scheme);
        Assert.Equal("unit-test-key", handler.Requests[1].Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task CreditEndpointParsesRemainingCredits()
    {
        var handler = new QueueHandler(Json(HttpStatusCode.OK, """{"data":{"total_credits":40,"total_usage":14.25}}"""));
        var client = CreateClient(handler);

        var credits = await client.GetCreditStatusAsync();

        Assert.Equal(40m, credits.TotalCredits);
        Assert.Equal(14.25m, credits.TotalUsage);
        Assert.Equal(25.75m, credits.RemainingCredits);
        Assert.False(credits.IsExhausted);
    }

    [Fact]
    public async Task CompleteUsesConfiguredModelWhenRequestDoesNotOverrideIt()
    {
        var handler = new QueueHandler(
            Json(HttpStatusCode.OK, """{"data":{"total_credits":40,"total_usage":14}}"""),
            Json(HttpStatusCode.OK, """{"choices":[{"message":{"content":"ok"}}]}"""));
        var options = new OpenRouterOptions
        {
            BaseUri = new Uri("https://openrouter.test"),
            Model = "custom/model",
            Timeout = TimeSpan.FromSeconds(5)
        };
        var client = new OpenRouterModelCompletionClient(new HttpClient(handler), options, () => "unit-test-key");

        await client.CompleteAsync(new ModelCompletionRequest(
            [new ModelMessage(ModelMessageRole.User, "hello")]));

        var payload = JsonDocument.Parse(handler.RequestBodies[1]).RootElement;
        Assert.Equal("custom/model", payload.GetProperty("model").GetString());
    }

    [Fact]
    public async Task CompleteThrowsCreditExhaustedBeforeCompletionWhenCreditsAreLow()
    {
        var handler = new QueueHandler(Json(HttpStatusCode.OK, """{"data":{"total_credits":40,"total_usage":40}}"""));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<ProviderCreditExhaustedException>(() => client.CompleteAsync(
            new ModelCompletionRequest([new ModelMessage(ModelMessageRole.User, "hello")])));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CompleteThrowsEmptyResponseWhenMessageContentIsMissing()
    {
        var handler = new QueueHandler(
            Json(HttpStatusCode.OK, """{"data":{"total_credits":40,"total_usage":1}}"""),
            Json(HttpStatusCode.OK, """{"choices":[{"message":{"content":""}}]}"""));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<ProviderEmptyResponseException>(() => client.CompleteAsync(
            new ModelCompletionRequest([new ModelMessage(ModelMessageRole.User, "hello")])));
    }

    [Fact]
    public async Task CompleteThrowsMalformedResponseForInvalidJson()
    {
        var handler = new QueueHandler(
            Json(HttpStatusCode.OK, """{"data":{"total_credits":40,"total_usage":1}}"""),
            Json(HttpStatusCode.OK, """{not-json"""));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<ProviderMalformedResponseException>(() => client.CompleteAsync(
            new ModelCompletionRequest([new ModelMessage(ModelMessageRole.User, "hello")])));
    }

    [Fact]
    public async Task NonSuccessMapsToTypedProviderErrors()
    {
        var unavailable = CreateClient(new QueueHandler(Text(HttpStatusCode.BadGateway, "upstream down")));

        await Assert.ThrowsAsync<ProviderUnavailableException>(() => unavailable.GetCreditStatusAsync());

        var exhausted = CreateClient(new QueueHandler(Text(HttpStatusCode.PaymentRequired, "no credits")));

        await Assert.ThrowsAsync<ProviderCreditExhaustedException>(() => exhausted.GetCreditStatusAsync());
    }

    [Fact]
    public async Task CallerCancellationDuringCompletionIsNotMappedToProviderUnavailable()
    {
        var options = new OpenRouterOptions
        {
            BaseUri = new Uri("https://openrouter.test"),
            CheckCreditsBeforeCompletion = false,
            Timeout = TimeSpan.FromSeconds(5)
        };
        var client = new OpenRouterModelCompletionClient(
            new HttpClient(new CancellationObservingHandler()),
            options,
            () => "unit-test-key");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CompleteAsync(
            new ModelCompletionRequest([new ModelMessage(ModelMessageRole.User, "hello")]),
            cancellation.Token));
    }

    [Fact]
    public async Task CallerCancellationDuringCreditCheckIsNotMappedToProviderUnavailable()
    {
        var client = CreateClient(new CancellationObservingHandler());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CompleteAsync(
            new ModelCompletionRequest([new ModelMessage(ModelMessageRole.User, "hello")]),
            cancellation.Token));
    }

    private static OpenRouterModelCompletionClient CreateClient(QueueHandler handler)
    {
        var options = new OpenRouterOptions
        {
            BaseUri = new Uri("https://openrouter.test"),
            Timeout = TimeSpan.FromSeconds(5)
        };

        return new OpenRouterModelCompletionClient(new HttpClient(handler), options, () => "unit-test-key");
    }

    private static OpenRouterModelCompletionClient CreateClient(HttpMessageHandler handler)
    {
        var options = new OpenRouterOptions
        {
            BaseUri = new Uri("https://openrouter.test"),
            Timeout = TimeSpan.FromSeconds(5)
        };

        return new OpenRouterModelCompletionClient(new HttpClient(handler), options, () => "unit-test-key");
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

            return clone;
        }
    }

    private sealed class CancellationObservingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Json(HttpStatusCode.OK, "{}"));
        }
    }
}
