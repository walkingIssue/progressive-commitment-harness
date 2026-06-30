using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pch.Core;

namespace Pch.Harness;

public sealed class LiveTurnProjector
{
    public const string AcceptedCode = "accepted";
    public const string AwaitingUserInputCode = "awaiting_user_input";
    public const string ProviderModelBlockedCode = "provider_model_blocked";
    public const string IntakeBlockedCode = "intake_blocked";
    public const string ApprovalRequiredCode = "approval_required";
    public const string DeterministicFallbackCode = "deterministic_fallback";

    public const string PendingConfirmationTraceId = "live-turn-pending-confirmation";

    private const int MaxTurns = 12;
    private const int MaxEvidenceReferences = 8;
    private const int MaxMarkers = 8;
    private const int MaxFields = 8;
    private const int MaxChoices = 8;
    private const int MaxTextLength = 120;

    private static readonly DateTimeOffset FixedObservedAt = new(2026, 6, 30, 0, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly CardMediaProvenanceBoundary _media = new();

    private static readonly string[] UnsafeFragments =
    [
        "RAW_",
        "PROVIDER_PAYLOAD",
        "RAW_PROMPT",
        "APPROVAL_TOKEN",
        "HOLD_REFERENCE",
        "PAYMENT",
        "BOOKING_REF",
        "CANDIDATE_DISPLAY",
        "SECRET",
        "CREDENTIAL",
        "PASSWORD",
        "API_KEY"
    ];

    public LiveTurnProjectionResult FromPromptIntake(PromptIntakeResult result)
    {
        if (result is null || !result.IsAccepted || result.Packet is null)
        {
            return BlockedResult(
                ProviderModelBlockedCode,
                "Prompt intake was blocked before planner execution.",
                [BlockedTurn(1, ProviderModelBlockedCode, "Prompt intake was blocked before planner execution.", [])]);
        }

        var turns = new List<LiveTurn>
        {
            SummaryTurn(
                1,
                AcceptedCode,
                "Prompt metadata accepted for planner execution.",
                [result.Packet.PacketId, result.Packet.Prompt.Category],
                result.Packet.EvidenceReferences)
        };

        if (result.Packet.PendingConfirmations.Count > 0)
        {
            turns.Add(FormTurn(
                2,
                AwaitingUserInputCode,
                "Pending confirmations require user input.",
                result.Packet.PendingConfirmations,
                result.Packet.EvidenceReferences));

            return BuildResult(
                IsAccepted: true,
                IsBlocked: false,
                AwaitingUserInputCode,
                "Live turn is awaiting user input.",
                turns);
        }

        return BuildResult(
            IsAccepted: true,
            IsBlocked: false,
            AcceptedCode,
            "Live turn accepted.",
            turns);
    }

    public LiveTurnProjectionResult FromMissionPendingConfirmations(
        PromptIntakeResult result,
        IReadOnlyList<MissionPendingConfirmation> pendingConfirmations)
    {
        if (result is null || !result.IsAccepted || result.Packet is null)
        {
            return BlockedResult(
                ProviderModelBlockedCode,
                "Prompt intake was blocked before planner execution.",
                [BlockedTurn(1, ProviderModelBlockedCode, "Prompt intake was blocked before planner execution.", [])]);
        }

        var pending = (pendingConfirmations ?? [])
            .Take(MaxFields)
            .Select(item => new PromptPendingConfirmation(
                item.FieldPath,
                item.Source.ToString(),
                item.ReasonCode,
                item.EvidenceIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray()))
            .ToArray();

        var turns = new List<LiveTurn>
        {
            SummaryTurn(
                1,
                AcceptedCode,
                "Prompt metadata accepted for planner execution.",
                [result.Packet.PacketId, result.Packet.Prompt.Category],
                result.Packet.EvidenceReferences),
            FormTurn(
                2,
                AwaitingUserInputCode,
                "Pending confirmations require user input.",
                pending,
                pending.SelectMany(item => item.EvidenceIds).Distinct(StringComparer.Ordinal).Take(MaxEvidenceReferences).ToArray())
        };

        return BuildResult(
            IsAccepted: true,
            IsBlocked: false,
            AwaitingUserInputCode,
            "Live turn is awaiting user input.",
            turns);
    }

    public LiveTurnProjectionResult FromSessionTurn(SessionTurnResult result)
    {
        if (result is null)
        {
            return DeterministicFallback();
        }

        if (result.IsBlocked)
        {
            return BlockedResult(
                IntakeBlockedCode,
                "Harness intake blocked the turn.",
                [BlockedTurn(1, IntakeBlockedCode, "Harness intake blocked the turn.", TraceEvidence(result.Trace))]);
        }

        var turn = result.NextAction switch
        {
            EmitFormAction form => FormTurn(1, AwaitingUserInputCode, "Form input is required.", form.Form),
            EmitChoiceSetAction choice => ChoiceTurn(1, AwaitingUserInputCode, "Choice input is required.", choice),
            RequestApprovalAction approval => ApprovalTurn(1, ApprovalRequiredCode, "Approval is required.", approval.Approval),
            SummarizeAction summary => SummaryTurn(1, AcceptedCode, "Summary turn is ready.", summary.ClaimIds, []),
            null => EvidenceTurn(1, AcceptedCode, "Turn accepted with no further action.", TraceEvidence(result.Trace), []),
            _ => SummaryTurn(1, AcceptedCode, "Turn accepted.", [result.NextAction.Kind], TraceEvidence(result.Trace))
        };

        var code = turn.Kind is "form" or "choice" ? AwaitingUserInputCode : turn.OutcomeCode;
        return BuildResult(
            IsAccepted: true,
            IsBlocked: false,
            code,
            code == AwaitingUserInputCode ? "Live turn is awaiting user input." : "Live turn accepted.",
            [turn]);
    }

    public LiveTurnProjectionResult FromRuntimeAction(RuntimeActionApplicationResult result)
    {
        if (result is null)
        {
            return DeterministicFallback();
        }

        if (result.IsBlocked && result.IntakeCode == "not_run")
        {
            return BlockedResult(
                ProviderModelBlockedCode,
                "Model action proposal was blocked before intake.",
                [ProviderFailureTurn(1, result.DecodeCode, "Model action proposal was blocked before intake.", TraceEvidence(result.Trace))]);
        }

        if (result.IsBlocked)
        {
            var code = string.Equals(result.IntakeCode, ApprovalRequiredCode, StringComparison.Ordinal)
                ? ApprovalRequiredCode
                : IntakeBlockedCode;
            var summary = code == ApprovalRequiredCode
                ? "Approval is required before this turn can continue."
                : "Harness intake blocked the action proposal.";

            return BlockedResult(
                code,
                summary,
                [BlockedTurn(1, code, summary, TraceEvidence(result.Trace))]);
        }

        return BuildResult(
            IsAccepted: true,
            IsBlocked: false,
            AcceptedCode,
            "Runtime action accepted.",
            [EvidenceTurn(1, AcceptedCode, "Runtime action accepted.", TraceEvidence(result.Trace), [])]);
    }

    public LiveTurnProjectionResult FromItineraryDecision(ItinerarySlotApplicationResult result, CardMediaManifest? mediaManifest = null)
    {
        if (result is null)
        {
            return DeterministicFallback();
        }

        if (result.IsBlocked)
        {
            return BlockedResult(
                IntakeBlockedCode,
                "Itinerary decision was blocked.",
                [BlockedTurn(1, IntakeBlockedCode, "Itinerary decision was blocked.", result.EvidenceIds)]);
        }

        if (result.Decision is { Kind: ItinerarySlotDecisionKind.Selected } decision)
        {
            return BuildResult(
                IsAccepted: true,
                IsBlocked: false,
                AcceptedCode,
                "Choice selection accepted.",
                [ChoiceEchoTurn(1, decision, mediaManifest)]);
        }

        return BuildResult(
            IsAccepted: true,
            IsBlocked: false,
            AwaitingUserInputCode,
            "Itinerary slot was deferred for later input.",
            [EvidenceTurn(1, AwaitingUserInputCode, "Itinerary slot deferred.", result.EvidenceIds, [])]);
    }

    public LiveTurnProjectionResult FromAvailabilityPreview(AvailabilityQuotePreviewResult result)
    {
        if (result is null)
        {
            return DeterministicFallback();
        }

        if (result.Code == AvailabilityQuotePreviewApplication.ApprovalRequiredCode)
        {
            return BlockedResult(
                ApprovalRequiredCode,
                "Approval is required before quote preview can continue.",
                [BlockedTurn(1, ApprovalRequiredCode, "Approval is required before quote preview can continue.", result.EvidenceReferences)]);
        }

        if (result.IsBlocked)
        {
            return BlockedResult(
                IntakeBlockedCode,
                "Availability quote preview was blocked.",
                [BlockedTurn(1, IntakeBlockedCode, "Availability quote preview was blocked.", result.EvidenceReferences)]);
        }

        return BuildResult(
            IsAccepted: true,
            IsBlocked: false,
            AcceptedCode,
            "Availability quote preview accepted.",
            [EvidenceTurn(1, result.Code, result.Summary, result.EvidenceReferences, [])]);
    }

    public LiveTurnPlanTrailProjection BuildPlanTrail(
        IReadOnlyList<ItinerarySlotApplicationResult> itineraryDecisions,
        CardMediaManifest? mediaManifest = null)
    {
        if (itineraryDecisions is null)
        {
            return new(false, IntakeBlockedCode, "Plan trail input failed validation.", []);
        }

        var items = itineraryDecisions
            .Where(result => result is not null && result.Decision is not null)
            .Take(MaxTurns)
            .Select(result => TrailItem(result!, mediaManifest))
            .Where(item => item is not null)
            .Cast<LiveTurnPlanTrailItem>()
            .ToArray();

        return new(true, AcceptedCode, "Plan trail projected.", items);
    }

    public LiveTurnProjectionResult ProviderModelFailure(string? providerCode = null)
    {
        var marker = string.IsNullOrWhiteSpace(providerCode) ? ProviderModelBlockedCode : providerCode;
        return BlockedResult(
            ProviderModelBlockedCode,
            "Provider or model output was blocked.",
            [ProviderFailureTurn(1, ProviderModelBlockedCode, "Provider or model output was blocked.", [marker])]);
    }

    public LiveTurnProjectionResult DeterministicFallback()
    {
        return BuildResult(
            IsAccepted: true,
            IsBlocked: false,
            DeterministicFallbackCode,
            "Deterministic fallback turn emitted.",
            [SummaryTurn(1, DeterministicFallbackCode, "Deterministic fallback turn emitted.", [DeterministicFallbackCode], [])]);
    }

    public LiveTurnProjectionResult BuildPendingConfirmationTrace()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        var pending = new MissionPendingConfirmation(
            "/mission/destination_country",
            "RAW_PROMPT_SHOULD_NOT_LEAK",
            AuthoritySource.StrongModelInference,
            "requires_confirmation",
            ["evidence-pending-country"]);
        var memory = new StructuredMemoryDigest(
            "digest-live-pending",
            session.SessionId,
            session.Mission.MissionId,
            ["purpose: pending confirmation fixture", "traveler_count: 1"],
            [pending],
            ["evidence-pending-country"]);
        var prompt = new PromptPacketBuilder().Build(session, new PromptIntakeRequest(
            session.SessionId,
            "RAW_PROMPT_SHOULD_NOT_LEAK user wants a trip but country came from model.",
            memory,
            "en-US",
            ["pending-confirmation"]));

        var result = FromPromptIntake(prompt);
        return result with
        {
            Transcript = result.Transcript with
            {
                TranscriptId = PendingConfirmationTraceId,
                Scenario = "pending_confirmation",
                TranscriptHash = Hash(result.Transcript.Turns, "pending_confirmation")
            }
        };
    }

