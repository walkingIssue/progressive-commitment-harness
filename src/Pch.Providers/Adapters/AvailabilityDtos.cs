namespace Pch.Providers.Adapters;

public sealed record AvailabilitySearchRequest(
    string Destination,
    DateOnly StartDate,
    DateOnly EndDate,
    int Travelers,
    IReadOnlyDictionary<string, string>? Filters = null);

public sealed record AvailabilityOption(
    string Id,
    string Provider,
    string Title,
    decimal Price,
    string Currency,
    DateTimeOffset ExpiresAt);

public interface IAvailabilityAdapter
{
    Task<IReadOnlyList<AvailabilityOption>> SearchAsync(
        AvailabilitySearchRequest request,
        CancellationToken cancellationToken = default);
}
