namespace Pch.UI.Features.StageCockpit;

internal sealed class StageCockpitFixtureProvider
{
    public StageCockpitFixture GetFixture() => new(
        Packet: new(
            Id: "slot_collection.demo",
            Name: "Trip intent slot collection",
            Summary: "Collect the first load-bearing trip constraints without asking the user to author a plan.",
            State: "Awaiting required slot review",
            Source: "Fixture fallback",
            RequiredSlotCount: 4,
            CompletedSlotCount: 3,
            AllowedOutputs:
            [
                "emit_form",
                "emit_choice_set",
                "request_approval",
                "defer_slot"
            ],
            Authority: "User answers can patch trip constraints. Model inferences require confirmation."),
        GeneratedForm: new(
            Title: "Generated Form",
            Fields:
            [
                new("tripPurpose", "Trip purpose", "select", "vacation", true,
                [
                    new("vacation", "Vacation"),
                    new("business", "Business"),
                    new("funeral", "Funeral / family obligation"),
                    new("family_support", "Helping family"),
                    new("mixed", "Mixed purpose")
                ]),
                new("country", "Country", "text", "Japan", true, []),
                new("startDate", "Start date", "date", "2026-10-05", true, []),
                new("endDate", "End date", "date", "2026-10-19", true, []),
                new("pace", "Pace", "select", "balanced", false,
                [
                    new("low_cognitive_load", "Low cognitive load"),
                    new("balanced", "Balanced"),
                    new("packed", "Packed")
                ])
            ]),
        ChoiceSet: new(
            Id: "dinner_strategy.choice_set",
            Title: "Choice Collapse",
            SelectedCandidateId: "dinner_mixed",
            Candidates:
            [
                new("dinner_reserved", "Reserve standout dinners", "Best when dining is a core part of the trip and timing can be protected.", "High certainty, higher commitment load"),
                new("dinner_mixed", "Mix bookings and flexible meals", "Keeps a few high-value reservations while preserving energy and spontaneity.", "Balanced commitment profile"),
                new("dinner_flexible", "Mostly casual finds", "Lower planning load and fewer commitments, with weaker guarantee for high-demand spots.", "Lowest planning load")
            ]),
        Approval: new(
            Id: "approval.mock_hold",
            Title: "Approval Gate",
            Summary: "Holds, bookings, and spend actions require a generated approval request plus a user token before an adapter can execute.",
            RequiredFor: "Fixture itinerary hold",
            State: "not_approved"),
        Trace: new(
            Label: "claim-ledger fixture",
            Claims:
            [
                new("claim.trip-purpose", "Trip purpose is vacation", "user", "tripPurpose"),
                new("claim.country", "Destination country is Japan", "user", "country"),
                new("claim.choice", "Dinner strategy candidate is preserved by ID", "fixture", "dinner_mixed"),
                new("claim.approval", "No adapter action may run before approval", "policy", "approval.mock_hold")
            ]),
        Session: new(
            Id: "session.fixture.trip-intent",
            EndpointHint: "UI-local seam pending Shellby integration",
            Responses:
            [
                new("response.pending.country", SessionResponseState.Pending, "Pending", "Country field update is staged for session apply.", "country", null),
                new("response.applied.pace", SessionResponseState.Applied, "Applied", "Pace preference accepted into the local session fixture.", "pace", null),
                new("response.rejected.date-window", SessionResponseState.Rejected, "Rejected", "Date window change rejected because it would invert start and end dates.", "endDate", null),
                new("response.approval.hold", SessionResponseState.ApprovalRequired, "Approval required", "Fixture itinerary hold requires explicit user approval before any adapter action.", "approval.mock_hold", "approval.mock_hold")
            ]),
        SuggestedActions: new(
            EndpointHint: "UI-local deterministic seam pending Shellby decoder contract",
            Suggestions:
            [
                new("suggestion.accept.defer-slot", "Defer dinner slot", "defer_slot", "Route a stage-allowed defer-slot action through harness decoder and intake.", null, null, """{ "slot_id": "dinner-day-2", "reason": "Need user preference." }"""),
                new("suggestion.blocked.booking", "Blocked booking handoff", "handoff", "Rejected by harness intake because booking handoff is not allowed for the current stage.", null, "approval-review", """{ "target": "booking-adapter", "reason": "Mock booking handoff." }"""),
                new("suggestion.decode.failure", "Malformed proposal", "defer_slot", "Show a sanitized decode failure without exposing proposal payload.", null, null, """{ "slot_id": "RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK" """)
            ],
            Outcomes: []),
        ModelSuggestionRuns: new(
            EndpointHint: "Server-side deterministic mock provider through provider bridge and runtime application",
            Runs:
            [
                new("server-model.accept.defer-slot", "Run accepted model suggestion", "defer_slot"),
                new("server-model.block.form-mismatch", "Run blocked model suggestion", "emit_form"),
                new("server-model.decode.missing-argument", "Run decode-failure model suggestion", "defer_slot")
            ],
            Outcomes: []),
        MissionIntake: new(
            EndpointHint: "Server-side deterministic runtime mission planner through provider DTO adapter, harness intake, and memory digest",
            Runs:
            [
                new("mission.vacation", "Plan vacation intake", "vacation"),
                new("mission.non-vacation-commitment", "Plan commitment intake", "family-support"),
                new("mission.pending-confirmation", "Plan confirmation intake", "pending-confirmation"),
                new("mission.validation-blocked", "Run provider-blocked intake", "validation-blocked"),
                new("mission.adapter-blocked", "Run adapter-blocked intake", "adapter-blocked"),
                new("mission.unknown-commitment-kind", "Run unknown-kind intake", "unknown-kind")
            ],
            Outcomes: [],
            AppliedFields: [],
            PendingConfirmations: [],
            HighPriorityCommitments: [],
            MemoryDigestFacts: []),
        PromptIntake: new(
            EndpointHint: "UI prompt packet seam through deterministic provider runtime and mission adapter",
            Runs:
            [
                new("prompt.accepted", "Plan from prompt", "accepted"),
                new("prompt.pending", "Infer pending prompt", "pending-confirmation"),
                new("prompt.provider-blocked", "Provider-blocked prompt", "provider-blocked"),
                new("prompt.adapter-blocked", "Adapter-blocked prompt", "adapter-blocked"),
                new("prompt.blank", "Blank prompt", "validation-blocked"),
                new("prompt.overlong", "Overlong prompt", "validation-blocked")
            ],
            Outcomes: [],
            AppliedFields: [],
            PendingConfirmations: [],
            HighPriorityCommitments: [],
            MemoryDigestFacts: []),
        ItineraryDayPlanner: new(
            EndpointHint: "Harness itinerary slot compiler through deterministic provider candidate expansion",
            Runs:
            [
                new("itinerary.accepted", "Build day skeleton", "accepted"),
                new("itinerary.select-candidate", "Select lunch candidate", "selection"),
                new("itinerary.defer-slot", "Defer activity slot", "defer"),
                new("itinerary.conflict", "Check fixed conflict", "conflict-blocked"),
                new("itinerary.missing-date", "Check date window", "date-blocked"),
                new("itinerary.provider-mismatch", "Check provider slots", "provider-blocked"),
                new("itinerary.hold.approval-required", "Request mock hold approval", "hold-approval-required"),
                new("itinerary.hold.approved", "Run approved mock hold", "hold-approved"),
                new("itinerary.hold.missing-approval", "Run hold without approval", "hold-missing-approval"),
                new("itinerary.hold.provider-mismatch", "Run mismatched hold", "hold-provider-mismatch")
            ],
            Outcomes: [],
            Days: [],
            CandidatePools: [],
            Evidence: [],
            DigestFacts: [],
            Holds: []));
}

