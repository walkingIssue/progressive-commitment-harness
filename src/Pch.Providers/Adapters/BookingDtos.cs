namespace Pch.Providers.Adapters;

public sealed record BookingCommitRequest(
    string OptionId,
    string? ApprovalToken,
    IReadOnlyDictionary<string, string>? TravelerFields = null);

public sealed record BookingCommitResult(
    string Provider,
    string OptionId,
    string ConfirmationId,
    BookingCommitKind Kind);

public enum BookingCommitKind
{
    Hold,
    Book,
    Pay
}

public interface IBookingCommitAdapter
{
    Task<BookingCommitResult> HoldAsync(BookingCommitRequest request, CancellationToken cancellationToken = default);

    Task<BookingCommitResult> BookAsync(BookingCommitRequest request, CancellationToken cancellationToken = default);

    Task<BookingCommitResult> PayAsync(BookingCommitRequest request, CancellationToken cancellationToken = default);
}
