using System.Text.Json.Serialization;

namespace Pch.Core;

public static class PlannerPrimitiveIds
{
    public const string AssistantMessage = "assistant_message";
    public const string StatusNotice = "status_notice";
    public const string TextInput = "text_input";
    public const string Textarea = "textarea";
    public const string NumberInput = "number_input";
    public const string Slider = "slider";
    public const string Date = "date";
    public const string DateRange = "date_range";
    public const string NumberRange = "number_range";
    public const string BudgetRange = "budget_range";
    public const string RadioGroup = "radio_group";
    public const string Select = "select";
    public const string SingleSelect = "single_select";
    public const string MultiSelect = "multi_select";
    public const string Checkbox = "checkbox";
    public const string ChoiceCard = "choice_card";
    public const string RankedChoice = "ranked_choice";
    public const string CandidateDeck = "candidate_deck";
    public const string ConfirmationQuestion = "confirmation_question";
    public const string ApprovalGate = "approval_gate";
    public const string AvailabilityPreview = "availability_preview";
    public const string EvidenceStrip = "evidence_strip";
    public const string TimelineAnchor = "timeline_anchor";
    public const string EditPatchRequest = "edit_patch_request";
    public const string TaskList = "task_list";
    public const string TaskGroup = "task_group";
    public const string TaskDecomposition = "task_decomposition";
    public const string TimelineItem = "timeline_item";
    public const string ToolSearchRequest = "tool_search_request";
    public const string ToolGapRequest = "tool_gap_request";
    public const string ToolContextReference = "tool_context_reference";

    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal)
    {
        AssistantMessage,
        StatusNotice,
        TextInput,
        Textarea,
        NumberInput,
        Slider,
        Date,
        DateRange,
        NumberRange,
        BudgetRange,
        RadioGroup,
        Select,
        SingleSelect,
        MultiSelect,
        Checkbox,
        ChoiceCard,
        RankedChoice,
        CandidateDeck,
        ConfirmationQuestion,
        ApprovalGate,
        AvailabilityPreview,
        EvidenceStrip,
        TimelineAnchor,
        EditPatchRequest,
        TaskList,
        TaskGroup,
        TaskDecomposition,
        TimelineItem,
        ToolSearchRequest,
        ToolGapRequest,
        ToolContextReference
    };
}

public static class PlannerRendererKeys
{
    public const string AssistantMessage = "assistant-message";
    public const string StatusNotice = "status-notice";
    public const string TextInput = "text-input";
    public const string Textarea = "textarea";
    public const string NumberInput = "number-input";
    public const string Slider = "slider";
    public const string Date = "date";
    public const string DateRange = "date-range";
    public const string RadioGroup = "radio-group";
    public const string Select = "select";
    public const string MultiSelect = "multi-select";
    public const string Checkbox = "checkbox";
    public const string ChoiceCard = "choice-card";
    public const string CandidateDeck = "candidate-deck";
    public const string TaskDecomposition = "task-decomposition";
    public const string TimelineItem = "timeline-item";
    public const string ToolSearchRequest = "tool-search-request";
    public const string ToolGapRequest = "tool-gap-request";
}

public static class PlannerAnswerValueKinds
{
    public const string None = "none";
    public const string Text = "text";
    public const string Number = "number";
    public const string Boolean = "boolean";
    public const string Date = "date";
    public const string DateRange = "date_range";
    public const string SingleChoice = "single_choice";
    public const string MultiChoice = "multi_choice";
    public const string TaskDecomposition = "task_decomposition";
}

public static class PlannerMoodTokens
{
    public const string ReflectiveCulture = "reflective_culture";
    public const string SoftNature = "soft_nature";
    public const string LivelyFood = "lively_food";
    public const string CalmMorning = "calm_morning";
    public const string RestorativeDowntime = "restorative_downtime";
    public const string Logistics = "logistics";
    public const string FamilySupport = "family_support";
    public const string Neutral = "neutral";

    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal)
    {
        ReflectiveCulture,
        SoftNature,
        LivelyFood,
        CalmMorning,
        RestorativeDowntime,
        Logistics,
        FamilySupport,
        Neutral
    };
}

public static class PlannerAnswerSchemaKinds
{
    public const string None = "none";
    public const string Text = "text";
    public const string Date = "date";
    public const string Boolean = "boolean";
    public const string DateRange = "date_range";
    public const string NumberRange = "number_range";
    public const string Number = "number";
    public const string SingleSelect = "single_select";
    public const string MultiSelect = "multi_select";
    public const string RankedChoice = "ranked_choice";
    public const string Confirmation = "confirmation";
    public const string TaskDecomposition = "task_decomposition";
}

