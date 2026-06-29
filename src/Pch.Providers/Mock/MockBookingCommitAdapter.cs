using Pch.Providers.Adapters;

namespace Pch.Providers.Mock;

public sealed class MockBookingCommitAdapter : IBookingCommitAdapter
{
    public Task<BookingCommitResult> HoldAsync(BookingCommitRequest request, CancellationToken cancellationToken = default) =>
        CommitAsync(request, BookingCommitKind.Hold, cancellationToken);

    public Task<BookingCommitResult> BookAsync(BookingCommitRequest request, CancellationToken cancellationToken = default) =>
        CommitAsync(request, BookingCommitKind.Book, cancellationToken);

    public Task<BookingCommitResult> PayAsync(BookingCommitRequest request, CancellationToken cancellationToken = default) =>
        CommitAsync(request, BookingCommitKind.Pay, cancellationToken);

    private static Task<BookingCommitResult> CommitAsync(
        BookingCommitRequest request,
        BookingCommitKind kind,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.ApprovalToken))
        {
            throw new InvalidOperationException("An approval token is required before hold, book, or pay can be committed.");
        }

        return Task.FromResult(new BookingCommitResult(
            "mock-booking",
            request.OptionId,
            $"mock-{kind.ToString().ToLowerInvariant()}-{request.OptionId}",
            kind));
    }
}
