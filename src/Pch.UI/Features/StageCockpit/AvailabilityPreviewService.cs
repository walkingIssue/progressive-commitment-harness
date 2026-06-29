using Pch.Providers.Adapters;
using Pch.Providers.CandidateExpansion;
using Pch.Providers.HoldPreparation;
using Pch.Providers.Mock;

namespace Pch.UI.Features.StageCockpit;

internal sealed class AvailabilityPreviewService
{
    private const string ProviderName = "mock-availability-preview";
    private const string ModelName = "mock-availability-deterministic";

    private readonly MockAvailabilityAdapter _availabilityAdapter = new();

    public AvailabilityPreviewResult Run(string runId)
    {
        var scenario = AvailabilityPreviewScenario.For(runId);
        if (scenario is null)
        {
            return BlockedUnknown(runId);
        }

        if (!string.Equals(scenario.SlotId, scenario.TrustedSlotId, StringComparison.Ordinal))
        {
            return HarnessBlocked(scenario);
        }

        return scenario.Kind switch
        {
            AvailabilityPreviewScenarioKind.Accepted => QuoteReady(scenario),
            AvailabilityPreviewScenarioKind.Unavailable => Unavailable(scenario),
            AvailabilityPreviewScenarioKind.StalePacket => ProviderBlocked(scenario),
            AvailabilityPreviewScenarioKind.ApprovalRequired => ApprovalRequired(scenario),
            AvailabilityPreviewScenarioKind.RawAbsence => QuoteReady(scenario),
            _ => HarnessBlocked(scenario)
        };
    }

    private AvailabilityPreviewResult QuoteReady(AvailabilityPreviewScenario scenario)
    {
        var option = _availabilityAdapter.SearchAsync(new AvailabilitySearchRequest(
            "Kyoto",
            new DateOnly(2026, 10, 5),
            new DateOnly(2026, 10, 6),
            2,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["slot_id"] = scenario.SlotId,
                ["candidate_id"] = scenario.CandidateId
            }))
            .GetAwaiter()
            .GetResult()
            .Single();
        var eval = EvaluateHoldPreparation(scenario, HoldPreparationOperation.Preview, MockHoldPreparationBehavior.Normal);

        var outcome = new AvailabilityPreviewOutcomeFixture(
            scenario.RunId,
            "quote-ready",
            scenario.SlotId,
            scenario.CandidateId,
            scenario.QuoteCategory,
            eval.OutcomeCode,
            "trusted_slot_candidate",
            "approval_not_required",
            null,
            null,
            option.Provider,
            ModelName,
            eval.RequestId);

        var quote = new AvailabilityQuoteFixture(
            $"quote-{scenario.RunId}",
            scenario.RunId,
            scenario.SlotId,
            scenario.CandidateId,
            scenario.QuoteCategory,
            option.Provider,
            "preview_only",
            "quote_ready",
            option.ExpiresAt.ToString("yyyy-MM-ddTHH:mm:ssZ"));

