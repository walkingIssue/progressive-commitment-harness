using System.Text.Json;
using Pch.Providers.Errors;
using Pch.Providers.Mock;
using Pch.Providers.ModelActions;
using Pch.Providers.ModelCompletion;
using Xunit;

namespace Pch.Providers.Tests;

public sealed class ModelActionRunnerTests
{
    [Fact]
    public async Task RunnerBuildsPacketPromptAndParsesAllowedAction()
    {
        var client = new CapturingCompletionClient(
            """{"action":"emit_form","arguments":{"field":"destination"},"summary":"ask user"}""");
        var runner = new ModelActionRunner(client);

        var result = await runner.RunAsync(CreatePacket());

        Assert.Equal("packet-1", result.PacketId);
        Assert.Equal("emit_form", result.ActionName);
        Assert.Equal("destination", result.Arguments.GetProperty("field").GetString());
        Assert.Equal("ask user", result.Summary);
        Assert.Equal("test-provider", result.Provider);
        Assert.Equal("model_action", client.LastRequest?.JsonSchemaName);
        Assert.Contains("emit_form", client.LastRequest?.JsonSchema);
        Assert.Contains("packet-1", client.LastRequest?.Messages.Last().Content);
    }

    [Fact]
    public async Task RunnerRejectsMalformedJson()
    {
        var runner = new ModelActionRunner(new CapturingCompletionClient("{not-json"));

        await Assert.ThrowsAsync<ProviderMalformedResponseException>(() => runner.RunAsync(CreatePacket()));
    }

    [Fact]
    public async Task RunnerRejectsDisallowedAction()
    {
        var runner = new ModelActionRunner(new CapturingCompletionClient(
            """{"action":"book_without_approval","arguments":{}}"""));

        await Assert.ThrowsAsync<ModelActionRunnerException>(() => runner.RunAsync(CreatePacket()));
    }

    [Fact]
    public async Task DeterministicMockActionClientSelectsFirstAllowedAction()
    {
        var runner = new ModelActionRunner(new MockModelActionCompletionClient());

        var result = await runner.RunAsync(CreatePacket());

        Assert.Equal("emit_form", result.ActionName);
        Assert.True(result.Arguments.GetProperty("deterministic").GetBoolean());
        Assert.Equal("packet-1", result.Arguments.GetProperty("packetId").GetString());
    }

    [Fact]
    public async Task EvaluatorReturnsPassFailRowsWithoutThrowingForBadCases()
    {
        var evaluator = new ModelActionEvaluator(new ModelActionRunner(new MockModelActionCompletionClient()));
        var cases = new[]
        {
            new ModelActionEvalCase("expected", CreatePacket(), "emit_form"),
            new ModelActionEvalCase("unexpected", CreatePacket(), "summarize")
        };

        var results = await evaluator.EvaluateAsync(cases);

        Assert.Collection(
            results,
            first => Assert.True(first.Passed),
            second =>
            {
                Assert.False(second.Passed);
                Assert.Equal("emit_form", second.ActualActionName);
            });
    }

    private static ModelActionPacket CreatePacket()
    {
        var input = JsonSerializer.SerializeToElement(new
        {
            destination = "Lisbon",
            travelers = 2
        });

        return new ModelActionPacket(
            "packet-1",
            "stage-ask",
            "Collect missing travel fields.",
            ["Ask only for missing fields.", "Never commit booking actions."],
            new Dictionary<string, JsonElement> { ["trip"] = input },
            [
                new ModelActionDefinition("emit_form", "Ask the user for structured fields."),
                new ModelActionDefinition("summarize", "Summarize the current packet.")
            ]);
    }

    private sealed class CapturingCompletionClient(string content) : IModelCompletionClient
    {
        public ModelCompletionRequest? LastRequest { get; private set; }

        public Task<ModelCompletionResponse> CompleteAsync(
            ModelCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new ModelCompletionResponse("test-model", content, "test-provider", RequestId: "request-1"));
        }
    }
}
