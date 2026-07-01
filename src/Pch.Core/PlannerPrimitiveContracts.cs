namespace Pch.Core;

public static class PlannerPrimitiveIds
{
    public const string AssistantMessage = "assistant_message";
    public const string TextInput = "text_input";
    public const string Textarea = "textarea";
    public const string DateRange = "date_range";
    public const string NumberRange = "number_range";
    public const string BudgetRange = "budget_range";
    public const string SingleSelect = "single_select";
    public const string MultiSelect = "multi_select";
    public const string RankedChoice = "ranked_choice";
    public const string CandidateDeck = "candidate_deck";
    public const string ConfirmationQuestion = "confirmation_question";
    public const string ApprovalGate = "approval_gate";
    public const string AvailabilityPreview = "availability_preview";
    public const string EvidenceStrip = "evidence_strip";
    public const string TimelineAnchor = "timeline_anchor";
    public const string EditPatchRequest = "edit_patch_request";
    public const string ToolSearchRequest = "tool_search_request";
    public const string ToolGapRequest = "tool_gap_request";

    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal)
    {
        AssistantMessage,
        TextInput,
        Textarea,
        DateRange,
        NumberRange,
        BudgetRange,
        SingleSelect,
        MultiSelect,
        RankedChoice,
        CandidateDeck,
        ConfirmationQuestion,
        ApprovalGate,
        AvailabilityPreview,
        EvidenceStrip,
        TimelineAnchor,
        EditPatchRequest,
        ToolSearchRequest,
        ToolGapRequest
    };
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
    public const string DateRange = "date_range";
    public const string NumberRange = "number_range";
    public const string SingleSelect = "single_select";
    public const string MultiSelect = "multi_select";
    public const string RankedChoice = "ranked_choice";
    public const string Confirmation = "confirmation";
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
    bool AllowsSpend);

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
    IReadOnlyList<string> DependencyReferences);

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
    string SanitizationStatus);

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
    IReadOnlyList<string> EvidenceReferences);
