namespace Pch.Providers.CandidateExpansion;

public interface ICandidateExpansionSource
{
    Task<CandidateExpansionResult> ExpandAsync(
        CandidateExpansionPacket packet,
        CandidateExpansionOptions? options = null,
        CancellationToken cancellationToken = default);
}
