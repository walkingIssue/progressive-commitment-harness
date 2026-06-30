using Pch.Core;
using Pch.Harness;
using Pch.Providers.AvailabilityPreview;
using Pch.Providers.Mock;

namespace Pch.UI.Features.StageCockpit;

internal sealed class AvailabilityPreviewService
{
    private const string MetadataNotPersisted = "not_persisted";
    private static readonly DateOnly PlannerDate = new(2027, 4, 2);
    private static readonly DateTimeOffset ObservedAt = new(2026, 6, 29, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RequestedAt = new(2027, 4, 1, 9, 30, 0, TimeSpan.Zero);

    private readonly AvailabilityQuotePreviewApplication _harnessPreview = new();
    private readonly ItinerarySlotCompiler _slotCompiler = new();
    private readonly ItineraryCandidateApplication _candidateApplication = new();

    public AvailabilityPreviewResult Run(string runId)
    {
        var scenario = AvailabilityPreviewScenario.For(runId);
        if (scenario is null)
        {
            return BlockedUnknown(runId);
        }

        var session = CreateSelectedSession(scenario);
        var context = _harnessPreview.CurrentContext(session);
        var request = new AvailabilityQuotePreviewRequest(
            session.SessionId,
            scenario.RequestSlotId,
            scenario.CandidateId,
            scenario.RequestSlotKind,
            scenario.CandidateKind,
            scenario.QuoteKind,
            context.CompilationFingerprint,
            context.SnapshotId,
            RequestedAt);

        var harnessResult = _harnessPreview.Preview(session, request);
        if (harnessResult.IsBlocked)
        {
            return BlockedFromHarness(scenario, harnessResult);
        }

        var providerRow = EvaluateProvider(scenario);
        if (harnessResult.Code == AvailabilityQuotePreviewApplication.PreviewUnavailableCode
            || providerRow.OutcomeCode == AvailabilityPreviewEvaluator.OutcomeUnavailable)
        {
            return Unavailable(scenario, harnessResult, providerRow);
        }

        if (!providerRow.Passed)
        {
            return BlockedFromProvider(scenario, harnessResult, providerRow);
        }

        return QuoteReady(scenario, harnessResult, providerRow);
    }

    private TripSession CreateSelectedSession(AvailabilityPreviewScenario scenario)
    {
        var session = SyntheticTripFactory.CreateSession(2);
        var compilation = _slotCompiler.Compile(session, new ItineraryCompilationRequest(
            session.SessionId,
            PlannerDate,
            PlannerDate,
            null,
            ["ui-availability-preview"]));
        if (!compilation.IsCompiled)
        {
            return session;
        }

        var trustedSlotId = scenario.TrustedSlotId ?? scenario.RequestSlotId;
        var trustedSlot = compilation.Days
            .SelectMany(day => day.Slots)
            .First(slot => string.Equals(slot.SlotId, trustedSlotId, StringComparison.Ordinal));
        var candidate = new Candidate(
            scenario.CandidateId,
            scenario.CandidateKind,
            "Availability preview fixture",
            "Deterministic availability preview candidate.",
            scenario.EstimatedCost,
            scenario.Currency,
            scenario.EvidenceIds,
            100);

        session.AddItineraryCandidatePool(trustedSlot.SlotId, new CandidatePool(
            $"pool-{SafePoolId(scenario.RunId)}",
            "availability-preview",
            [candidate],
            [],
            ObservedAt));

        var selection = _candidateApplication.Apply(session, new ItinerarySlotDecisionRequest(
            session.SessionId,
            trustedSlot.SlotId,
            ItinerarySlotDecisionKind.Selected,
            trustedSlot.Kind,
            candidate.CandidateId,
            candidate.Kind,
            RequestedAt));
        if (selection.IsBlocked)
        {
            throw new InvalidOperationException("Deterministic availability preview fixture failed to seed a selected candidate.");
        }

        return session;
    }

    private static SanitizedAvailabilityPreviewEvalRow EvaluateProvider(AvailabilityPreviewScenario scenario)
    {
        var evaluator = new AvailabilityPreviewEvaluator(new MockAvailabilityPreviewAdapter(scenario.ProviderBehavior));
        var packet = new AvailabilityPreviewPacket(
            $"availability-preview-{SafePoolId(scenario.RunId)}",
            [
                new AvailabilityPreviewCandidate(
                    scenario.RequestSlotId,
                    scenario.CandidateId,
                    scenario.ProviderCategory)
            ],
            "en-US",
            "availability-preview-context-redacted");

        return evaluator.EvaluateAsync([new AvailabilityPreviewEvalCase(scenario.RunId, packet)])
            .GetAwaiter()
            .GetResult()
            .Single();
    }

    private static AvailabilityPreviewResult QuoteReady(
        AvailabilityPreviewScenario scenario,
        AvailabilityQuotePreviewResult harnessResult,
        SanitizedAvailabilityPreviewEvalRow providerRow)
    {
        var preview = harnessResult.Preview!;
        var outcome = new AvailabilityPreviewOutcomeFixture(
            scenario.RunId,
            "quote-ready",
            preview.SlotId,
            preview.CandidateId,
            scenario.QuoteCategory,
            providerRow.OutcomeCode,
            harnessResult.Code,
            "approval_not_required",
            null,
            null,
            providerRow.Provider ?? MetadataNotPersisted,
            providerRow.Model ?? MetadataNotPersisted,
            providerRow.RequestId);

        var quote = new AvailabilityQuoteFixture(
            $"quote-{scenario.RunId}",
            scenario.RunId,
            preview.SlotId,
            preview.CandidateId,
            scenario.QuoteCategory,
            providerRow.Provider ?? MetadataNotPersisted,
            "preview_only",
            "quote_ready",
            "2026-07-01T12:00:00Z");

        return new(outcome, [quote]);
    }

    private static AvailabilityPreviewResult Unavailable(
        AvailabilityPreviewScenario scenario,
        AvailabilityQuotePreviewResult harnessResult,
        SanitizedAvailabilityPreviewEvalRow providerRow)
    {
        var preview = harnessResult.Preview!;
        var outcome = new AvailabilityPreviewOutcomeFixture(
            scenario.RunId,
            "unavailable",
            preview.SlotId,
            preview.CandidateId,
            scenario.QuoteCategory,
            providerRow.OutcomeCode,
            harnessResult.Code,
            "approval_not_required",
            "PCH_UI_AVAILABILITY_UNAVAILABLE",
            harnessResult.Summary,
            MetadataNotPersisted,
            MetadataNotPersisted,
            null);

        return new(outcome, []);
    }

    private static AvailabilityPreviewResult BlockedFromProvider(
        AvailabilityPreviewScenario scenario,
        AvailabilityQuotePreviewResult harnessResult,
        SanitizedAvailabilityPreviewEvalRow providerRow)
    {
        var outcome = new AvailabilityPreviewOutcomeFixture(
            scenario.RunId,
            "provider-blocked",
            scenario.RequestSlotId,
            scenario.CandidateId,
            scenario.QuoteCategory,
            providerRow.OutcomeCode,
            harnessResult.Code,
            "approval_not_required",
            ProviderErrorCode(providerRow.OutcomeCode),
            ProviderBlockedReason(providerRow.OutcomeCode),
            MetadataNotPersisted,
            MetadataNotPersisted,
            null);

        return new(outcome, []);
    }

    private static AvailabilityPreviewResult BlockedFromHarness(
        AvailabilityPreviewScenario scenario,
        AvailabilityQuotePreviewResult harnessResult)
    {
        var isApprovalRequired = string.Equals(
            harnessResult.Code,
            AvailabilityQuotePreviewApplication.ApprovalRequiredCode,
            StringComparison.Ordinal);
        var outcome = new AvailabilityPreviewOutcomeFixture(
            scenario.RunId,
            isApprovalRequired ? "approval-required" : "harness-blocked",
            scenario.RequestSlotId,
            scenario.CandidateId,
            scenario.QuoteCategory,
            "not_run",
            harnessResult.Code,
            isApprovalRequired ? "approval_required" : "not_run",
            HarnessErrorCode(harnessResult.Code),
            harnessResult.Summary,
            MetadataNotPersisted,
            MetadataNotPersisted,
            null);

        return new(outcome, []);
    }

    private static AvailabilityPreviewResult BlockedUnknown(string runId)
    {
        var outcome = new AvailabilityPreviewOutcomeFixture(
            runId,
            "harness-blocked",
            "",
            "",
            "unknown",
            "not_run",
            "harness_blocked_unknown_run",
            "not_run",
            "PCH_UI_AVAILABILITY_RUN_UNKNOWN",
            "Availability preview scenario is not recognized.",
            MetadataNotPersisted,
            MetadataNotPersisted,
            null);

        return new(outcome, []);
    }

    private static string ProviderErrorCode(string outcomeCode) => outcomeCode switch
    {
        AvailabilityPreviewEvaluator.OutcomePacketMismatch => "PCH_UI_AVAILABILITY_PROVIDER_PACKET_ID_MISMATCH",
        AvailabilityPreviewEvaluator.OutcomeCandidateMismatch => "PCH_UI_AVAILABILITY_PROVIDER_CANDIDATE_MISMATCH",
        AvailabilityPreviewEvaluator.OutcomeMalformedPacket => "PCH_UI_AVAILABILITY_PROVIDER_MALFORMED_PACKET",
        AvailabilityPreviewEvaluator.OutcomeMalformedResult => "PCH_UI_AVAILABILITY_PROVIDER_MALFORMED_RESULT",
        AvailabilityPreviewEvaluator.OutcomeUnsupportedResult => "PCH_UI_AVAILABILITY_PROVIDER_UNSUPPORTED_RESULT",
        AvailabilityPreviewEvaluator.OutcomeUnsupportedCategory => "PCH_UI_AVAILABILITY_PROVIDER_UNSUPPORTED_CATEGORY",
        AvailabilityPreviewEvaluator.OutcomeProviderUnavailable => "PCH_UI_AVAILABILITY_PROVIDER_UNAVAILABLE",
        AvailabilityPreviewEvaluator.OutcomeTimeout => "PCH_UI_AVAILABILITY_PROVIDER_TIMEOUT",
        _ => "PCH_UI_AVAILABILITY_PROVIDER_BLOCKED"
    };

    private static string ProviderBlockedReason(string outcomeCode) => outcomeCode switch
    {
        AvailabilityPreviewEvaluator.OutcomePacketMismatch => "Provider preview result did not match the trusted preview packet.",
        AvailabilityPreviewEvaluator.OutcomeCandidateMismatch => "Provider preview candidate rows did not match the trusted packet.",
        AvailabilityPreviewEvaluator.OutcomeMalformedPacket => "Provider preview packet failed validation before provider handoff.",
        AvailabilityPreviewEvaluator.OutcomeMalformedResult => "Provider preview result failed sanitized validation.",
        AvailabilityPreviewEvaluator.OutcomeUnsupportedResult => "Provider preview returned an unsupported result.",
        AvailabilityPreviewEvaluator.OutcomeUnsupportedCategory => "Provider preview returned an unsupported category.",
        AvailabilityPreviewEvaluator.OutcomeProviderUnavailable => "Provider preview adapter was unavailable.",
        AvailabilityPreviewEvaluator.OutcomeTimeout => "Provider preview adapter timed out.",
        _ => "Provider preview was blocked by sanitized evaluation."
    };

    private static string HarnessErrorCode(string harnessCode) => harnessCode switch
    {
        AvailabilityQuotePreviewApplication.ApprovalRequiredCode => "PCH_UI_AVAILABILITY_APPROVAL_REQUIRED",
        AvailabilityQuotePreviewApplication.CandidateOwnershipMismatchCode => "PCH_UI_AVAILABILITY_WRONG_SLOT",
        AvailabilityQuotePreviewApplication.UnknownSlotCode => "PCH_UI_AVAILABILITY_UNKNOWN_SLOT",
        AvailabilityQuotePreviewApplication.UnknownCandidateCode => "PCH_UI_AVAILABILITY_UNKNOWN_CANDIDATE",
        AvailabilityQuotePreviewApplication.StaleCompilationSnapshotCode => "PCH_UI_AVAILABILITY_STALE_COMPILATION",
        AvailabilityQuotePreviewApplication.UnsupportedQuoteCategoryCode => "PCH_UI_AVAILABILITY_UNSUPPORTED_CATEGORY",
        _ => "PCH_UI_AVAILABILITY_HARNESS_BLOCKED"
    };

    private static string SafePoolId(string value) => value.Replace(".", "-", StringComparison.Ordinal);
}

internal sealed record AvailabilityPreviewResult(
    AvailabilityPreviewOutcomeFixture Outcome,
    IReadOnlyList<AvailabilityQuoteFixture> Quotes);

internal sealed record AvailabilityPreviewScenario(
    string RunId,
    AvailabilityPreviewScenarioKind Kind,
    string RequestSlotId,
    string? TrustedSlotId,
    ItinerarySlotKind RequestSlotKind,
    string CandidateId,
    CandidateKind CandidateKind,
    string QuoteCategory,
    AvailabilityPreviewCategory ProviderCategory,
    AvailabilityQuoteKind QuoteKind,
    MockAvailabilityPreviewBehavior ProviderBehavior,
    IReadOnlyList<string> EvidenceIds,
    decimal? EstimatedCost = null,
    string? Currency = null)
{
    public static AvailabilityPreviewScenario? For(string runId) => runId switch
    {
        "availability.accepted" => new(
            runId,
            AvailabilityPreviewScenarioKind.Accepted,
            "slot-20270402-lunch",
            null,
            ItinerarySlotKind.Meal,
            "candidate-ramen-lunch",
            CandidateKind.Restaurant,
            "dining",
            AvailabilityPreviewCategory.Dining,
            AvailabilityQuoteKind.Availability,
            MockAvailabilityPreviewBehavior.QuoteReady,
            ["evidence-fixture-candidates"]),
        "availability.unavailable" => new(
            runId,
            AvailabilityPreviewScenarioKind.Unavailable,
            "slot-20270402-activity",
            null,
            ItinerarySlotKind.Activity,
            "candidate-garden-entry",
            CandidateKind.Activity,
            "activity",
            AvailabilityPreviewCategory.Activity,
            AvailabilityQuoteKind.Availability,
            MockAvailabilityPreviewBehavior.Unavailable,
            ["evidence-unavailable"]),
        "availability.stale-packet" => new(
            runId,
            AvailabilityPreviewScenarioKind.StalePacket,
            "slot-20270402-transit-start",
            null,
            ItinerarySlotKind.Transit,
            "candidate-rail-pass",
            CandidateKind.Transit,
            "transit",
            AvailabilityPreviewCategory.Transit,
            AvailabilityQuoteKind.Availability,
            MockAvailabilityPreviewBehavior.PacketMismatch,
            ["evidence-fixture-candidates"]),
        "availability.wrong-slot" => new(
            runId,
            AvailabilityPreviewScenarioKind.WrongSlot,
            "slot-20270402-breakfast",
            "slot-20270402-lunch",
            ItinerarySlotKind.Meal,
            "candidate-ramen-lunch",
            CandidateKind.Restaurant,
            "dining",
            AvailabilityPreviewCategory.Dining,
            AvailabilityQuoteKind.Availability,
            MockAvailabilityPreviewBehavior.QuoteReady,
            ["evidence-fixture-candidates"]),
        "availability.approval-required" => new(
            runId,
            AvailabilityPreviewScenarioKind.ApprovalRequired,
            "slot-20270402-dinner",
            null,
            ItinerarySlotKind.Meal,
            "candidate-kaiseki-preview",
            CandidateKind.Restaurant,
            "dining",
            AvailabilityPreviewCategory.Dining,
            AvailabilityQuoteKind.Quote,
            MockAvailabilityPreviewBehavior.QuoteReady,
            ["evidence-fixture-candidates"],
            180,
            "USD"),
        "availability.raw-sentinel" => new(
            runId,
            AvailabilityPreviewScenarioKind.RawAbsence,
            "slot-20270402-downtime",
            null,
            ItinerarySlotKind.Downtime,
            "candidate-tea-break",
            CandidateKind.Activity,
            "activity",
            AvailabilityPreviewCategory.Activity,
            AvailabilityQuoteKind.Availability,
            MockAvailabilityPreviewBehavior.QuoteReady,
            ["evidence-fixture-candidates"]),
        _ => null
    };
}

internal enum AvailabilityPreviewScenarioKind
{
    Accepted,
    Unavailable,
    StalePacket,
    WrongSlot,
    ApprovalRequired,
    RawAbsence
}