public sealed record StageCockpitFixture(
    StagePacketFixture Packet,
    GeneratedFormFixture GeneratedForm,
    ChoiceSetFixture ChoiceSet,
    ApprovalGateFixture Approval,
    EvidenceTraceFixture Trace,
    StageSessionFixture Session,
    SuggestedActionPanelFixture SuggestedActions,
    ModelSuggestionRunPanelFixture ModelSuggestionRuns,
    MissionIntakePanelFixture MissionIntake,
    PromptIntakePanelFixture PromptIntake,
    ItineraryDayPlannerPanelFixture ItineraryDayPlanner);

public sealed record StagePacketFixture(
    string Id,
    string Name,
    string Summary,
    string State,
    string Source,
    int RequiredSlotCount,
    int CompletedSlotCount,
    IReadOnlyList<string> AllowedOutputs,
    string Authority);

public sealed record GeneratedFormFixture(string Title, IReadOnlyList<GeneratedFieldFixture> Fields);

public sealed record GeneratedFieldFixture(
    string Id,
    string Label,
    string Kind,
    string Value,
    bool Required,
    IReadOnlyList<FieldOptionFixture> Options);

public sealed record FieldOptionFixture(string Value, string Label);

public sealed record ChoiceSetFixture(
    string Id,
    string Title,
    string SelectedCandidateId,
    IReadOnlyList<ChoiceCandidateFixture> Candidates);

