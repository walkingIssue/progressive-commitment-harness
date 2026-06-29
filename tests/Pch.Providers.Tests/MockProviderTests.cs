using Pch.Providers.Adapters;
using Pch.Providers.Mock;
using Pch.Providers.ModelCompletion;
using Xunit;

namespace Pch.Providers.Tests;

public sealed class MockProviderTests
{
    [Fact]
    public async Task MockCompletionIsDeterministic()
    {
        var client = new MockModelCompletionClient();
        var request = new ModelCompletionRequest(
            [new ModelMessage(ModelMessageRole.User, "hello")],
            Model: "unit-test-model");

        var response = await client.CompleteAsync(request);

        Assert.Equal("mock", response.Provider);
        Assert.Equal("unit-test-model", response.Model);
        Assert.Contains("\"inputLength\":5", response.Content);
    }

    [Fact]
    public async Task MockAvailabilityReturnsStableOption()
    {
        var adapter = new MockAvailabilityAdapter();

        var options = await adapter.SearchAsync(new AvailabilitySearchRequest(
            "Lisbon",
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 5),
            2));

        var option = Assert.Single(options);
        Assert.Equal("mock-option-001", option.Id);
        Assert.Equal(240m, option.Price);
    }

    [Fact]
    public async Task MockCommitAdapterRequiresApprovalTokenForHoldBookAndPay()
    {
        var adapter = new MockBookingCommitAdapter();
        var request = new BookingCommitRequest("mock-option-001", ApprovalToken: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.HoldAsync(request));
        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.BookAsync(request));
        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.PayAsync(request));
    }

    [Fact]
    public async Task MockCommitAdapterReturnsConfirmationWhenApproved()
    {
        var adapter = new MockBookingCommitAdapter();

        var result = await adapter.PayAsync(new BookingCommitRequest("mock-option-001", "approval-123"));

        Assert.Equal(BookingCommitKind.Pay, result.Kind);
        Assert.Equal("mock-pay-mock-option-001", result.ConfirmationId);
    }
}
