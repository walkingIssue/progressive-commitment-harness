namespace Pch.Providers.ModelCompletion;

public interface IModelCompletionClient
{
    Task<ModelCompletionResponse> CompleteAsync(
        ModelCompletionRequest request,
        CancellationToken cancellationToken = default);
}

public interface IProviderCreditClient
{
    Task<ProviderCreditStatus> GetCreditStatusAsync(CancellationToken cancellationToken = default);
}
