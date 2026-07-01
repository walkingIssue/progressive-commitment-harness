using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pch.Core;

namespace Pch.Harness;

public sealed class PlannerToolManifestCompiler
{
    public const int ManifestSchemaVersion = 1;

    private const int MaxPrimitiveCount = 12;
    private const int MaxTextLength = 160;
    private const int MaxOptionCount = 8;

    private readonly PlanningEditImpactAnalyzer _graph = new();

    public PlannerToolManifest Compile(TripSession session, HarnessStage? stage = null)
    {
        var currentStage = stage ?? session.Stage;
        var snapshot = _graph.BuildSnapshot(session);
        var slots = session.LastItineraryCompilation?.Days
            .SelectMany(day => day.Slots)
            .Select(slot => slot.SlotId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray() ?? [];
        var candidates = session.CandidatePools
            .SelectMany(pool => pool.Candidates)
            .Select(candidate => candidate.CandidateId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        var taskIds = snapshot.Nodes
            .Select(node => node.NodeId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        return new PlannerToolManifest(
            ManifestId: $"planner-manifest-{session.SessionId}-{snapshot.Fingerprint[..12]}".ToLowerInvariant(),
            SchemaVersion: ManifestSchemaVersion,
            GraphRevision: snapshot.Fingerprint,
            SessionId: session.SessionId,
            Stage: currentStage.ToString(),
            AllowedPrimitives: DefinitionsFor(currentStage),
            CompositeForms: CompositeFormsFor(currentStage),
            AllowedFieldPaths: AllowedFieldPaths(),
            AllowedSlotIds: slots,
            AllowedCandidateIds: candidates,
            AllowedTaskIds: taskIds,
            AllowedMoodTokens: PlannerMoodTokens.Known.OrderBy(token => token, StringComparer.Ordinal).ToArray(),
            AllowedMediaTokens: PlannerMoodTokens.Known.Select(token => $"media:{token}").OrderBy(token => token, StringComparer.Ordinal).ToArray(),
            MaxPrimitiveCount: MaxPrimitiveCount,
            MaxTextLength: MaxTextLength,
            AllowsApproval: currentStage is HarnessStage.ApprovalQueue,
            AllowsSpend: false)
        {
            MaxOptionCount = MaxOptionCount,
            AllowedToolIds =
            [
                PlannerPrimitiveIds.ToolSearchRequest,
                PlannerPrimitiveIds.ToolGapRequest,
                PlannerPrimitiveIds.ToolContextReference
            ],
            AllowedToolContextRefs = ToolContextRefs(session, taskIds)
        };
    }

    public string CurrentGraphRevision(TripSession session)
    {
        return _graph.BuildSnapshot(session).Fingerprint;
    }

    private static IReadOnlyList<PlannerPrimitiveDefinition> DefinitionsFor(HarnessStage stage)
    {
        var stages = new[] { stage.ToString() };
        var shared = new List<PlannerPrimitiveDefinition>
        {
            Definition(PlannerPrimitiveIds.AssistantMessage, PlannerRendererKeys.AssistantMessage, stages, PlannerAnswerSchemaKinds.None, [], supportsMood: true, supportsMedia: false),
            Definition(PlannerPrimitiveIds.StatusNotice, PlannerRendererKeys.StatusNotice, stages, PlannerAnswerSchemaKinds.None, [], supportsMood: false, supportsMedia: false),
            Definition(PlannerPrimitiveIds.EvidenceStrip, "evidence-strip", stages, PlannerAnswerSchemaKinds.None, [], supportsMood: false, supportsMedia: false),
            Definition(PlannerPrimitiveIds.TimelineAnchor, "timeline-anchor", stages, PlannerAnswerSchemaKinds.None, [], supportsMood: false, supportsMedia: false),
            Definition(PlannerPrimitiveIds.TimelineItem, PlannerRendererKeys.TimelineItem, stages, PlannerAnswerSchemaKinds.None, [], supportsMood: true, supportsMedia: false),
            Definition(PlannerPrimitiveIds.TaskList, "task-list", stages, PlannerAnswerSchemaKinds.None, [], supportsMood: true, supportsMedia: false),
            Definition(PlannerPrimitiveIds.TaskGroup, "task-group", stages, PlannerAnswerSchemaKinds.None, [], supportsMood: true, supportsMedia: false),
            Definition(PlannerPrimitiveIds.TaskDecomposition, PlannerRendererKeys.TaskDecomposition, stages, PlannerAnswerSchemaKinds.TaskDecomposition, [], supportsMood: true, supportsMedia: false),
            Definition(PlannerPrimitiveIds.ToolSearchRequest, PlannerRendererKeys.ToolSearchRequest, stages, PlannerAnswerSchemaKinds.Text, [], supportsMood: false, supportsMedia: false),
            Definition(PlannerPrimitiveIds.ToolGapRequest, PlannerRendererKeys.ToolGapRequest, stages, PlannerAnswerSchemaKinds.Text, [], supportsMood: false, supportsMedia: false),
            Definition(PlannerPrimitiveIds.ToolContextReference, "tool-context-reference", stages, PlannerAnswerSchemaKinds.None, [], supportsMood: false, supportsMedia: false)
        };

        shared.AddRange(stage switch
        {
            HarnessStage.Intake or HarnessStage.SlotCollection =>
            [
                Definition(PlannerPrimitiveIds.TextInput, PlannerRendererKeys.TextInput, stages, PlannerAnswerSchemaKinds.Text, AllowedFieldPaths(), supportsMood: false, supportsMedia: false),
                Definition(PlannerPrimitiveIds.Textarea, PlannerRendererKeys.Textarea, stages, PlannerAnswerSchemaKinds.Text, AllowedFieldPaths(), supportsMood: false, supportsMedia: false),
                Definition(PlannerPrimitiveIds.NumberInput, PlannerRendererKeys.NumberInput, stages, PlannerAnswerSchemaKinds.Number, NumericFieldPaths(), supportsMood: false, supportsMedia: false),
                Definition(PlannerPrimitiveIds.Slider, PlannerRendererKeys.Slider, stages, PlannerAnswerSchemaKinds.Number, NumericFieldPaths(), supportsMood: true, supportsMedia: false),
                Definition(PlannerPrimitiveIds.Date, PlannerRendererKeys.Date, stages, PlannerAnswerSchemaKinds.Date, ["/mission/start_date", "/mission/end_date"], supportsMood: false, supportsMedia: false),
                Definition(PlannerPrimitiveIds.DateRange, PlannerRendererKeys.DateRange, stages, PlannerAnswerSchemaKinds.DateRange, ["/mission/start_date", "/mission/end_date"], supportsMood: false, supportsMedia: false),
                Definition(PlannerPrimitiveIds.RadioGroup, PlannerRendererKeys.RadioGroup, stages, PlannerAnswerSchemaKinds.SingleSelect, ChoiceFieldPaths(), supportsMood: true, supportsMedia: false),
                Definition(PlannerPrimitiveIds.Select, PlannerRendererKeys.Select, stages, PlannerAnswerSchemaKinds.SingleSelect, ChoiceFieldPaths(), supportsMood: false, supportsMedia: false),
                Definition(PlannerPrimitiveIds.MultiSelect, PlannerRendererKeys.MultiSelect, stages, PlannerAnswerSchemaKinds.MultiSelect, ChoiceFieldPaths(), supportsMood: true, supportsMedia: false),
                Definition(PlannerPrimitiveIds.Checkbox, PlannerRendererKeys.Checkbox, stages, PlannerAnswerSchemaKinds.Boolean, AllowedFieldPaths(), supportsMood: false, supportsMedia: false),
                Definition(PlannerPrimitiveIds.ChoiceCard, PlannerRendererKeys.ChoiceCard, stages, PlannerAnswerSchemaKinds.SingleSelect, ChoiceFieldPaths(), supportsMood: true, supportsMedia: true),
                Definition(PlannerPrimitiveIds.ConfirmationQuestion, "confirmation-question", stages, PlannerAnswerSchemaKinds.Confirmation, AllowedFieldPaths(), supportsMood: true, supportsMedia: false)
            ],
            HarnessStage.DaySkeletonGeneration or HarnessStage.Logistics or HarnessStage.Meals or HarnessStage.ActivitiesDowntime =>
            [
                Definition(PlannerPrimitiveIds.CandidateDeck, PlannerRendererKeys.CandidateDeck, stages, PlannerAnswerSchemaKinds.SingleSelect, [], supportsMood: true, supportsMedia: true),
                Definition(PlannerPrimitiveIds.ChoiceCard, PlannerRendererKeys.ChoiceCard, stages, PlannerAnswerSchemaKinds.SingleSelect, [], supportsMood: true, supportsMedia: true),
                Definition(PlannerPrimitiveIds.RadioGroup, PlannerRendererKeys.RadioGroup, stages, PlannerAnswerSchemaKinds.SingleSelect, [], supportsMood: true, supportsMedia: false),
                Definition(PlannerPrimitiveIds.Select, PlannerRendererKeys.Select, stages, PlannerAnswerSchemaKinds.SingleSelect, [], supportsMood: false, supportsMedia: false),
                Definition(PlannerPrimitiveIds.SingleSelect, "single-select", stages, PlannerAnswerSchemaKinds.SingleSelect, [], supportsMood: true, supportsMedia: false),
                Definition(PlannerPrimitiveIds.MultiSelect, PlannerRendererKeys.MultiSelect, stages, PlannerAnswerSchemaKinds.MultiSelect, [], supportsMood: true, supportsMedia: false),
                Definition(PlannerPrimitiveIds.RankedChoice, "ranked-choice", stages, PlannerAnswerSchemaKinds.RankedChoice, [], supportsMood: true, supportsMedia: true),
                Definition(PlannerPrimitiveIds.AvailabilityPreview, "availability-preview", stages, PlannerAnswerSchemaKinds.None, [], supportsMood: false, supportsMedia: false)
            ],
            HarnessStage.ApprovalQueue =>
            [
                Definition(PlannerPrimitiveIds.ApprovalGate, "approval-gate", stages, PlannerAnswerSchemaKinds.Confirmation, [], supportsMood: false, supportsMedia: false)
            ],
            _ =>
            [
                Definition(PlannerPrimitiveIds.TextInput, PlannerRendererKeys.TextInput, stages, PlannerAnswerSchemaKinds.Text, AllowedFieldPaths(), supportsMood: false, supportsMedia: false),
                Definition(PlannerPrimitiveIds.EditPatchRequest, "edit-patch-request", stages, PlannerAnswerSchemaKinds.Text, AllowedFieldPaths(), supportsMood: false, supportsMedia: false)
            ]
        });

        return shared
            .GroupBy(definition => definition.PrimitiveId, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(definition => definition.PrimitiveId, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<PlannerCompositeFormDefinition> CompositeFormsFor(HarnessStage stage)
    {
        return stage switch
        {
            HarnessStage.Intake or HarnessStage.SlotCollection =>
            [
                new(
                    FormId: "form-trip-basics-v1",
                    SchemaVersion: ManifestSchemaVersion,
                    RendererKey: "composite-trip-basics",
                    PrimitiveInstanceIds: ["purpose-input", "dates-input"])
            ],
            _ => []
        };
    }

    private static PlannerPrimitiveDefinition Definition(
        string primitiveId,
        string rendererKey,
        IReadOnlyList<string> stages,
        string answerKind,
        IReadOnlyList<string> allowedFieldPaths,
        bool supportsMood,
        bool supportsMedia)
    {
        return new(
            primitiveId,
            ManifestSchemaVersion,
            rendererKey,
            stages,
            new PlannerAnswerSchema(answerKind, answerKind is not PlannerAnswerSchemaKinds.None, null, answerKind is PlannerAnswerSchemaKinds.Text ? 1 : null, DefaultOptions(answerKind)),
            allowedFieldPaths,
            supportsMood,
            supportsMedia);
    }

    private static IReadOnlyList<string> DefaultOptions(string answerKind)
    {
        return answerKind switch
        {
            PlannerAnswerSchemaKinds.Confirmation => ["confirm", "correct", "defer"],
            PlannerAnswerSchemaKinds.Boolean => ["checked"],
            PlannerAnswerSchemaKinds.SingleSelect => [],
            PlannerAnswerSchemaKinds.MultiSelect => [],
            PlannerAnswerSchemaKinds.RankedChoice => ["ranked"],
            _ => []
        };
    }

    private static IReadOnlyList<string> AllowedFieldPaths()
    {
        return
        [
            "/mission/purpose",
            "/mission/destination_country",
            "/mission/start_date",
            "/mission/end_date",
            "/constraints/pace",
            "/constraints/budget",
            "/constraints/freeform",
            "/constraints/notes",
            "/constraints/budget_amount",
            "/constraints/max_walking_minutes",
            "/mission/travel_style"
        ];
    }

    private static IReadOnlyList<string> FreeTextFieldPaths()
    {
        return
        [
            "/mission/purpose",
            "/constraints/freeform",
            "/constraints/notes"
        ];
    }

    private static IReadOnlyList<string> ChoiceFieldPaths()
    {
        return
        [
            "/mission/destination_country",
            "/constraints/pace",
            "/constraints/budget",
            "/mission/travel_style"
        ];
    }

    private static IReadOnlyList<string> NumericFieldPaths()
    {
        return
        [
            "/constraints/budget_amount",
            "/constraints/max_walking_minutes"
        ];
    }

    private static IReadOnlyList<string> ToolContextRefs(TripSession session, IReadOnlyList<string> taskIds)
    {
        return taskIds
            .Concat(session.EvidenceTrace.Items.Select(item => item.EvidenceId))
            .Concat(session.MemoryDigest?.TraceReferences ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .Take(24)
            .ToArray();
    }
}

public sealed class PlannerPrimitiveValidator
{
    public const string AcceptedCode = "primitive_turn_accepted";
    public const string AwaitingUserInputCode = "awaiting_user_input";
    public const string ToolSearchRequestedCode = "tool_search_requested";
    public const string ToolGapReviewRequiredCode = "tool_gap_review_required";
    public const string ValidationBlockedCode = "primitive_validation_blocked";
    public const string InvalidManifestCode = "invalid_manifest";
    public const string PrimitiveNotSupportedCode = "primitive_not_supported";
    public const string PrimitiveNotAllowedForStageCode = "primitive_not_allowed_for_stage";
    public const string FieldPathNotAllowedCode = "field_path_not_allowed";
    public const string AnswerSchemaInvalidCode = PrimitiveAnswerSchemaInvalidCode;
    public const string StaleGraphRevisionCode = "stale_graph_revision";
    public const string OwnershipInvalidCode = "ownership_invalid";
    public const string PrimitiveMetadataRedactedCode = "primitive_metadata_redacted";
    public const string PrimitiveTextRedactedCode = "primitive_text_redacted";
    public const string ApprovalRequiredCode = "approval_required";
    public const string ToolNotAllowedCode = "tool_not_allowed";
    public const string PrimitiveOptionLimitExceededCode = "primitive_option_limit_exceeded";
    public const string PrimitiveOptionsMissingCode = "primitive_options_missing";
    public const string AnswerValueNotAllowedCode = "answer_value_not_allowed";
    public const string PrimitiveRendererMismatchCode = "primitive_renderer_mismatch";
    public const string PrimitiveAnswerSchemaInvalidCode = "primitive_answer_schema_invalid";
    public const string TaskDecompositionInvalidCode = "task_decomposition_invalid";
    public const string TaskDecompositionMissingCode = "task_decomposition_missing";
    public const string ToolContextRefInvalidCode = "tool_context_ref_invalid";

    [Obsolete("Use PrimitiveAnswerSchemaInvalidCode.")]
    public const string LegacyAnswerSchemaInvalidCode = "answer_schema_invalid";

    private const int MaxTextLength = 160;
    private const int MaxRefs = 12;

    private readonly PlannerToolManifestCompiler _manifestCompiler = new();

    public PlannerPrimitiveValidationResult Validate(
        TripSession session,
        PlannerToolManifest? manifest,
        PlannerPrimitiveTurnProposal? proposal)
    {
        var manifestValidation = ValidateManifest(session, manifest);
        if (!manifestValidation.IsAccepted)
        {
            return Blocked(manifestValidation.Code, manifestValidation.Summary, manifest);
        }

        if (proposal is null
            || string.IsNullOrWhiteSpace(proposal.ProposalId)
            || proposal.Primitives is null
            || proposal.Primitives.Count == 0
            || proposal.Primitives.Count > manifest!.MaxPrimitiveCount
            || !string.Equals(proposal.ManifestId, manifest.ManifestId, StringComparison.Ordinal)
            || proposal.SchemaVersion != manifest.SchemaVersion
            || !string.Equals(proposal.SessionId, manifest.SessionId, StringComparison.Ordinal)
            || !string.Equals(proposal.Stage, manifest.Stage, StringComparison.Ordinal))
        {
            return Blocked(InvalidManifestCode, "Planner primitive proposal failed validation.", manifest);
        }

        var currentRevision = _manifestCompiler.CurrentGraphRevision(session);
        if (!string.Equals(proposal.GraphRevision, manifest.GraphRevision, StringComparison.Ordinal)
            || !string.Equals(manifest.GraphRevision, currentRevision, StringComparison.Ordinal))
        {
            return Blocked(StaleGraphRevisionCode, "Planner primitive proposal references stale graph state.", manifest);
        }

        var validated = new List<ValidatedPrimitiveView>();
        foreach (var primitive in proposal.Primitives)
        {
            var validation = ValidatePrimitive(session, manifest, primitive);
            if (!validation.IsAccepted || validation.Primitive is null)
            {
                return Blocked(validation.Code, validation.Summary, manifest);
            }

            validated.Add(validation.Primitive);
        }

        var code = validated.All(primitive => primitive.PrimitiveId == PlannerPrimitiveIds.ToolSearchRequest)
            ? ToolSearchRequestedCode
            : validated.All(primitive => primitive.PrimitiveId == PlannerPrimitiveIds.ToolGapRequest)
                ? ToolGapReviewRequiredCode
                : validated.Any(primitive => RequiresAnswer(primitive.AnswerSchema))
                    ? AwaitingUserInputCode
                    : AcceptedCode;
        var primitiveHash = HashValidatedTurn(validated);
        var renderedPrimitiveIds = validated.Select(primitive => primitive.InstanceId).Distinct(StringComparer.Ordinal).Take(MaxRefs).ToArray();
        var taskRailRefs = validated
            .SelectMany(primitive => new[] { primitive.TaskId }.Where(id => !string.IsNullOrWhiteSpace(id)))
            .Concat(validated.SelectMany(primitive => primitive.TaskReferences.Select(task => task.TaskId)))
            .Concat(validated.SelectMany(primitive => primitive.TaskDecomposition.Select(task => task.TaskId)))
            .Select(SafeId)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxRefs)
            .ToArray();
        var answerIds = validated
            .SelectMany(primitive => primitive.Answers.Keys.Select(key => $"{primitive.InstanceId}:{key}"))
            .Select(SafeId)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxRefs)
            .ToArray();
        var toolContextRefs = validated
            .SelectMany(primitive => primitive.ToolContextReferences)
            .Concat(validated.SelectMany(primitive => primitive.Options.SelectMany(option => option.ToolContextReferences)))
            .Concat(validated.SelectMany(primitive => primitive.TaskReferences.SelectMany(task => task.ToolContextReferences)))
            .Distinct(StringComparer.Ordinal)
            .Take(MaxRefs)
            .ToArray();
        var view = new ValidatedTurnView(
            TurnId: $"validated-turn-{SafeId(proposal.ProposalId)}",
            SessionId: SafeId(session.SessionId),
            GraphRevision: manifest.GraphRevision,
            Source: "live_provider_candidate",
            Code: code,
            Primitives: validated,
            TaskRailItemRefs: taskRailRefs,
            TimelineAnchorRefs: validated.Where(primitive => primitive.PrimitiveId == PlannerPrimitiveIds.TimelineAnchor).Select(primitive => primitive.InstanceId).Take(MaxRefs).ToArray(),
            EvidenceReferences: validated.SelectMany(primitive => primitive.EvidenceReferences).Distinct(StringComparer.Ordinal).Take(MaxRefs).ToArray(),
            SanitizationStatus: "sanitized")
        {
            PrimitiveHash = primitiveHash,
            ProviderOutputHash = primitiveHash,
            RenderedPrimitiveIds = renderedPrimitiveIds,
            AnswerIds = answerIds,
            ToolContextReferences = toolContextRefs
        };

        return new(true, false, code, SummaryFor(code), view);
    }

    private PlannerPrimitiveValidation ValidateManifest(TripSession session, PlannerToolManifest? manifest)
    {
        if (manifest is null
            || manifest.AllowedPrimitives is null
            || manifest.CompositeForms is null
            || manifest.AllowedFieldPaths is null
            || manifest.AllowedSlotIds is null
            || manifest.AllowedCandidateIds is null
            || manifest.AllowedTaskIds is null
            || manifest.AllowedMoodTokens is null
            || manifest.AllowedMediaTokens is null
            || manifest.AllowedToolIds is null
            || manifest.AllowedToolContextRefs is null
            || manifest.SchemaVersion != PlannerToolManifestCompiler.ManifestSchemaVersion
            || string.IsNullOrWhiteSpace(manifest.ManifestId)
            || string.IsNullOrWhiteSpace(manifest.GraphRevision)
            || string.IsNullOrWhiteSpace(manifest.SessionId)
            || !string.Equals(manifest.SessionId, session.SessionId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(manifest.Stage)
            || manifest.AllowedPrimitives.Count == 0
            || manifest.MaxPrimitiveCount <= 0
            || manifest.MaxTextLength <= 0)
        {
            return Reject(InvalidManifestCode, "Planner tool manifest failed validation.");
        }

        if (manifest.AllowedPrimitives.Any(definition => definition is null
                || string.IsNullOrWhiteSpace(definition.PrimitiveId)
                || !PlannerPrimitiveIds.Known.Contains(definition.PrimitiveId)
                || definition.SchemaVersion != manifest.SchemaVersion
                || definition.StageEligibility is null
                || !definition.StageEligibility.Contains(manifest.Stage, StringComparer.Ordinal)
                || definition.AnswerSchema is null))
        {
            return Reject(InvalidManifestCode, "Planner tool manifest failed validation.");
        }

        if (!string.Equals(manifest.GraphRevision, _manifestCompiler.CurrentGraphRevision(session), StringComparison.Ordinal))
        {
            return Reject(StaleGraphRevisionCode, "Planner tool manifest references stale graph state.");
        }

        return PlannerPrimitiveValidation.Accepted();
    }

    private PlannerPrimitiveValidation ValidatePrimitive(
        TripSession session,
        PlannerToolManifest manifest,
        PlannerPrimitiveInstance? primitive)
    {
        if (primitive is null
            || string.IsNullOrWhiteSpace(primitive.InstanceId)
            || string.IsNullOrWhiteSpace(primitive.PrimitiveId)
            || string.IsNullOrWhiteSpace(primitive.RendererKey)
            || primitive.AnswerSchema is null
            || primitive.Answers is null
            || primitive.EvidenceReferences is null
            || primitive.DependencyReferences is null
            || primitive.Options is null
            || primitive.Defaults is null
            || primitive.TaskReferences is null
            || primitive.TaskDecomposition is null
            || primitive.ToolContextReferences is null
            || primitive.ValidationRules is null
            || primitive.RendererHints is null
            || primitive.Options.Any(option => option is null)
            || primitive.Defaults.Any(item => item is null)
            || primitive.TaskReferences.Any(item => item is null)
            || primitive.TaskDecomposition.Any(item => item is null)
            || primitive.ValidationRules.Any(item => item is null)
            || primitive.RendererHints.Any(item => item is null))
        {
            return Reject(InvalidManifestCode, "Planner primitive failed validation.");
        }

        var definition = manifest.AllowedPrimitives.FirstOrDefault(item => string.Equals(item.PrimitiveId, primitive.PrimitiveId, StringComparison.Ordinal));
        if (definition is null)
        {
            return Reject(PlannerPrimitiveIds.Known.Contains(primitive.PrimitiveId) ? PrimitiveNotAllowedForStageCode : PrimitiveNotSupportedCode, "Planner primitive is not allowed.");
        }

        if (!definition.StageEligibility.Contains(manifest.Stage, StringComparer.Ordinal))
        {
            return Reject(PrimitiveNotAllowedForStageCode, "Planner primitive is not allowed for the current stage.");
        }

        if (primitive.SchemaVersion != definition.SchemaVersion
            || !CanonicalRendererMatches(definition, primitive))
        {
            return Reject(PrimitiveRendererMismatchCode, "Planner primitive renderer does not match the primitive kind.");
        }

        if (primitive.Label?.Length > manifest.MaxTextLength
            || primitive.Prompt?.Length > manifest.MaxTextLength
            || primitive.HelpText?.Length > manifest.MaxTextLength
            || primitive.Options.Any(option => option.Label?.Length > manifest.MaxTextLength || option.Summary?.Length > manifest.MaxTextLength)
            || primitive.Defaults.Any(item => item.Value?.Length > manifest.MaxTextLength)
            || primitive.TaskReferences.Any(item => item.Label?.Length > manifest.MaxTextLength)
            || primitive.TaskDecomposition.Any(item => item.Title?.Length > manifest.MaxTextLength)
            || primitive.ToolContextReferences.Count > MaxRefs
            || primitive.ValidationRules.Count > MaxRefs
            || primitive.RendererHints.Count > MaxRefs)
        {
            return Reject(InvalidManifestCode, "Planner primitive failed validation.");
        }

        if (Unsafe(primitive.InstanceId)
            || Unsafe(primitive.PrimitiveId)
            || Unsafe(primitive.RendererKey)
            || Unsafe(primitive.Label)
            || Unsafe(primitive.Prompt)
            || Unsafe(primitive.HelpText)
            || Unsafe(primitive.MoodToken)
            || Unsafe(primitive.MediaToken)
            || LooksLikeRawMediaOrCss(primitive.MediaToken)
            || primitive.Answers.Any(pair => Unsafe(pair.Key) || Unsafe(pair.Value))
            || HasUnsafeDynamicContent(primitive))
        {
            return Reject(PrimitiveTextRedactedCode, "Planner primitive text was unsafe.");
        }

        if (primitive.PrimitiveId is PlannerPrimitiveIds.ToolSearchRequest or PlannerPrimitiveIds.ToolGapRequest or PlannerPrimitiveIds.ToolContextReference
            && !manifest.AllowedToolIds.Contains(primitive.PrimitiveId, StringComparer.Ordinal))
        {
            return Reject(ToolNotAllowedCode, "Planner primitive tool request is not allowed.");
        }

        if (primitive.Options.Count > manifest.MaxOptionCount)
        {
            return Reject(PrimitiveOptionLimitExceededCode, "Planner primitive option count exceeded the allowed maximum.");
        }

        if (RequiresOptions(primitive.PrimitiveId) && primitive.Options.Count == 0)
        {
            return Reject(PrimitiveOptionsMissingCode, "Planner primitive requires explicit options.");
        }

        var dynamicOptionValidation = ValidateDynamicOptions(manifest, definition, primitive);
        if (!dynamicOptionValidation.IsAccepted)
        {
            return dynamicOptionValidation;
        }

        var dynamicReferenceValidation = ValidateDynamicReferences(manifest, primitive);
        if (!dynamicReferenceValidation.IsAccepted)
        {
            return dynamicReferenceValidation;
        }

        var taskDecompositionValidation = ValidateTaskDecomposition(primitive);
        if (!taskDecompositionValidation.IsAccepted)
        {
            return taskDecompositionValidation;
        }

        if (!string.IsNullOrWhiteSpace(primitive.FieldPath)
            && (!definition.AllowedFieldPaths.Contains(primitive.FieldPath, StringComparer.Ordinal)
                || !manifest.AllowedFieldPaths.Contains(primitive.FieldPath, StringComparer.Ordinal)))
        {
            return Reject(FieldPathNotAllowedCode, "Planner primitive field path is not allowed.");
        }

        if (!RendererAllowedForFieldPath(primitive))
        {
            return Reject(PrimitiveRendererMismatchCode, "Planner primitive renderer does not match the field path.");
        }

        if (!string.IsNullOrWhiteSpace(primitive.SlotId)
            && !manifest.AllowedSlotIds.Contains(primitive.SlotId, StringComparer.Ordinal))
        {
            return Reject(OwnershipInvalidCode, "Planner primitive references an unknown slot.");
        }

        if (!string.IsNullOrWhiteSpace(primitive.CandidateId)
            && !manifest.AllowedCandidateIds.Contains(primitive.CandidateId, StringComparer.Ordinal))
        {
            return Reject(OwnershipInvalidCode, "Planner primitive references an unknown candidate.");
        }

        if (!string.IsNullOrWhiteSpace(primitive.SlotId)
            && !string.IsNullOrWhiteSpace(primitive.CandidateId)
            && !session.HasItineraryCandidateForSlot(primitive.SlotId, primitive.CandidateId))
        {
            return Reject(OwnershipInvalidCode, "Planner primitive candidate is not owned by the slot.");
        }

        if (!string.IsNullOrWhiteSpace(primitive.TaskId)
            && !manifest.AllowedTaskIds.Contains(primitive.TaskId, StringComparer.Ordinal))
        {
            return Reject(OwnershipInvalidCode, "Planner primitive references an unknown task.");
        }

        if (!string.IsNullOrWhiteSpace(primitive.MoodToken)
            && (!definition.SupportsMood || !manifest.AllowedMoodTokens.Contains(primitive.MoodToken, StringComparer.Ordinal)))
        {
            return Reject(PrimitiveMetadataRedactedCode, "Planner primitive metadata was unsafe.");
        }

        if (!string.IsNullOrWhiteSpace(primitive.MediaToken)
            && (!definition.SupportsMedia || !manifest.AllowedMediaTokens.Contains(primitive.MediaToken, StringComparer.Ordinal)))
        {
            return Reject(PrimitiveMetadataRedactedCode, "Planner primitive metadata was unsafe.");
        }

        if (primitive.PrimitiveId == PlannerPrimitiveIds.ApprovalGate && (!manifest.AllowsApproval || !manifest.AllowsSpend))
        {
            return Reject(ApprovalRequiredCode, "Planner primitive requires approval before spend-adjacent rendering.");
        }

        if (!AnswerSchemaMatches(definition.AnswerSchema, primitive.AnswerSchema)
            || !AnswersMatch(primitive.AnswerSchema, primitive.Answers))
        {
            return Reject(PrimitiveAnswerSchemaInvalidCode, "Planner primitive answer schema failed validation.");
        }

        if (!DefaultsMatchAnswerSchema(primitive))
        {
            return Reject(PrimitiveAnswerSchemaInvalidCode, "Planner primitive answer schema failed validation.");
        }

        if (!AnswerValuesAllowed(primitive))
        {
            return Reject(AnswerValueNotAllowedCode, "Planner primitive answer value is not allowed.");
        }

        var cleanOptions = CleanOptions(primitive.Options, manifest).ToArray();
        var cleanDefaults = CleanDefaults(primitive.Defaults, manifest).ToArray();
        var cleanTasks = CleanTasks(primitive.TaskReferences, manifest).ToArray();
        var cleanToolRefs = CleanRefs(primitive.ToolContextReferences).ToArray();
        var cleanRules = CleanRules(primitive.ValidationRules, manifest).ToArray();
        var cleanHints = CleanHints(primitive.RendererHints, manifest).ToArray();

        return new(
            true,
            AcceptedCode,
            "Planner primitive accepted.",
            new ValidatedPrimitiveView(
                SafeId(primitive.InstanceId),
                definition.PrimitiveId,
                definition.RendererKey,
                SafeNullableText(primitive.Label, manifest.MaxTextLength),
                SafeNullableText(primitive.Prompt, manifest.MaxTextLength),
                SafeNullableText(primitive.FieldPath, manifest.MaxTextLength),
                SafeNullableText(primitive.SlotId, manifest.MaxTextLength),
                SafeNullableText(primitive.CandidateId, manifest.MaxTextLength),
                SafeNullableText(primitive.TaskId, manifest.MaxTextLength),
                SafeNullableText(primitive.MoodToken, manifest.MaxTextLength),
                SafeNullableText(primitive.MediaToken, manifest.MaxTextLength),
                primitive.AnswerSchema with
                {
                    Options = primitive.AnswerSchema.Options.Select(SafeId).Distinct(StringComparer.Ordinal).Take(MaxRefs).ToArray()
                },
                primitive.Answers.ToDictionary(pair => SafeId(pair.Key), pair => SafeNullableText(pair.Value, manifest.MaxTextLength), StringComparer.Ordinal),
                CleanRefs(primitive.EvidenceReferences))
            {
                HelpText = SafeNullableText(primitive.HelpText, manifest.MaxTextLength),
                Options = cleanOptions,
                Defaults = cleanDefaults,
                TaskReferences = cleanTasks,
                TaskDecomposition = CleanTaskDecomposition(primitive.TaskDecomposition, manifest).ToArray(),
                ToolContextReferences = cleanToolRefs,
                ValidationRules = cleanRules,
                RendererHints = cleanHints
            });
    }

    private static bool CanonicalRendererMatches(PlannerPrimitiveDefinition definition, PlannerPrimitiveInstance primitive)
    {
        return string.Equals(primitive.RendererKey, definition.RendererKey, StringComparison.Ordinal);
    }

    private static bool RequiresOptions(string primitiveId)
    {
        return primitiveId is PlannerPrimitiveIds.RadioGroup
            or PlannerPrimitiveIds.Select
            or PlannerPrimitiveIds.MultiSelect
            or PlannerPrimitiveIds.ChoiceCard
            or PlannerPrimitiveIds.CandidateDeck;
    }

    private static bool RendererAllowedForFieldPath(PlannerPrimitiveInstance primitive)
    {
        if (string.IsNullOrWhiteSpace(primitive.FieldPath))
        {
            return true;
        }

        if (primitive.FieldPath is "/mission/start_date" or "/mission/end_date"
            && primitive.PrimitiveId is not (PlannerPrimitiveIds.Date or PlannerPrimitiveIds.DateRange))
        {
            return false;
        }

        if (primitive.FieldPath is "/mission/destination_country" or "/constraints/pace" or "/constraints/budget" or "/mission/travel_style"
            && primitive.PrimitiveId is PlannerPrimitiveIds.TextInput or PlannerPrimitiveIds.Textarea)
        {
            return false;
        }

        return true;
    }

    private static PlannerPrimitiveValidation ValidateTaskDecomposition(PlannerPrimitiveInstance primitive)
    {
        if (primitive.PrimitiveId != PlannerPrimitiveIds.TaskDecomposition)
        {
            return primitive.TaskDecomposition.Count == 0
                ? PlannerPrimitiveValidation.Accepted()
                : Reject(TaskDecompositionInvalidCode, "Planner task decomposition failed validation.");
        }

        if (primitive.TaskDecomposition.Count == 0)
        {
            return Reject(TaskDecompositionMissingCode, "Planner task decomposition is missing.");
        }

        var taskIds = new HashSet<string>(StringComparer.Ordinal);
        var orders = new HashSet<int>();
        foreach (var item in primitive.TaskDecomposition)
        {
            if (item is null
                || string.IsNullOrWhiteSpace(item.TaskId)
                || string.IsNullOrWhiteSpace(item.Title)
                || string.IsNullOrWhiteSpace(item.State)
                || item.Order < 0
                || item.DependencyTaskIds is null
                || item.EvidenceReferences is null
                || Unsafe(item.TaskId)
                || Unsafe(item.Title)
                || Unsafe(item.State)
                || item.DependencyTaskIds.Any(Unsafe)
                || item.EvidenceReferences.Any(Unsafe)
                || !PlannerTaskStates.Known.Contains(item.State)
                || !taskIds.Add(item.TaskId)
                || !orders.Add(item.Order))
            {
                return Reject(TaskDecompositionInvalidCode, "Planner task decomposition failed validation.");
            }
        }

        foreach (var item in primitive.TaskDecomposition)
        {
            if (item.DependencyTaskIds.Any(dependencyId => !taskIds.Contains(dependencyId)))
            {
                return Reject(TaskDecompositionInvalidCode, "Planner task decomposition failed validation.");
            }
        }

        return PlannerPrimitiveValidation.Accepted();
    }

    private static bool DefaultsMatchAnswerSchema(PlannerPrimitiveInstance primitive)
    {
        foreach (var item in primitive.Defaults)
        {
            if (!DefaultMatches(primitive.AnswerSchema.Kind, item))
            {
                return false;
            }
        }

        return true;
    }

    private static bool DefaultMatches(string answerKind, PlannerPrimitiveDefault item)
    {
        if (string.IsNullOrWhiteSpace(item.Value))
        {
            return true;
        }

        return answerKind switch
        {
            PlannerAnswerSchemaKinds.Date => DateOnly.TryParseExact(item.Value, "yyyy-MM-dd", out _),
            PlannerAnswerSchemaKinds.DateRange => item.FieldKey is "start" or "end"
                && DateOnly.TryParseExact(item.Value, "yyyy-MM-dd", out _),
            PlannerAnswerSchemaKinds.Number or PlannerAnswerSchemaKinds.NumberRange => decimal.TryParse(item.Value, out _),
            PlannerAnswerSchemaKinds.Boolean => item.Value is "true" or "false",
            PlannerAnswerSchemaKinds.SingleSelect or PlannerAnswerSchemaKinds.MultiSelect or PlannerAnswerSchemaKinds.RankedChoice or PlannerAnswerSchemaKinds.Confirmation => true,
            PlannerAnswerSchemaKinds.Text => true,
            PlannerAnswerSchemaKinds.None => false,
            PlannerAnswerSchemaKinds.TaskDecomposition => false,
            _ => false
        };
    }

    private static PlannerPrimitiveValidation ValidateDynamicOptions(
        PlannerToolManifest manifest,
        PlannerPrimitiveDefinition definition,
        PlannerPrimitiveInstance primitive)
    {
        var optionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var option in primitive.Options)
        {
            if (option is null
                || string.IsNullOrWhiteSpace(option.OptionId)
                || string.IsNullOrWhiteSpace(option.Label)
                || Unsafe(option.OptionId)
                || Unsafe(option.Label)
                || Unsafe(option.Summary)
                || Unsafe(option.MoodToken)
                || Unsafe(option.MediaToken)
                || LooksLikeRawMediaOrCss(option.MediaToken)
                || option.EvidenceReferences is null
                || option.ToolContextReferences is null
                || !optionIds.Add(option.OptionId))
            {
                return Reject(PrimitiveTextRedactedCode, "Planner primitive text was unsafe.");
            }

            if (!string.IsNullOrWhiteSpace(option.MoodToken)
                && (!definition.SupportsMood || !manifest.AllowedMoodTokens.Contains(option.MoodToken, StringComparer.Ordinal)))
            {
                return Reject(PrimitiveTextRedactedCode, "Planner primitive text was unsafe.");
            }

            if (!string.IsNullOrWhiteSpace(option.MediaToken)
                && (!definition.SupportsMedia || !manifest.AllowedMediaTokens.Contains(option.MediaToken, StringComparer.Ordinal)))
            {
                return Reject(PrimitiveTextRedactedCode, "Planner primitive text was unsafe.");
            }

            if (option.ToolContextReferences.Any(reference => !manifest.AllowedToolContextRefs.Contains(reference, StringComparer.Ordinal)))
            {
                return Reject(ToolContextRefInvalidCode, "Planner primitive references unknown tool context.");
            }
        }

        return PlannerPrimitiveValidation.Accepted();
    }

    private static PlannerPrimitiveValidation ValidateDynamicReferences(PlannerToolManifest manifest, PlannerPrimitiveInstance primitive)
    {
        foreach (var item in primitive.Defaults)
        {
            if (item is null
                || string.IsNullOrWhiteSpace(item.FieldKey)
                || Unsafe(item.FieldKey)
                || Unsafe(item.Value))
            {
                return Reject(PrimitiveTextRedactedCode, "Planner primitive text was unsafe.");
            }
        }

        foreach (var task in primitive.TaskReferences)
        {
            if (task is null
                || string.IsNullOrWhiteSpace(task.TaskId)
                || string.IsNullOrWhiteSpace(task.Label)
                || Unsafe(task.TaskId)
                || Unsafe(task.Label)
                || Unsafe(task.GroupId)
                || task.EvidenceReferences is null
                || task.ToolContextReferences is null)
            {
                return Reject(PrimitiveTextRedactedCode, "Planner primitive text was unsafe.");
            }

            if (!manifest.AllowedTaskIds.Contains(task.TaskId, StringComparer.Ordinal))
            {
                return Reject(OwnershipInvalidCode, "Planner primitive references an unknown task.");
            }

            if (task.ToolContextReferences.Any(reference => !manifest.AllowedToolContextRefs.Contains(reference, StringComparer.Ordinal)))
            {
                return Reject(ToolContextRefInvalidCode, "Planner primitive references unknown tool context.");
            }
        }

        if (primitive.ToolContextReferences.Any(reference => Unsafe(reference) || !manifest.AllowedToolContextRefs.Contains(reference, StringComparer.Ordinal)))
        {
            return Reject(ToolContextRefInvalidCode, "Planner primitive references unknown tool context.");
        }

        if (primitive.ValidationRules.Any(rule => rule is null
                || string.IsNullOrWhiteSpace(rule.RuleId)
                || string.IsNullOrWhiteSpace(rule.Kind)
                || string.IsNullOrWhiteSpace(rule.Code)
                || Unsafe(rule.RuleId)
                || Unsafe(rule.Kind)
                || Unsafe(rule.Value)
                || Unsafe(rule.Code)))
        {
            return Reject(PrimitiveTextRedactedCode, "Planner primitive text was unsafe.");
        }

        if (primitive.RendererHints.Any(hint => hint is null
                || string.IsNullOrWhiteSpace(hint.Key)
                || Unsafe(hint.Key)
                || Unsafe(hint.Value)
                || LooksLikeRawMediaOrCss(hint.Value)))
        {
            return Reject(PrimitiveTextRedactedCode, "Planner primitive text was unsafe.");
        }

        return PlannerPrimitiveValidation.Accepted();
    }

    private static bool AnswerSchemaMatches(PlannerAnswerSchema expected, PlannerAnswerSchema actual)
    {
        return string.Equals(expected.Kind, actual.Kind, StringComparison.Ordinal)
            && expected.Required == actual.Required;
    }

    private static bool AnswersMatch(PlannerAnswerSchema schema, IReadOnlyDictionary<string, string?> answers)
    {
        if (!schema.Required && answers.Count == 0)
        {
            return true;
        }

        return schema.Kind switch
        {
            PlannerAnswerSchemaKinds.None => answers.Count == 0,
            PlannerAnswerSchemaKinds.Text => !schema.Required || HasValue(answers, "value"),
            PlannerAnswerSchemaKinds.Date => HasDate(answers, "value"),
            PlannerAnswerSchemaKinds.Boolean => HasAllowedValue(answers, "checked", ["true", "false"]),
            PlannerAnswerSchemaKinds.Number => HasNumber(answers, "value"),
            PlannerAnswerSchemaKinds.Confirmation => HasAllowedValue(answers, "value", schema.Options.Count > 0 ? schema.Options : ["confirm", "correct", "defer"]),
            PlannerAnswerSchemaKinds.SingleSelect => HasValue(answers, "selected"),
            PlannerAnswerSchemaKinds.MultiSelect => !schema.Required || HasValue(answers, "selected"),
            PlannerAnswerSchemaKinds.RankedChoice => !schema.Required || HasValue(answers, "ranked"),
            PlannerAnswerSchemaKinds.DateRange => HasValue(answers, "start") && HasValue(answers, "end"),
            PlannerAnswerSchemaKinds.NumberRange => HasValue(answers, "min") && HasValue(answers, "max"),
            PlannerAnswerSchemaKinds.TaskDecomposition => answers.Count == 0,
            _ => false
        };
    }

    private static bool AnswerValuesAllowed(PlannerPrimitiveInstance primitive)
    {
        var optionIds = primitive.Options.Select(option => option.OptionId).ToHashSet(StringComparer.Ordinal);
        if (optionIds.Count == 0)
        {
            return true;
        }

        var selectedValues = primitive.AnswerSchema.Kind switch
        {
            PlannerAnswerSchemaKinds.SingleSelect => Values(primitive.Answers, "selected"),
            PlannerAnswerSchemaKinds.MultiSelect => Values(primitive.Answers, "selected"),
            PlannerAnswerSchemaKinds.RankedChoice => Values(primitive.Answers, "ranked"),
            PlannerAnswerSchemaKinds.Confirmation => Values(primitive.Answers, "value"),
            _ => []
        };

        return selectedValues.Count == 0 || selectedValues.All(value => optionIds.Contains(value));
    }

    private static IReadOnlyList<string> Values(IReadOnlyDictionary<string, string?> answers, string key)
    {
        if (!answers.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool RequiresAnswer(PlannerAnswerSchema schema)
    {
        return schema.Required && schema.Kind is not PlannerAnswerSchemaKinds.None;
    }

    private static bool HasValue(IReadOnlyDictionary<string, string?> answers, string key)
    {
        return answers.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value);
    }

    private static bool HasDate(IReadOnlyDictionary<string, string?> answers, string key)
    {
        return answers.TryGetValue(key, out var value)
            && !string.IsNullOrWhiteSpace(value)
            && DateOnly.TryParseExact(value, "yyyy-MM-dd", out _);
    }

    private static bool HasNumber(IReadOnlyDictionary<string, string?> answers, string key)
    {
        return answers.TryGetValue(key, out var value)
            && !string.IsNullOrWhiteSpace(value)
            && decimal.TryParse(value, out _);
    }

    private static bool HasAllowedValue(IReadOnlyDictionary<string, string?> answers, string key, IReadOnlyList<string> allowed)
    {
        return answers.TryGetValue(key, out var value)
            && !string.IsNullOrWhiteSpace(value)
            && allowed.Contains(value, StringComparer.Ordinal);
    }

    private static PlannerPrimitiveValidation Reject(string code, string summary)
    {
        return new(false, code, summary, null);
    }

    private static PlannerPrimitiveValidationResult Blocked(string code, string summary, PlannerToolManifest? manifest)
    {
        return new(
            IsAccepted: false,
            IsBlocked: true,
            Code: code,
            Summary: summary,
            View: new ValidatedTurnView(
                TurnId: "validated-turn-blocked",
                SessionId: SafeId(manifest?.SessionId),
                GraphRevision: SafeId(manifest?.GraphRevision),
                Source: "harness_blocked",
                Code: code,
                Primitives: [],
                TaskRailItemRefs: [],
                TimelineAnchorRefs: [],
                EvidenceReferences: [],
                SanitizationStatus: "sanitized"));
    }

    private static string SummaryFor(string code)
    {
        return code switch
        {
            AwaitingUserInputCode => "Planner primitive turn is awaiting user input.",
            ToolSearchRequestedCode => "Planner requested tool search without mutation.",
            ToolGapReviewRequiredCode => "Planner reported a tool gap for review without mutation.",
            _ => "Planner primitive turn accepted."
        };
    }

    private static IEnumerable<PlannerPrimitiveOption> CleanOptions(IEnumerable<PlannerPrimitiveOption> options, PlannerToolManifest manifest)
    {
        return options
            .Take(manifest.MaxOptionCount)
            .Select(option => new PlannerPrimitiveOption(
                SafeId(option.OptionId),
                SafeText(option.Label, manifest.MaxTextLength),
                SafeNullableText(option.Summary, manifest.MaxTextLength),
                SafeNullableText(option.MoodToken, manifest.MaxTextLength),
                SafeNullableText(option.MediaToken, manifest.MaxTextLength),
                CleanRefs(option.EvidenceReferences),
                CleanRefs(option.ToolContextReferences)));
    }

    private static IEnumerable<PlannerPrimitiveDefault> CleanDefaults(IEnumerable<PlannerPrimitiveDefault> defaults, PlannerToolManifest manifest)
    {
        return defaults
            .Take(MaxRefs)
            .Select(item => new PlannerPrimitiveDefault(SafeId(item.FieldKey), SafeNullableText(item.Value, manifest.MaxTextLength)));
    }

    private static IEnumerable<PlannerTaskReference> CleanTasks(IEnumerable<PlannerTaskReference> tasks, PlannerToolManifest manifest)
    {
        return tasks
            .Take(MaxRefs)
            .Select(task => new PlannerTaskReference(
                SafeId(task.TaskId),
                SafeText(task.Label, manifest.MaxTextLength),
                SafeNullableText(task.GroupId, manifest.MaxTextLength),
                CleanRefs(task.EvidenceReferences),
                CleanRefs(task.ToolContextReferences)));
    }

    private static IEnumerable<PlannerTaskDecompositionItem> CleanTaskDecomposition(IEnumerable<PlannerTaskDecompositionItem> tasks, PlannerToolManifest manifest)
    {
        return tasks
            .OrderBy(task => task.Order)
            .Take(MaxRefs)
            .Select(task => new PlannerTaskDecompositionItem(
                SafeId(task.TaskId),
                SafeText(task.Title, manifest.MaxTextLength),
                SafeId(task.State),
                task.Order,
                CleanRefs(task.DependencyTaskIds),
                CleanRefs(task.EvidenceReferences)));
    }

    private static IEnumerable<PlannerPrimitiveValidationRule> CleanRules(IEnumerable<PlannerPrimitiveValidationRule> rules, PlannerToolManifest manifest)
    {
        return rules
            .Take(MaxRefs)
            .Select(rule => new PlannerPrimitiveValidationRule(
                SafeId(rule.RuleId),
                SafeId(rule.Kind),
                SafeNullableText(rule.Value, manifest.MaxTextLength),
                SafeId(rule.Code)));
    }

    private static IEnumerable<PlannerRendererHint> CleanHints(IEnumerable<PlannerRendererHint> hints, PlannerToolManifest manifest)
    {
        return hints
            .Take(MaxRefs)
            .Select(hint => new PlannerRendererHint(SafeId(hint.Key), SafeNullableText(hint.Value, manifest.MaxTextLength)));
    }

    private static IReadOnlyList<string> CleanRefs(IEnumerable<string>? values)
    {
        return values?
            .Where(value => !string.IsNullOrWhiteSpace(value) && !Unsafe(value))
            .Select(SafeId)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxRefs)
            .ToArray()
            ?? [];
    }

    private static string SafeId(string? value)
    {
        var text = SafeText(value, MaxTextLength);
        if (text == "redacted")
        {
            return text;
        }

        var normalized = new string(text
            .Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or ':' or '/' ? character : '-')
            .ToArray());
        return string.IsNullOrWhiteSpace(normalized) ? "redacted" : normalized;
    }

    private static string? SafeNullableText(string? value, int maxLength)
    {
        return string.IsNullOrWhiteSpace(value) ? null : SafeText(value, maxLength);
    }

    private static string SafeText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "redacted";
        }

        if (Unsafe(value))
        {
            return "redacted";
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static bool Unsafe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("RAW_", StringComparison.OrdinalIgnoreCase)
            || value.Contains("PROVIDER_PAYLOAD", StringComparison.OrdinalIgnoreCase)
            || value.Contains("RAW_PROMPT", StringComparison.OrdinalIgnoreCase)
            || value.Contains("APPROVAL_TOKEN", StringComparison.OrdinalIgnoreCase)
            || value.Contains("HOLD_REFERENCE", StringComparison.OrdinalIgnoreCase)
            || value.Contains("PAYMENT", StringComparison.OrdinalIgnoreCase)
            || value.Contains("BOOKING_REF", StringComparison.OrdinalIgnoreCase)
            || value.Contains("CANDIDATE_DISPLAY", StringComparison.OrdinalIgnoreCase)
            || value.Contains("SECRET", StringComparison.OrdinalIgnoreCase)
            || value.Contains("CREDENTIAL", StringComparison.OrdinalIgnoreCase)
            || value.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase)
            || value.Contains("API_KEY", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeRawMediaOrCss(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("http://", StringComparison.OrdinalIgnoreCase)
            || value.Contains("https://", StringComparison.OrdinalIgnoreCase)
            || value.Contains("style=", StringComparison.OrdinalIgnoreCase)
            || value.Contains("{", StringComparison.Ordinal)
            || value.Contains("}", StringComparison.Ordinal)
            || value.Contains("javascript:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasUnsafeDynamicContent(PlannerPrimitiveInstance primitive)
    {
        return primitive.Options.Any(option => option is null
                || Unsafe(option.OptionId)
                || Unsafe(option.Label)
                || Unsafe(option.Summary)
                || Unsafe(option.MoodToken)
                || Unsafe(option.MediaToken)
                || LooksLikeRawMediaOrCss(option.MediaToken)
                || option.EvidenceReferences is null
                || option.EvidenceReferences.Any(Unsafe)
                || option.ToolContextReferences is null
                || option.ToolContextReferences.Any(Unsafe))
            || primitive.Defaults.Any(item => item is null || Unsafe(item.FieldKey) || Unsafe(item.Value))
            || primitive.TaskReferences.Any(task => task is null
                || Unsafe(task.TaskId)
                || Unsafe(task.Label)
                || Unsafe(task.GroupId)
                || task.EvidenceReferences is null
                || task.EvidenceReferences.Any(Unsafe)
                || task.ToolContextReferences is null
                || task.ToolContextReferences.Any(Unsafe))
            || primitive.TaskDecomposition.Any(task => task is null
                || Unsafe(task.TaskId)
                || Unsafe(task.Title)
                || Unsafe(task.State)
                || task.DependencyTaskIds is null
                || task.DependencyTaskIds.Any(Unsafe)
                || task.EvidenceReferences is null
                || task.EvidenceReferences.Any(Unsafe))
            || primitive.ToolContextReferences.Any(Unsafe)
            || primitive.ValidationRules.Any(rule => rule is null || Unsafe(rule.RuleId) || Unsafe(rule.Kind) || Unsafe(rule.Value) || Unsafe(rule.Code))
            || primitive.RendererHints.Any(hint => hint is null || Unsafe(hint.Key) || Unsafe(hint.Value) || LooksLikeRawMediaOrCss(hint.Value));
    }

    internal static string HashValidatedTurn(IReadOnlyList<ValidatedPrimitiveView> primitives)
    {
        var payload = JsonSerializer.Serialize(primitives.Select(primitive => new
        {
            primitive.InstanceId,
            primitive.PrimitiveId,
            primitive.FieldPath,
            primitive.MoodToken,
            primitive.MediaToken,
            Options = primitive.Options.Select(option => option.OptionId).OrderBy(id => id, StringComparer.Ordinal),
            Tasks = primitive.TaskReferences.Select(task => task.TaskId).OrderBy(id => id, StringComparer.Ordinal),
            TaskDecomposition = primitive.TaskDecomposition.Select(task => new { task.TaskId, task.State, task.Order }).OrderBy(task => task.Order),
            ToolContextReferences = primitive.ToolContextReferences.OrderBy(id => id, StringComparer.Ordinal),
            Answers = primitive.Answers.OrderBy(pair => pair.Key, StringComparer.Ordinal)
        }));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}

public sealed record PlannerPrimitiveValidationResult(
    bool IsAccepted,
    bool IsBlocked,
    string Code,
    string Summary,
    ValidatedTurnView View);

public sealed record PlannerPrimitiveValidation(
    bool IsAccepted,
    string Code,
    string Summary,
    ValidatedPrimitiveView? Primitive)
{
    public static PlannerPrimitiveValidation Accepted() => new(true, PlannerPrimitiveValidator.AcceptedCode, "Accepted.", null);
}