public sealed record PlannerAnswerSchema(
    string Kind,
    bool Required,
    int? MinItems,
    int? MaxItems,
    IReadOnlyList<string> Options);

public sealed record PlannerPrimitiveDefinition(
    string PrimitiveId,
    int SchemaVersion,
    string RendererKey,
    IReadOnlyList<string> StageEligibility,
    PlannerAnswerSchema AnswerSchema,
    IReadOnlyList<string> AllowedFieldPaths,
    bool SupportsMood,
    bool SupportsMedia);

public sealed record PlannerCompositeFormDefinition(
    string FormId,
    int SchemaVersion,
    string RendererKey,
    IReadOnlyList<string> PrimitiveInstanceIds);

public sealed record PlannerToolManifest(
    string ManifestId,
    int SchemaVersion,
    string GraphRevision,
    string SessionId,
    string Stage,
    IReadOnlyList<PlannerPrimitiveDefinition> AllowedPrimitives,
    IReadOnlyList<PlannerCompositeFormDefinition> CompositeForms,
    IReadOnlyList<string> AllowedFieldPaths,
    IReadOnlyList<string> AllowedSlotIds,
    IReadOnlyList<string> AllowedCandidateIds,
    IReadOnlyList<string> AllowedTaskIds,
    IReadOnlyList<string> AllowedMoodTokens,
    IReadOnlyList<string> AllowedMediaTokens,
    int MaxPrimitiveCount,
    int MaxTextLength,
    bool AllowsApproval,
    bool AllowsSpend)
{
    public int MaxOptionCount { get; init; } = 8;

    public IReadOnlyList<string> AllowedToolIds { get; init; } = [];

    public IReadOnlyList<string> AllowedToolContextRefs { get; init; } = [];
}

public sealed record PlannerPrimitiveOption(
    string OptionId,
    string Label,
    string? Summary,
    string? MoodToken,
    string? MediaToken,
    IReadOnlyList<string> EvidenceReferences,
    IReadOnlyList<string> ToolContextReferences);

public sealed record PlannerPrimitiveDefault(
    string FieldKey,
    string? Value);

public sealed record PlannerPrimitiveValidationRule(
    string RuleId,
    string Kind,
    string? Value,
    string Code);

public sealed record PlannerRendererHint(
    string Key,
    string? Value);

public sealed record PlannerTaskReference(
    string TaskId,
    string Label,
    string? GroupId,
    IReadOnlyList<string> EvidenceReferences,
    IReadOnlyList<string> ToolContextReferences);

public static class PlannerTaskStates
{
    public const string Pending = "pending";
    public const string Active = "active";
    public const string Blocked = "blocked";
    public const string Complete = "complete";

    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal)
    {
        Pending,
        Active,
        Blocked,
        Complete
    };
}

public sealed record PlannerTaskDecompositionItem(
    string TaskId,
    string Title,
    string State,
    int Order,
    IReadOnlyList<string> DependencyTaskIds,
    IReadOnlyList<string> EvidenceReferences);

public sealed record PlannerToolContextReference(
    string ReferenceId,
    string SourceClass,
    string Summary,
    IReadOnlyList<string> EvidenceReferences);

public sealed record PlannerPrimitiveAnswer(
    string AnswerId,
    string PrimitiveInstanceId,
    string PrimitiveId,
    IReadOnlyDictionary<string, string?> Values,
    IReadOnlyList<string> SelectedOptionIds,
    IReadOnlyList<string> EvidenceReferences);

public sealed record PlannerPrimitiveInstance(
    string InstanceId,
    string PrimitiveId,
    int SchemaVersion,
    string RendererKey,
    string? Label,
    string? Prompt,
    string? FieldPath,
    string? SlotId,
    string? CandidateId,
    string? TaskId,
    string? MoodToken,
    string? MediaToken,
    PlannerAnswerSchema AnswerSchema,
    IReadOnlyDictionary<string, string?> Answers,
    IReadOnlyList<string> EvidenceReferences,
    IReadOnlyList<string> DependencyReferences)
{
    public string? HelpText { get; init; }

    public IReadOnlyList<PlannerPrimitiveOption> Options { get; init; } = [];

    public IReadOnlyList<PlannerPrimitiveDefault> Defaults { get; init; } = [];

    public IReadOnlyList<PlannerTaskReference> TaskReferences { get; init; } = [];

    public IReadOnlyList<PlannerTaskDecompositionItem> TaskDecomposition { get; init; } = [];

    public IReadOnlyList<string> ToolContextReferences { get; init; } = [];

    public IReadOnlyList<PlannerPrimitiveValidationRule> ValidationRules { get; init; } = [];

    public IReadOnlyList<PlannerRendererHint> RendererHints { get; init; } = [];
}