    private LiveTurnProjectionResult BlockedResult(string code, string summary, IReadOnlyList<LiveTurn> turns)
    {
        return BuildResult(false, true, code, summary, turns);
    }

    private LiveTurnProjectionResult BuildResult(
        bool IsAccepted,
        bool IsBlocked,
        string code,
        string summary,
        IReadOnlyList<LiveTurn> turns)
    {
        var boundedTurns = turns.Take(MaxTurns).Select(SanitizeTurn).ToArray();
        var evidence = boundedTurns
            .SelectMany(turn => turn.EvidenceReferences)
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Distinct(StringComparer.Ordinal)
            .Take(MaxEvidenceReferences)
            .ToArray();
        var transcript = new LiveTurnTranscript(
            TranscriptId: SafeId($"live-turn-{code}"),
            Scenario: SafeText(code),
            Code: SafeText(code),
            Summary: SafeText(summary),
            ObservedAt: FixedObservedAt,
            Turns: boundedTurns,
            EvidenceReferences: evidence,
            TranscriptHash: Hash(boundedTurns, code));

        return new(
            IsAccepted,
            IsBlocked,
            SafeText(code),
            SafeText(summary),
            transcript);
    }

    private static LiveTurn SummaryTurn(
        int index,
        string code,
        string summary,
        IReadOnlyList<string> markers,
        IReadOnlyList<string> evidenceReferences)
    {
        return new(
            TurnId: $"turn-{index:00}",
            Actor: "assistant",
            Kind: "summary",
            Stage: "live",
            OutcomeCode: code,
            Summary: summary,
            WorkItem: new LiveTurnWorkItem(
                WorkItemId: $"work-{index:00}-summary",
                Kind: "summary",
                Status: code,
                Fields: [],
                Choices: [],
                Approval: null,
                Blocked: null,
                Summary: new LiveTurnSummary(markers.Take(MaxMarkers).ToArray()),
                Evidence: null,
                ProviderFailure: null),
            EvidenceReferences: evidenceReferences,
            Markers: markers);
    }

