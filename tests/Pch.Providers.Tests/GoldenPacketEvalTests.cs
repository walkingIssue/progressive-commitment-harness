using System.Text;
using System.Text.Json;
using Pch.Providers.Mock;
using Pch.Providers.ModelActions;
using Pch.Providers.ModelCompletion;
using Xunit;

namespace Pch.Providers.Tests;

public sealed class GoldenPacketEvalTests
{
    [Fact]
    public async Task LoaderReadsGoldenPacketCases()
    {
        await using var stream = OpenFixture("golden-packets.valid.json");

        var cases = await GoldenPacketEvalLoader.LoadCasesAsync(stream);

        Assert.Equal(2, cases.Count);
        Assert.Equal("ask-for-destination", cases[0].Name);
        Assert.Equal("emit_form", cases[0].ExpectedActionName);
        Assert.Equal("packet-golden-001", cases[0].Packet.PacketId);
    }

    [Fact]
    public async Task LoaderRejectsExpectedActionOutsideAllowedSet()
    {
        const string invalid = """
            {
              "cases": [
                {
                  "name": "invalid",
                  "expectedActionName": "pay_now",
                  "packet": {
                    "packetId": "packet-invalid",
                    "stage": "stage-ask",
                    "goal": "Collect fields",
                    "instructions": ["Ask only missing fields."],
                    "inputs": {},
                    "allowedActions": [
                      {"name": "emit_form", "description": "Ask for fields."}
                    ]
                  }
                }
              ]
            }
            """;
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(invalid));

        var exception = await Assert.ThrowsAsync<ModelActionRunnerException>(() =>
            GoldenPacketEvalLoader.LoadCasesAsync(stream));

        Assert.Equal("Golden packet eval expected action is outside the allowed set.", exception.Message);
    }

    [Fact]
    public async Task SanitizedEvalRowsDoNotIncludePromptOrRawModelPayload()
    {
        await using var stream = OpenFixture("golden-packets.valid.json");
        var cases = await GoldenPacketEvalLoader.LoadCasesAsync(stream);
        var runner = new SanitizedModelActionEvalRunner(new ModelActionRunner(new MockModelActionCompletionClient()));

        var rows = await runner.EvaluateAsync(cases);

        Assert.All(rows, row =>
        {
            Assert.True(row.Passed);
            Assert.Equal("emit_form", row.ActualActionName);
            Assert.NotNull(row.ResponseContentLength);
            Assert.Equal("mock-action", row.Provider);
            var serialized = JsonSerializer.Serialize(row, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.DoesNotContain("Lisbon", serialized);
            Assert.DoesNotContain("deterministic mock action", serialized);
            Assert.DoesNotContain("Ask only missing fields", serialized);
        });
    }

    [Fact]
    public async Task SanitizedEvalRowsUseErrorCodesForFailures()
    {
        var runner = new SanitizedModelActionEvalRunner(new ModelActionRunner(new StaticCompletionClient(
            """{"action":"pay_now","arguments":{}}""")));
        var cases = new[]
        {
            new ModelActionEvalCase("bad", CreatePacket(), "emit_form")
        };

        var row = Assert.Single(await runner.EvaluateAsync(cases));

        Assert.False(row.Passed);
        Assert.Equal("model_action_runner_error", row.ErrorCode);
        Assert.Null(row.ActualActionName);
        Assert.Null(row.ResponseContentLength);
    }

    private static FileStream OpenFixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        return File.OpenRead(path);
    }

    private static ModelActionPacket CreatePacket()
    {
        var input = JsonSerializer.SerializeToElement(new { destination = "Lisbon" });
        return new ModelActionPacket(
            "packet-test",
            "stage-ask",
            "Collect fields",
            ["Ask only missing fields."],
            new Dictionary<string, JsonElement> { ["trip"] = input },
            [new ModelActionDefinition("emit_form", "Ask for fields.")]);
    }

    private sealed class StaticCompletionClient(string content) : IModelCompletionClient
    {
        public Task<ModelCompletionResponse> CompleteAsync(
            ModelCompletionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ModelCompletionResponse("test-model", content, "test-provider", RequestId: "request-1"));
    }
}