public sealed record ChoiceCandidateFixture(string Id, string Title, string Summary, string Tradeoff);

public sealed record ApprovalGateFixture(string Id, string Title, string Summary, string RequiredFor, string State);

public sealed record EvidenceTraceFixture(string Label, IReadOnlyList<EvidenceClaimFixture> Claims);

public sealed record EvidenceClaimFixture(string Id, string Text, string Source, string Reference);

public sealed record StageSessionFixture(
    string Id,
    string EndpointHint,
    IReadOnlyList<SessionResponseFixture> Responses);

public sealed record SessionResponseFixture(
    string Id,
    SessionResponseState State,
    string Label,
    string Summary,
    string Target,
    string? ApprovalId);

public sealed record SuggestedActionPanelFixture(
    string EndpointHint,
    IReadOnlyList<SuggestedActionFixture> Suggestions,
    IReadOnlyList<SuggestedActionOutcomeFixture> Outcomes);

public sealed record SuggestedActionFixture(
    string Id,
    string Title,
    string ActionKind,
    string Summary,
    string? CandidateId,
    string? ApprovalId,
    string JsonArguments);

public sealed record SuggestedActionOutcomeFixture(
    string SuggestionId,
    string State,
    string ActionKind,
    string TraceOutcome,
    string? CandidateId,
    string? ApprovalId,
    string? ErrorCode,
    string? BlockedReason);

public sealed record ModelSuggestionRunPanelFixture(
    string EndpointHint,
    IReadOnlyList<ModelSuggestionRunFixture> Runs,
    IReadOnlyList<ModelSuggestionRunOutcomeFixture> Outcomes);

public sealed record ModelSuggestionRunFixture(
    string Id,
    string Label,
    string ExpectedActionKind);

public sealed record ModelSuggestionRunOutcomeFixture(
    string RunId,
    string State,
    string ActionKind,
    string BridgeOutcomeCode,
    string RuntimeDecodeOutcomeCode,
    string RuntimeIntakeOutcomeCode,
    string TraceOutcome,
    string? ErrorCode,
    string? BlockedReason,
    string Provider,
    string Model,
    string? RequestId);

public sealed record MissionIntakePanelFixture(
    string EndpointHint,
    IReadOnlyList<MissionIntakeRunFixture> Runs,
    IReadOnlyList<MissionIntakeOutcomeFixture> Outcomes,
    IReadOnlyList<MissionFieldFixture> AppliedFields,
    IReadOnlyList<MissionFieldFixture> PendingConfirmations,
    IReadOnlyList<MissionCommitmentFixture> HighPriorityCommitments,
    IReadOnlyList<MemoryDigestFactFixture> MemoryDigestFacts);

public sealed record MissionIntakeRunFixture(
    string Id,
    string Label,
    string Scenario);