    private static LiveTurn FormTurn(
        int index,
        string code,
        string summary,
        FormRequest form)
    {
        var fields = form.Fields
            .Take(MaxFields)
            .Select(field => new LiveTurnField(
                field.FieldId,
                field.Label,
                field.FieldType,
                field.Required,
                field.Options.Take(MaxMarkers).ToArray()))
            .ToArray();

        return new(
            TurnId: $"turn-{index:00}",
            Actor: "assistant",
            Kind: "form",
            Stage: "live",
            OutcomeCode: code,
            Summary: summary,
            WorkItem: new LiveTurnWorkItem(
                WorkItemId: SafeId(form.FormId),
                Kind: "form",
                Status: code,
                Fields: fields,
                Choices: [],
                Approval: null,
                Blocked: null,
                Summary: null,
                Evidence: null,
                ProviderFailure: null),
            EvidenceReferences: [],
            Markers: [SafeText(form.SubmitLabel)]);
    }

    private static LiveTurn FormTurn(
        int index,
        string code,
        string summary,
        IReadOnlyList<PromptPendingConfirmation> pending,
        IReadOnlyList<string> evidenceReferences)
    {
        var fields = pending
            .Take(MaxFields)
            .Select(item => new LiveTurnField(
                FieldId: SafeId(item.FieldPath),
                Label: SafeText(item.FieldPath),
                FieldType: "confirmation",
                Required: true,
                Options: ["confirm", "correct", "defer"]))
            .ToArray();

        return new(
            TurnId: $"turn-{index:00}",
            Actor: "assistant",
            Kind: "form",
            Stage: "live",
            OutcomeCode: code,
            Summary: summary,
            WorkItem: new LiveTurnWorkItem(
                WorkItemId: "work-pending-confirmations",
                Kind: "form",
                Status: code,
                Fields: fields,
                Choices: [],
                Approval: null,
                Blocked: null,
                Summary: null,
                Evidence: null,
                ProviderFailure: null),
            EvidenceReferences: evidenceReferences,
            Markers: ["pending_confirmation"]);
    }