        return new(outcome, [quote]);
    }

    private static AvailabilityPreviewResult Unavailable(AvailabilityPreviewScenario scenario)
    {
        var outcome = new AvailabilityPreviewOutcomeFixture(
            scenario.RunId,
            "unavailable",
            scenario.SlotId,
            scenario.CandidateId,
            scenario.QuoteCategory,
            "availability_unavailable",
            "trusted_slot_candidate",
            "approval_not_required",
            "PCH_UI_AVAILABILITY_UNAVAILABLE",
            "Deterministic preview found no available option for the selected candidate.",
            ProviderName,
            ModelName,
            $"mock-availability-{scenario.RunId}");

        return new(outcome, []);
    }

    private static AvailabilityPreviewResult ProviderBlocked(AvailabilityPreviewScenario scenario)
    {
        var eval = EvaluateHoldPreparation(
            scenario,
            HoldPreparationOperation.Preview,
            MockHoldPreparationBehavior.PacketMismatch);
        var outcome = new AvailabilityPreviewOutcomeFixture(
            scenario.RunId,
            "provider-blocked",
            scenario.SlotId,
            scenario.CandidateId,
            scenario.QuoteCategory,
            eval.OutcomeCode,
            "trusted_slot_candidate",
            "approval_not_required",
            "PCH_UI_AVAILABILITY_PROVIDER_PACKET_ID_MISMATCH",
            "Provider preview result did not match the trusted preview packet.",
            eval.Provider ?? ProviderName,
            eval.Model ?? ModelName,
            eval.RequestId);

        return new(outcome, []);
    }

    private static AvailabilityPreviewResult ApprovalRequired(AvailabilityPreviewScenario scenario)
    {
        var eval = EvaluateHoldPreparation(
            scenario,
            HoldPreparationOperation.Hold,
            MockHoldPreparationBehavior.Normal);
        var outcome = new AvailabilityPreviewOutcomeFixture(
            scenario.RunId,
            "approval-required",
            scenario.SlotId,
            scenario.CandidateId,
            scenario.QuoteCategory,
            eval.OutcomeCode,
            "trusted_slot_candidate",
            "approval_required",
            "PCH_UI_AVAILABILITY_APPROVAL_REQUIRED",
            "Preview is quote-ready, but mock hold preparation requires explicit approval before provider handoff.",
            eval.Provider ?? ProviderName,
            eval.Model ?? ModelName,
            eval.RequestId);

        return new(outcome, []);
    }

    private static AvailabilityPreviewResult HarnessBlocked(AvailabilityPreviewScenario scenario)
    {
        var outcome = new AvailabilityPreviewOutcomeFixture(
            scenario.RunId,
            "harness-blocked",
            scenario.SlotId,
            scenario.CandidateId,
            scenario.QuoteCategory,
            "not_run",
            "harness_blocked_wrong_slot",
            "not_run",
            "PCH_UI_AVAILABILITY_WRONG_SLOT",
            "Selected candidate is not associated with the trusted itinerary slot.",
            ProviderName,
            ModelName,
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
            ProviderName,
            ModelName,
            null);

        return new(outcome, []);
    }

    private static SanitizedHoldPreparationEvalRow EvaluateHoldPreparation(
        AvailabilityPreviewScenario scenario,
        HoldPreparationOperation operation,
        MockHoldPreparationBehavior behavior)
    {
        var evaluator = new HoldPreparationEvaluator(new MockHoldPreparationAdapter(behavior));
        var packet = new HoldPreparationPacket(
            $"availability-preview-{scenario.RunId}",
            operation,
            [
                new SelectedItineraryCandidate(
                    scenario.SlotId,
                    scenario.CandidateId,
                    ToCandidateCategory(scenario.QuoteCategory))
            ],
            "en-US",
            ApprovalToken: null,
            ContextDigest: "availability-preview-context-redacted");

        return evaluator.EvaluateAsync([new HoldPreparationEvalCase(scenario.RunId, packet)])
            .GetAwaiter()
            .GetResult()
            .Single();
    }

    private static CandidateCategory ToCandidateCategory(string quoteCategory) =>
        quoteCategory switch
        {
            "dining" => CandidateCategory.Dining,
            "activity" => CandidateCategory.Activity,
            "transit" => CandidateCategory.Transit,
            _ => CandidateCategory.Downtime
        };
}

internal sealed record AvailabilityPreviewResult(
    AvailabilityPreviewOutcomeFixture Outcome,
    IReadOnlyList<AvailabilityQuoteFixture> Quotes);

internal sealed record AvailabilityPreviewScenario(
    string RunId,
    AvailabilityPreviewScenarioKind Kind,
    string SlotId,
    string TrustedSlotId,
    string CandidateId,
    string QuoteCategory)
{
    public static AvailabilityPreviewScenario? For(string runId) => runId switch
    {
        "availability.accepted" => new(runId, AvailabilityPreviewScenarioKind.Accepted, "slot-lunch-day-2", "slot-lunch-day-2", "candidate-ramen-lunch", "dining"),
        "availability.unavailable" => new(runId, AvailabilityPreviewScenarioKind.Unavailable, "slot-activity-day-2", "slot-activity-day-2", "candidate-garden-entry", "activity"),
        "availability.stale-packet" => new(runId, AvailabilityPreviewScenarioKind.StalePacket, "slot-transit-day-3", "slot-transit-day-3", "candidate-rail-pass", "transit"),
        "availability.wrong-slot" => new(runId, AvailabilityPreviewScenarioKind.WrongSlot, "slot-lunch-day-9", "slot-lunch-day-2", "candidate-ramen-lunch", "dining"),
        "availability.approval-required" => new(runId, AvailabilityPreviewScenarioKind.ApprovalRequired, "slot-dinner-day-4", "slot-dinner-day-4", "candidate-kaiseki-preview", "dining"),
        "availability.raw-sentinel" => new(runId, AvailabilityPreviewScenarioKind.RawAbsence, "slot-quiet-day-5", "slot-quiet-day-5", "candidate-tea-break", "downtime"),
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
