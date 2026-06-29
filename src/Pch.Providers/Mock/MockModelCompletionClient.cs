using Pch.Providers.ModelCompletion;

namespace Pch.Providers.Mock;

public sealed class MockModelCompletionClient : IModelCompletionClient, IProviderCreditClient
{
    public const string ProviderName = "mock";

    public Task<ModelCompletionResponse> CompleteAsync(
        ModelCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var userContent = request.Messages.LastOrDefault(message => message.Role == ModelMessageRole.User)?.Content ?? string.Empty;
        var content = $$"""
            {"provider":"mock","model":"{{request.Model ?? "mock-deterministic"}}","summary":"deterministic response","inputLength":{{userContent.Length}}}
            """;

        return Task.FromResult(new ModelCompletionResponse(
            request.Model ?? "mock-deterministic",
            content,
            ProviderName,
            new ModelUsage(null, null, null),
            "mock-request"));
    }

    public Task<ProviderCreditStatus> GetCreditStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ProviderCreditStatus(0m, 0m, null, false));
    }
}