    private LiveTurn ChoiceTurn(int index, string code, string summary, EmitChoiceSetAction choice)
    {
        var options = choice.Choices
            .Take(MaxChoices)
            .Select(candidate => new LiveTurnChoiceOption(
                SafeId(candidate.CandidateId),
                SafeText(candidate.Kind),
                FeelFor(candidate.Kind, candidate.EvidenceIds),
                CleanRefs(candidate.EvidenceIds),
                MediaFor(candidate.Kind, candidate.EvidenceIds, null)))
            .ToArray();

        return new(
            TurnId: $"turn-{index:00}",
            Actor: "assistant",
            Kind: "choice",
            Stage: "live",
            OutcomeCode: code,
            Summary: summary,
            WorkItem: new LiveTurnWorkItem(
                WorkItemId: SafeId(choice.ActionId),
                Kind: "choice",
                Status: code,
                Fields: [],
                Choices: options,
                Approval: null,
                Blocked: null,
                Summary: null,
                Evidence: null,
                ProviderFailure: null),
            EvidenceReferences: options.SelectMany(option => option.EvidenceReferences).ToArray(),
            Markers: ["max_selectable:" + choice.MaxSelectable]);
    }

    private LiveTurn ChoiceEchoTurn(int index, ItinerarySlotDecision decision, CardMediaManifest? mediaManifest)
    {
        var option = new LiveTurnChoiceOption(
            SafeId(decision.CandidateId ?? "deferred"),
            SafeText(decision.CandidateKind?.ToString() ?? decision.SlotKind.ToString()),
            FeelFor(decision.SlotKind.ToString(), decision.EvidenceIds),
            CleanRefs(decision.EvidenceIds),
            decision.CandidateKind is null ? null : MediaFor(decision.CandidateKind.Value.ToString(), decision.EvidenceIds, mediaManifest));

        return new(
            TurnId: $"turn-{index:00}",
            Actor: "user",
            Kind: "choice_echo",
            Stage: "live",
            OutcomeCode: AcceptedCode,
            Summary: "Selected option accepted.",
            WorkItem: new LiveTurnWorkItem(
                WorkItemId: SafeId(decision.DecisionId),
                Kind: "choice_echo",
                Status: AcceptedCode,
                Fields: [],
                Choices: [option],
                Approval: null,
                Blocked: null,
                Summary: null,
                Evidence: null,
                ProviderFailure: null),
            EvidenceReferences: option.EvidenceReferences,
            Markers: [SafeId(decision.SlotId), SafeText(decision.SlotKind.ToString())]);
    }

