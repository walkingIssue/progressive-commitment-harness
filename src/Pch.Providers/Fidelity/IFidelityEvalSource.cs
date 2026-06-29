namespace Pch.Providers.Fidelity;

public interface IFidelityEvalSource
{
    FidelityEvalSourceKind SourceKind { get; }

    Task<FidelityEvalSourceResult> EvaluateAsync(
        FidelityEvalPacket packet,
        FidelityEvalOptions? options = null,
        CancellationToken cancellationToken = default);
}
