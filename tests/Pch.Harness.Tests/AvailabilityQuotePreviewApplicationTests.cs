using System.Text.Json;
using Pch.Core;
using Pch.Harness;
using Xunit;

namespace Pch.Harness.Tests;

public sealed class AvailabilityQuotePreviewApplicationTests
{
    private static readonly DateTimeOffset FixedAt = new(2027, 4, 1, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ObservedAt = new(2026, 6, 29, 0, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void AcceptedAvailabilityPreviewUsesTrustedSelectedCandidate()
    {
        var session = SelectedSession();
        var slot = Slot(session, ItinerarySlotKind.Activity);
        var app = new AvailabilityQuotePreviewApplication();

        var result = app.Preview(session, Request(app, session, slot, "candidate-03", CandidateKind.Activity));

        Assert.True(result.IsAccepted);
        Assert.False(result.IsBlocked);
        Assert.Equal(AvailabilityQuotePreviewApplication.PreviewAcceptedCode, result.Code);
        Assert.Equal("Availability quote preview accepted.", result.Summary);
        Assert.NotNull(result.Preview);
        Assert.True(result.Preview!.IsAvailable);
        Assert.False(result.Preview.RequiresApproval);
        Assert.False(result.Preview.IsRealHold);
        Assert.False(result.Preview.IsRealBooking);
        Assert.False(result.Preview.IsRealPayment);
        Assert.Equal(slot.SlotId, result.Preview.SlotId);
        Assert.Equal("candidate-03", result.Preview.CandidateId);
        Assert.Contains("evidence-fixture-candidates", result.EvidenceReferences);
        Assert.Single(result.Trace);
    }

    [Fact]
    public void UnavailablePreviewIsAcceptedButMarkedUnavailable()
    {
        var session = SelectedSession(
            candidateId: "candidate-unavailable",
            evidenceIds: ["evidence-unavailable"]);
        var slot = Slot(session, ItinerarySlotKind.Activity);
        var app = new AvailabilityQuotePreviewApplication();

        var result = app.Preview(session, Request(app, session, slot, "candidate-unavailable", CandidateKind.Activity));

        Assert.True(result.IsAccepted);
        Assert.Equal(AvailabilityQuotePreviewApplication.PreviewUnavailableCode, result.Code);
        Assert.Equal("Availability quote preview is unavailable.", result.Summary);
        Assert.NotNull(result.Preview);
        Assert.False(result.Preview!.IsAvailable);
        Assert.Equal("unavailable_preview", result.Preview.Status);
    }

    [Fact]
    public void QuotePreviewWithSpendAdjacentCandidateRequiresApprovalAndDoesNotRecordToken()
    {
        var session = SelectedSession(
            candidateId: "candidate-priced",
            estimatedCost: 120,
            currency: "USD");
        session.RecordApproval(new ApprovalToken("approval-preview", "RAW_APPROVAL_TOKEN_SHOULD_NOT_LEAK", FixedAt));
        var slot = Slot(session, ItinerarySlotKind.Activity);
        var app = new AvailabilityQuotePreviewApplication();

        var result = app.Preview(session, Request(
            app,
            session,
            slot,
            "candidate-priced",
            CandidateKind.Activity,
            AvailabilityQuoteKind.Quote));
        var serialized = JsonSerializer.Serialize(result, JsonOptions);

        AssertBlocked(result, AvailabilityQuotePreviewApplication.ApprovalRequiredCode);
        Assert.Equal("Availability quote preview requires approval before quote preparation.", result.Summary);
        Assert.DoesNotContain("RAW_APPROVAL_TOKEN_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("approved", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StaleCompilationSnapshotBlocksWithoutMutation()
    {
        var session = SelectedSession();
        var before = Counts(session);
        var slot = Slot(session, ItinerarySlotKind.Activity);
        var app = new AvailabilityQuotePreviewApplication();
        var request = Request(app, session, slot, "candidate-03", CandidateKind.Activity) with
        {
            CompilationFingerprint = "compilation-stale"
        };

        var result = app.Preview(session, request);

        AssertBlocked(result, AvailabilityQuotePreviewApplication.StaleCompilationSnapshotCode);
        Assert.Equal("Availability quote preview request references stale itinerary state.", result.Summary);
        Assert.Equal(before, Counts(session));
    }

    [Fact]
    public void WrongSlotCandidateOwnershipBlocksWithoutMutation()
    {
        var session = MealSelectionSession();
        var before = Counts(session);
        var breakfast = session.LastItineraryCompilation!.Days
            .SelectMany(day => day.Slots)
            .First(slot => slot.Kind == ItinerarySlotKind.Meal && slot.SlotId.Contains("breakfast", StringComparison.Ordinal));
        var app = new AvailabilityQuotePreviewApplication();

        var result = app.Preview(session, Request(
            app,
            session,
            breakfast,
            "candidate-lunch",
            CandidateKind.Restaurant));

        AssertBlocked(result, AvailabilityQuotePreviewApplication.CandidateOwnershipMismatchCode);
        Assert.Equal("Availability quote preview candidate is not trusted for the slot.", result.Summary);
        Assert.Equal(before, Counts(session));
    }

    [Fact]
    public void UnsupportedQuoteKindAndMalformedInputUseFixedCodes()
    {
        var session = SelectedSession();
        var slot = Slot(session, ItinerarySlotKind.Activity);
        var app = new AvailabilityQuotePreviewApplication();

        var unsupported = app.Preview(session, Request(
            app,
            session,
            slot,
            "candidate-03",
            CandidateKind.Activity,
            (AvailabilityQuoteKind)999));
        var malformed = app.Preview(session, Request(app, session, slot, "candidate-03", CandidateKind.Activity) with
        {
            CandidateId = " "
        });

        AssertBlocked(unsupported, AvailabilityQuotePreviewApplication.UnsupportedQuoteKindCode);
        Assert.Equal("Availability quote preview kind is unsupported.", unsupported.Summary);
        AssertBlocked(malformed, AvailabilityQuotePreviewApplication.MalformedInputCode);
        Assert.Equal("Availability quote preview request failed validation.", malformed.Summary);
    }

    [Fact]
    public void InvalidSessionAndUnsupportedCategoryUseFixedCodes()
    {
        var session = SelectedSession();
        var slot = Slot(session, ItinerarySlotKind.Activity);
        var app = new AvailabilityQuotePreviewApplication();

        var invalidSession = app.Preview(session, Request(app, session, slot, "candidate-03", CandidateKind.Activity) with
        {
            SessionId = "wrong-session"
        });
        var unsupportedCategory = app.Preview(session, Request(app, session, slot, "candidate-03", CandidateKind.Restaurant));

        AssertBlocked(invalidSession, AvailabilityQuotePreviewApplication.InvalidSessionCode);
        Assert.Equal("Availability quote preview request failed validation.", invalidSession.Summary);
        AssertBlocked(unsupportedCategory, AvailabilityQuotePreviewApplication.UnsupportedQuoteCategoryCode);
        Assert.Equal("Availability quote preview category is unsupported.", unsupportedCategory.Summary);
    }

    [Fact]
    public void NoCompiledItineraryUnknownSlotAndUnknownCandidateUseFixedCodes()
    {
        var noCompile = SyntheticTripFactory.CreateSession(1);
        var selected = SelectedSession();
        var slot = Slot(selected, ItinerarySlotKind.Activity);
        var app = new AvailabilityQuotePreviewApplication();
        var context = app.CurrentContext(selected);

        var noCompiled = app.Preview(noCompile, new AvailabilityQuotePreviewRequest(
            noCompile.SessionId,
            "slot-any",
            "candidate-any",
            ItinerarySlotKind.Activity,
            CandidateKind.Activity,
            AvailabilityQuoteKind.Availability,
            "compilation-any",
            "snapshot-any",
            FixedAt));
        var unknownSlot = app.Preview(selected, new AvailabilityQuotePreviewRequest(
            selected.SessionId,
            "slot-missing",
            "candidate-03",
            ItinerarySlotKind.Activity,
            CandidateKind.Activity,
            AvailabilityQuoteKind.Availability,
            context.CompilationFingerprint,
            context.SnapshotId,
            FixedAt));
        var unknownCandidate = app.Preview(selected, new AvailabilityQuotePreviewRequest(
            selected.SessionId,
            slot.SlotId,
            "candidate-missing",
            slot.Kind,
            CandidateKind.Activity,
            AvailabilityQuoteKind.Availability,
            context.CompilationFingerprint,
            context.SnapshotId,
            FixedAt));

        AssertBlocked(noCompiled, AvailabilityQuotePreviewApplication.NoCompiledItineraryCode);
        AssertBlocked(unknownSlot, AvailabilityQuotePreviewApplication.UnknownSlotCode);
        AssertBlocked(unknownCandidate, AvailabilityQuotePreviewApplication.UnknownCandidateCode);
    }

    [Fact]
    public void SerializedPreviewOmitsRawPromptProviderCredentialsPaymentBookingAndCandidateText()
    {
        const string providerSentinel = "RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK";
        const string promptSentinel = "RAW_PROMPT_SHOULD_NOT_LEAK";
        const string credentialSentinel = "RAW_CREDENTIAL_SHOULD_NOT_LEAK";
        const string paymentSentinel = "RAW_PAYMENT_SHOULD_NOT_LEAK";
        const string bookingSentinel = "RAW_BOOKING_REF_SHOULD_NOT_LEAK";
        var session = SelectedSession(
            missionPurpose: promptSentinel,
            candidateId: "candidate-safe",
            candidateTitle: providerSentinel,
            candidateSummary: credentialSentinel,
            evidenceIds: ["evidence-safe", paymentSentinel, bookingSentinel]);
        var slot = Slot(session, ItinerarySlotKind.Activity);
        var app = new AvailabilityQuotePreviewApplication();

        var result = app.Preview(session, Request(app, session, slot, "candidate-safe", CandidateKind.Activity));
        var serialized = JsonSerializer.Serialize(result, JsonOptions);

        Assert.True(result.IsAccepted);
        Assert.NotNull(result.Preview);
        Assert.False(result.Preview!.IsRealHold);
        Assert.False(result.Preview.IsRealBooking);
        Assert.False(result.Preview.IsRealPayment);
        Assert.DoesNotContain(providerSentinel, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(promptSentinel, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(credentialSentinel, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(paymentSentinel, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(bookingSentinel, serialized, StringComparison.Ordinal);
    }

    private static TripSession SelectedSession(
        string missionPurpose = "Focused Tokyo stopover",
        string candidateId = "candidate-03",
        string candidateTitle = "Fixture option",
        string candidateSummary = "Trusted slot-scoped candidate.",
        IReadOnlyList<string>? evidenceIds = null,
        decimal? estimatedCost = null,
        string? currency = null)
    {
        var session = SyntheticTripFactory.CreateSession(1);
        if (!string.Equals(session.Mission.Purpose, missionPurpose, StringComparison.Ordinal))
        {
            session.ReplaceMission(session.Mission with { Purpose = missionPurpose });
        }

        Compile(session);
        var slot = Slot(session, ItinerarySlotKind.Activity);
        session.AddItineraryCandidatePool(slot.SlotId, CandidatePoolFor(
            "pool-preview-activity",
            new Candidate(
                candidateId,
                CandidateKind.Activity,
                candidateTitle,
                candidateSummary,
                estimatedCost,
                currency,
                evidenceIds ?? ["evidence-fixture-candidates"],
                120)));
        Select(session, slot, candidateId, CandidateKind.Activity);
        return session;
    }

    private static TripSession MealSelectionSession()
    {
        var session = SyntheticTripFactory.CreateSession(1);
        Compile(session);
        var meals = session.LastItineraryCompilation!.Days
            .SelectMany(day => day.Slots)
            .Where(slot => slot.Kind == ItinerarySlotKind.Meal)
            .Take(2)
            .ToArray();
        var breakfast = meals[0];
        var lunch = meals[1];
        session.AddItineraryCandidatePool(breakfast.SlotId, CandidatePoolFor(
            "pool-breakfast",
            new Candidate(
                "candidate-breakfast",
                CandidateKind.Restaurant,
                "Breakfast",
                "Breakfast candidate.",
                null,
                null,
                ["evidence-breakfast"],
                100)));
        session.AddItineraryCandidatePool(lunch.SlotId, CandidatePoolFor(
            "pool-lunch",
            new Candidate(
                "candidate-lunch",
                CandidateKind.Restaurant,
                "Lunch",
                "Lunch candidate.",
                null,
                null,
                ["evidence-lunch"],
                100)));
        Select(session, lunch, "candidate-lunch", CandidateKind.Restaurant);
        return session;
    }

    private static void Compile(TripSession session)
    {
        var result = new ItinerarySlotCompiler().Compile(session, new ItineraryCompilationRequest(
            session.SessionId,
            null,
            null,
            null,
            []));
        Assert.True(result.IsCompiled);
    }

    private static void Select(TripSession session, ItinerarySlot slot, string candidateId, CandidateKind candidateKind)
    {
        var result = new ItineraryCandidateApplication().Apply(session, new ItinerarySlotDecisionRequest(
            session.SessionId,
            slot.SlotId,
            ItinerarySlotDecisionKind.Selected,
            slot.Kind,
            candidateId,
            candidateKind,
            FixedAt));
        Assert.True(result.IsAccepted);
    }

    private static AvailabilityQuotePreviewRequest Request(
        AvailabilityQuotePreviewApplication app,
        TripSession session,
        ItinerarySlot slot,
        string candidateId,
        CandidateKind candidateKind,
        AvailabilityQuoteKind quoteKind = AvailabilityQuoteKind.Availability)
    {
        var context = app.CurrentContext(session);
        return new(
            session.SessionId,
            slot.SlotId,
            candidateId,
            slot.Kind,
            candidateKind,
            quoteKind,
            context.CompilationFingerprint,
            context.SnapshotId,
            FixedAt);
    }

    private static ItinerarySlot Slot(TripSession session, ItinerarySlotKind kind)
    {
        return session.LastItineraryCompilation!.Days
            .SelectMany(day => day.Slots)
            .First(slot => slot.Kind == kind);
    }

    private static CandidatePool CandidatePoolFor(string poolId, Candidate candidate)
    {
        return new(poolId, "all", [candidate], [], ObservedAt);
    }

    private static (int Actions, int Decisions, int Approvals, int ItineraryDecisions) Counts(TripSession session)
    {
        return (
            session.Actions.Count,
            session.DecisionLedger.Records.Count,
            session.ApprovalTokens.Count,
            session.ItineraryDecisions.Count);
    }

    private static void AssertBlocked(AvailabilityQuotePreviewResult result, string expectedCode)
    {
        Assert.False(result.IsAccepted);
        Assert.True(result.IsBlocked);
        Assert.Equal(expectedCode, result.Code);
        Assert.Null(result.Preview);
        Assert.Empty(result.EvidenceReferences);
        Assert.Single(result.Trace);
    }
}