    private static LiveTurn ApprovalTurn(int index, string code, string summary, ApprovalRequest approval)
    {
        return new(
            TurnId: $"turn-{index:00}",
            Actor: "assistant",
            Kind: "approval",
            Stage: "live",
            OutcomeCode: code,
            Summary: summary,
            WorkItem: new LiveTurnWorkItem(
                WorkItemId: SafeId(approval.ApprovalId),
                Kind: "approval",
                Status: code,
                Fields: [],
                Choices: [],
                Approval: new LiveTurnApproval(
                    SafeId(approval.ApprovalId),
                    SafeId(approval.ActionId),
                    CleanRefs(approval.RiskFlags),
                    RequiresUserApproval: true),
                Blocked: null,
                Summary: null,
                Evidence: null,
                ProviderFailure: null),
            EvidenceReferences: [],
            Markers: ["approval_required"]);
    }

    private static LiveTurn BlockedTurn(int index, string code, string summary, IReadOnlyList<string> evidenceReferences)
    {
        return new(
            TurnId: $"turn-{index:00}",
            Actor: "harness",
            Kind: "blocked",
            Stage: "live",
            OutcomeCode: code,
            Summary: summary,
            WorkItem: new LiveTurnWorkItem(
                WorkItemId: $"work-{index:00}-blocked",
                Kind: "blocked",
                Status: code,
                Fields: [],
                Choices: [],
                Approval: null,
                Blocked: new LiveTurnBlockedNotice(code, "safety", summary),
                Summary: null,
                Evidence: null,
                ProviderFailure: null),
            EvidenceReferences: evidenceReferences,
            Markers: [code]);
    }