public sealed record MissionIntakeOutcomeFixture(
    string RunId,
    string State,
    string ProviderRuntimeOutcomeCode,
    string AdapterOutcomeCode,
    string PlannerOutcomeCode,
    string IntakeOutcomeCode,
    string MemoryDigestOutcomeCode,
    string TraceOutcome,
    string? ErrorCode,
    string? BlockedReason,
    string Provider,
    string Model,
    string? RequestId);

public sealed record MissionFieldFixture(
    string FieldId,
    string Label,
    string Value,
    string Source,
    string State);

public sealed record MissionCommitmentFixture(
    string CommitmentId,
    string Title,
    string Kind,
    string Priority,
    string Source);

public sealed record MemoryDigestFactFixture(
    string FactId,
    string Text,
    string Source,
    string ReferenceId);

public sealed record PromptIntakePanelFixture(
    string EndpointHint,
    IReadOnlyList<PromptIntakeRunFixture> Runs,
    IReadOnlyList<PromptIntakeOutcomeFixture> Outcomes,
    IReadOnlyList<MissionFieldFixture> AppliedFields,
    IReadOnlyList<MissionFieldFixture> PendingConfirmations,
    IReadOnlyList<MissionCommitmentFixture> HighPriorityCommitments,
    IReadOnlyList<MemoryDigestFactFixture> MemoryDigestFacts);

public sealed record PromptIntakeRunFixture(
    string Id,
    string Label,
    string Scenario);

public sealed record PromptIntakeOutcomeFixture(
    string RunId,
    string State,
    string PromptPacketOutcomeCode,
    string ProviderRuntimeOutcomeCode,
    string AdapterOutcomeCode,
    string IntakeOutcomeCode,
    string MemoryDigestOutcomeCode,
    string TraceOutcome,
    string? ErrorCode,
    string? BlockedReason,
    string Provider,
    string Model,
    string? RequestId);

public sealed record ItineraryDayPlannerPanelFixture(
    string EndpointHint,
    IReadOnlyList<ItineraryPlannerRunFixture> Runs,
    IReadOnlyList<ItineraryPlannerOutcomeFixture> Outcomes,
    IReadOnlyList<ItineraryDayFixture> Days,
    IReadOnlyList<ItineraryCandidatePoolFixture> CandidatePools,
    IReadOnlyList<ItineraryEvidenceFixture> Evidence,
    IReadOnlyList<MemoryDigestFactFixture> DigestFacts,
    IReadOnlyList<ItineraryHoldFixture> Holds);

public sealed record ItineraryPlannerRunFixture(
    string Id,
    string Label,
    string Scenario);

public sealed record ItineraryPlannerOutcomeFixture(
    string RunId,
    string State,
    string DayId,
    string SelectedOutcome,
    string DeferredOutcome,
    string BlockedOutcome,
    string HoldOutcome,
    string? ApprovalId,
    string? ErrorCode,
    string? BlockedReason);

public sealed record ItineraryDayFixture(
    string DayId,
    string Date,
    string State,
    IReadOnlyList<ItinerarySlotFixture> Slots);

public sealed record ItinerarySlotFixture(
    string SlotId,
    string SlotType,
    string State,
    string? CandidatePoolId,
    string? SelectedCandidateId);

public sealed record ItineraryCandidatePoolFixture(
    string PoolId,
    string SlotId,
    IReadOnlyList<ItineraryCandidateFixture> Candidates);

public sealed record ItineraryCandidateFixture(
    string CandidateId,
    string Category,
    string Title,
    IReadOnlyList<string> EvidenceIds);

public sealed record ItineraryEvidenceFixture(
    string EvidenceId,
    string Source,
    string Outcome);

public sealed record ItineraryHoldFixture(
    string HoldId,
    string SlotId,
    string CandidateId,
    string ApprovalId,
    string Outcome,
    string Provider,
    string? ConfirmationId,
    string? ErrorCode);

public enum SessionResponseState
{
    Pending,
    Applied,
    Rejected,
    ApprovalRequired,
    Blocked
}
