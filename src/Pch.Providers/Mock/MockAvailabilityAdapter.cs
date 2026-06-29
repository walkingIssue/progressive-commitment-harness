using Pch.Providers.Adapters;

namespace Pch.Providers.Mock;

public sealed class MockAvailabilityAdapter : IAvailabilityAdapter
{
    public Task<IReadOnlyList<AvailabilityOption>> SearchAsync(
        AvailabilitySearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<AvailabilityOption> options =
        [
            new(
                "mock-option-001",
                "mock-availability",
                $"{request.Destination} deterministic stay",
                120m * Math.Max(1, request.Travelers),
                "USD",
                DateTimeOffset.UnixEpoch.AddDays(1))
        ];

        return Task.FromResult(options);
    }
}