    private static LiveTurn ProviderFailureTurn(int index, string code, string summary, IReadOnlyList<string> evidenceReferences)
    {
        return new(
            TurnId: $"turn-{index:00}",
            Actor: "harness",
            Kind: "provider_failure",
            Stage: "live",
            OutcomeCode: ProviderModelBlockedCode,
            Summary: summary,
            WorkItem: new LiveTurnWorkItem(
                WorkItemId: $"work-{index:00}-provider-failure",
                Kind: "provider_failure",
                Status: ProviderModelBlockedCode,
                Fields: [],
                Choices: [],
                Approval: null,
                Blocked: null,
                Summary: null,
                Evidence: null,
                ProviderFailure: new LiveTurnProviderFailure(code, summary)),
            EvidenceReferences: evidenceReferences,
            Markers: [ProviderModelBlockedCode]);
    }

    private LiveTurn EvidenceTurn(
        int index,
        string code,
        string summary,
        IReadOnlyList<string> evidenceReferences,
        IReadOnlyList<CardMediaReference> media)
    {
        var safeMedia = CleanMedia(media);
        return new(
            TurnId: $"turn-{index:00}",
            Actor: "harness",
            Kind: "evidence",
            Stage: "live",
            OutcomeCode: code,
            Summary: summary,
            WorkItem: new LiveTurnWorkItem(
                WorkItemId: $"work-{index:00}-evidence",
                Kind: "evidence",
                Status: code,
                Fields: [],
                Choices: [],
                Approval: null,
                Blocked: null,
                Summary: null,
                Evidence: new LiveTurnEvidenceStrip(CleanRefs(evidenceReferences), [], safeMedia),
                ProviderFailure: null),
            EvidenceReferences: evidenceReferences,
            Markers: [code]);
    }

    private LiveTurn SanitizeTurn(LiveTurn turn)
    {
        return turn with
        {
            TurnId = SafeId(turn.TurnId),
            Actor = SafeText(turn.Actor),
            Kind = SafeText(turn.Kind),
            Stage = SafeText(turn.Stage),
            OutcomeCode = SafeText(turn.OutcomeCode),
            Summary = SafeText(turn.Summary),
            WorkItem = SanitizeWorkItem(turn.WorkItem),
            EvidenceReferences = CleanRefs(turn.EvidenceReferences),
            Markers = CleanRefs(turn.Markers)
        };
    }

    private LiveTurnWorkItem? SanitizeWorkItem(LiveTurnWorkItem? item)
    {
        return item is null
            ? null
            : item with
            {
                WorkItemId = SafeId(item.WorkItemId),
                Kind = SafeText(item.Kind),
                Status = SafeText(item.Status),
                Fields = item.Fields.Take(MaxFields).Select(SanitizeField).ToArray(),
                Choices = item.Choices.Take(MaxChoices).Select(SanitizeChoice).ToArray(),
                Approval = item.Approval is null
                    ? null
                    : item.Approval with
                    {
                        ApprovalId = SafeId(item.Approval.ApprovalId),
                        ActionId = SafeId(item.Approval.ActionId),
                        RiskFlags = CleanRefs(item.Approval.RiskFlags)
                    },
                Blocked = item.Blocked is null
                    ? null
                    : item.Blocked with
                    {
                        Code = SafeText(item.Blocked.Code),
                        Category = SafeText(item.Blocked.Category),
                        Summary = SafeText(item.Blocked.Summary)
                    },
                Summary = item.Summary is null
                    ? null
                    : item.Summary with { Markers = CleanRefs(item.Summary.Markers) },
                Evidence = item.Evidence is null
                    ? null
                    : item.Evidence with
                    {
                        EvidenceReferences = CleanRefs(item.Evidence.EvidenceReferences),
                        TraceReferences = CleanRefs(item.Evidence.TraceReferences),
                        Media = CleanMedia(item.Evidence.Media)
                    },
                ProviderFailure = item.ProviderFailure is null
                    ? null
                    : item.ProviderFailure with
                    {
                        Code = SafeText(item.ProviderFailure.Code),
                        Summary = SafeText(item.ProviderFailure.Summary)
                    }
            };
    }