public sealed record PlannerPrimitiveTurnProposal(
    string ProposalId,
    string ManifestId,
    int SchemaVersion,
    string GraphRevision,
    string SessionId,
    string Stage,
    IReadOnlyList<PlannerPrimitiveInstance> Primitives);

public sealed record ValidatedTurnView(
    string TurnId,
    string SessionId,
    string GraphRevision,
    string Source,
    string Code,
    IReadOnlyList<ValidatedPrimitiveView> Primitives,
    IReadOnlyList<string> TaskRailItemRefs,
    IReadOnlyList<string> TimelineAnchorRefs,
    IReadOnlyList<string> EvidenceReferences,
    string SanitizationStatus)
{
    public string PrimitiveHash { get; init; } = "redacted";

    public string ProviderOutputHash { get; init; } = "provider-output-absent";

    public IReadOnlyList<string> RenderedPrimitiveIds { get; init; } = [];

    public IReadOnlyList<string> AnswerIds { get; init; } = [];

    public IReadOnlyList<string> ToolContextReferences { get; init; } = [];
}

public sealed record ValidatedPrimitiveView(
    string InstanceId,
    string PrimitiveId,
    string RendererKey,
    string? Label,
    string? Prompt,
    string? FieldPath,
    string? SlotId,
    string? CandidateId,
    string? TaskId,
    string? MoodToken,
    string? MediaToken,
    PlannerAnswerSchema AnswerSchema,
    IReadOnlyDictionary<string, string?> Answers,
    IReadOnlyList<string> EvidenceReferences)
{
    public string? HelpText { get; init; }

    public IReadOnlyList<PlannerPrimitiveOption> Options { get; init; } = [];

    public IReadOnlyList<PlannerPrimitiveDefault> Defaults { get; init; } = [];

    public IReadOnlyList<PlannerTaskReference> TaskReferences { get; init; } = [];

    public IReadOnlyList<PlannerTaskDecompositionItem> TaskDecomposition { get; init; } = [];

    public IReadOnlyList<string> ToolContextReferences { get; init; } = [];

    public IReadOnlyList<PlannerPrimitiveValidationRule> ValidationRules { get; init; } = [];

    public IReadOnlyList<PlannerRendererHint> RendererHints { get; init; } = [];
}

public sealed record PlannerTurnContextRequest(
    string SessionId,
    [property: JsonIgnore] string TransientRawPrompt,
    string? Locale,
    IReadOnlyList<string> ScenarioHints);

public sealed record PlannerAcceptedFact(
    string FactId,
    string FieldPath,
    string Value,
    IReadOnlyList<string> EvidenceReferences);

public sealed record PlannerValidatedTurnSummary(
    string TurnId,
    string Code,
    string PrimitiveHash,
    IReadOnlyList<string> RenderedPrimitiveIds,
    IReadOnlyList<string> TaskIds,
    IReadOnlyList<string> AnswerIds);

public sealed record PlannerTurnContext(
    string ContextId,
    string SessionId,
    string GraphRevision,
    string Stage,
    string PromptCategory,
    int PromptLength,
    string PromptSha256,
    IReadOnlyList<PlannerAcceptedFact> AcceptedFacts,
    IReadOnlyList<PlannerPrimitiveAnswer> SubmittedAnswers,
    IReadOnlyList<PlannerValidatedTurnSummary> ValidatedTurnSummaries,
    IReadOnlyList<PlannerToolContextReference> ToolContextReferences,
    PlannerToolManifest Manifest);

public sealed record PlannerAnswerApplicationRequest(
    string SessionId,
    string GraphRevision,
    IReadOnlyList<PlannerPrimitiveAnswer> Answers);

public sealed record PlannerAnswerApplicationResult(
    bool IsAccepted,
    bool IsBlocked,
    string Code,
    string Summary,
    IReadOnlyList<PlannerPrimitiveAnswer> AppliedAnswers);

public sealed record PlannerDevelopmentDiagnostics(
    string DiagnosticId,
    string SessionId,
    string Stage,
    string ProviderStateCode,
    string HarnessStateCode,
    string UiStateCode,
    string? LatestTurnId,
    string? LatestRequestId,
    string? LatestPrimitiveId,
    IReadOnlyList<string> SafeTraceReferences);