    private static LiveTurnField SanitizeField(LiveTurnField field)
    {
        return field with
        {
            FieldId = SafeId(field.FieldId),
            Label = SafeText(field.Label),
            FieldType = SafeText(field.FieldType),
            Options = CleanRefs(field.Options)
        };
    }

    private LiveTurnChoiceOption SanitizeChoice(LiveTurnChoiceOption option)
    {
        return option with
        {
            CandidateId = SafeId(option.CandidateId),
            Kind = SafeText(option.Kind),
            GroupFeel = option.GroupFeel is null ? null : SafeText(option.GroupFeel),
            EvidenceReferences = CleanRefs(option.EvidenceReferences),
            Media = CleanMedia(option.Media)
        };
    }

    private LiveTurnPlanTrailItem? TrailItem(ItinerarySlotApplicationResult result, CardMediaManifest? mediaManifest)
    {
        if (result.Decision is null)
        {
            return null;
        }

        var decision = result.Decision;
        return new LiveTurnPlanTrailItem(
            ItemId: SafeId(decision.DecisionId),
            Kind: decision.Kind is ItinerarySlotDecisionKind.Selected ? "selected_card" : "deferred_card",
            Status: SafeText(result.Code),
            SlotId: SafeId(decision.SlotId),
            CandidateId: decision.CandidateId is null ? null : SafeId(decision.CandidateId),
            EvidenceReferences: CleanRefs(decision.EvidenceIds),
            Media: decision.CandidateKind is null ? null : MediaFor(decision.CandidateKind.Value.ToString(), decision.EvidenceIds, mediaManifest));
    }

    private CardMediaReference? MediaFor(string candidateKind, IReadOnlyList<string> evidenceIds, CardMediaManifest? mediaManifest)
    {
        if (!Enum.TryParse<CandidateKind>(candidateKind, ignoreCase: true, out var kind))
        {
            return null;
        }

        return _media.MediaForCandidate(mediaManifest ?? _media.BuildJapanMoodMediaManifest(), kind, evidenceIds);
    }

    private IReadOnlyList<CardMediaReference> CleanMedia(IEnumerable<CardMediaReference>? media)
    {
        return media?
            .Select(item => _media.Validate(item))
            .Where(result => result.IsAccepted && result.Media is not null)
            .Select(result => result.Media!)
            .Take(MaxEvidenceReferences)
            .ToArray()
            ?? [];
    }

    private CardMediaReference? CleanMedia(CardMediaReference? media)
    {
        if (media is null)
        {
            return null;
        }

        var result = _media.Validate(media);
        return result.IsAccepted ? result.Media : null;
    }

    private static IReadOnlyList<string> TraceEvidence(IReadOnlyList<SessionTraceEvent> trace)
    {
        return trace.Select(item => item.EventId).ToArray();
    }

    private static IReadOnlyList<string> CleanRefs(IEnumerable<string>? values)
    {
        return values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(SafeId)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxEvidenceReferences)
            .ToArray()
            ?? [];
    }

    private static string? FeelFor(string kind, IReadOnlyList<string> evidenceIds)
    {
        var normalized = kind.ToLowerInvariant();
        if (normalized.Contains("meal", StringComparison.Ordinal) || normalized.Contains("restaurant", StringComparison.Ordinal))
        {
            return "meal";
        }

        if (normalized.Contains("downtime", StringComparison.Ordinal))
        {
            return "quiet";
        }

        if (normalized.Contains("transit", StringComparison.Ordinal) || normalized.Contains("flight", StringComparison.Ordinal))
        {
            return "logistics";
        }

        if (evidenceIds.Any(id => id.Contains("family", StringComparison.OrdinalIgnoreCase)))
        {
            return "family_support";
        }

        return normalized.Contains("activity", StringComparison.Ordinal) ? "activity" : null;
    }

    private static string SafeId(string? value)
    {
        var text = SafeText(value);
        if (text == "redacted")
        {
            return text;
        }

        var normalized = new string(text
            .Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or ':' ? character : '-')
            .ToArray());
        return string.IsNullOrWhiteSpace(normalized) ? "redacted" : normalized;
    }

    private static string SafeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "redacted";
        }

        if (UnsafeFragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            return "redacted";
        }

        var trimmed = value.Trim();
        return trimmed.Length <= MaxTextLength ? trimmed : trimmed[..MaxTextLength];
    }

    private static string Hash(IReadOnlyList<LiveTurn> turns, string scope)
    {
        var json = JsonSerializer.Serialize(new { scope, turns }, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }
}

public sealed record LiveTurnProjectionResult(
    bool IsAccepted,
    bool IsBlocked,
    string Code,
    string Summary,
    LiveTurnTranscript Transcript);

public sealed record LiveTurnTranscript(
    string TranscriptId,
    string Scenario,
    string Code,
    string Summary,
    DateTimeOffset ObservedAt,
    IReadOnlyList<LiveTurn> Turns,
    IReadOnlyList<string> EvidenceReferences,
    string TranscriptHash);

public sealed record LiveTurn(
    string TurnId,
    string Actor,
    string Kind,
    string Stage,
    string OutcomeCode,
    string Summary,
    LiveTurnWorkItem? WorkItem,
    IReadOnlyList<string> EvidenceReferences,
    IReadOnlyList<string> Markers);

public sealed record LiveTurnWorkItem(
    string WorkItemId,
    string Kind,
    string Status,
    IReadOnlyList<LiveTurnField> Fields,
    IReadOnlyList<LiveTurnChoiceOption> Choices,
    LiveTurnApproval? Approval,
    LiveTurnBlockedNotice? Blocked,
    LiveTurnSummary? Summary,
    LiveTurnEvidenceStrip? Evidence,
    LiveTurnProviderFailure? ProviderFailure);

public sealed record LiveTurnField(
    string FieldId,
    string Label,
    string FieldType,
    bool Required,
    IReadOnlyList<string> Options);

public sealed record LiveTurnChoiceOption(
    string CandidateId,
    string Kind,
    string? GroupFeel,
    IReadOnlyList<string> EvidenceReferences,
    CardMediaReference? Media = null);

public sealed record LiveTurnApproval(
    string ApprovalId,
    string ActionId,
    IReadOnlyList<string> RiskFlags,
    bool RequiresUserApproval);

public sealed record LiveTurnBlockedNotice(
    string Code,
    string Category,
    string Summary);

public sealed record LiveTurnSummary(IReadOnlyList<string> Markers);

public sealed record LiveTurnEvidenceStrip(
    IReadOnlyList<string> EvidenceReferences,
    IReadOnlyList<string> TraceReferences,
    IReadOnlyList<CardMediaReference> Media);

public sealed record LiveTurnProviderFailure(
    string Code,
    string Summary);

public sealed record LiveTurnPlanTrailProjection(
    bool IsAccepted,
    string Code,
    string Summary,
    IReadOnlyList<LiveTurnPlanTrailItem> Items);

public sealed record LiveTurnPlanTrailItem(
    string ItemId,
    string Kind,
    string Status,
    string SlotId,
    string? CandidateId,
    IReadOnlyList<string> EvidenceReferences,
    CardMediaReference? Media);
