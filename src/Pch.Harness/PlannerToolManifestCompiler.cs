using Pch.Core;

namespace Pch.Harness;

public sealed class PlannerToolManifestCompiler
{
    public const int ManifestSchemaVersion = 1;

    private const int MaxPrimitiveCount = 12;
    private const int MaxTextLength = 160;

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
            AllowsSpend: false);
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
            Definition(PlannerPrimitiveIds.AssistantMessage, "assistant-message", stages, PlannerAnswerSchemaKinds.None, [], supportsMood: true, supportsMedia: false),
            Definition(PlannerPrimitiveIds.EvidenceStrip, "evidence-strip", stages, PlannerAnswerSchemaKinds.None, [], supportsMood: false, supportsMedia: false),
            Definition(PlannerPrimitiveIds.TimelineAnchor, "timeline-anchor", stages, PlannerAnswerSchemaKinds.None, [], supportsMood: false, supportsMedia: false),
            Definition(PlannerPrimitiveIds.ToolSearchRequest, "tool-search-request", stages, PlannerAnswerSchemaKinds.Text, [], supportsMood: false, supportsMedia: false),
            Definition(PlannerPrimitiveIds.ToolGapRequest, "tool-gap-request", stages, PlannerAnswerSchemaKinds.Text, [], supportsMood: false, supportsMedia: false)
        };

        shared.AddRange(stage switch
        {
            HarnessStage.Intake or HarnessStage.SlotCollection =>
            [
                Definition(PlannerPrimitiveIds.TextInput, "text-input", stages, PlannerAnswerSchemaKinds.Text, AllowedFieldPaths(), supportsMood: false, supportsMedia: false),
                Definition(PlannerPrimitiveIds.Textarea, "textarea", stages, PlannerAnswerSchemaKinds.Text, AllowedFieldPaths(), supportsMood: false, supportsMedia: false),
                Definition(PlannerPrimitiveIds.DateRange, "date-range", stages, PlannerAnswerSchemaKinds.DateRange, ["/mission/start_date", "/mission/end_date"], supportsMood: false, supportsMedia: false),
                Definition(PlannerPrimitiveIds.ConfirmationQuestion, "confirmation-question", stages, PlannerAnswerSchemaKinds.Confirmation, AllowedFieldPaths(), supportsMood: true, supportsMedia: false)
            ],
            HarnessStage.DaySkeletonGeneration or HarnessStage.Logistics or HarnessStage.Meals or HarnessStage.ActivitiesDowntime =>
            [
                Definition(PlannerPrimitiveIds.CandidateDeck, "candidate-deck", stages, PlannerAnswerSchemaKinds.SingleSelect, [], supportsMood: true, supportsMedia: true),
                Definition(PlannerPrimitiveIds.SingleSelect, "single-select", stages, PlannerAnswerSchemaKinds.SingleSelect, [], supportsMood: true, supportsMedia: false),
                Definition(PlannerPrimitiveIds.MultiSelect, "multi-select", stages, PlannerAnswerSchemaKinds.MultiSelect, [], supportsMood: true, supportsMedia: false),
                Definition(PlannerPrimitiveIds.RankedChoice, "ranked-choice", stages, PlannerAnswerSchemaKinds.RankedChoice, [], supportsMood: true, supportsMedia: true),
                Definition(PlannerPrimitiveIds.AvailabilityPreview, "availability-preview", stages, PlannerAnswerSchemaKinds.None, [], supportsMood: false, supportsMedia: false)
            ],
            HarnessStage.ApprovalQueue =>
            [
                Definition(PlannerPrimitiveIds.ApprovalGate, "approval-gate", stages, PlannerAnswerSchemaKinds.Confirmation, [], supportsMood: false, supportsMedia: false)
            ],
            _ =>
            [
                Definition(PlannerPrimitiveIds.TextInput, "text-input", stages, PlannerAnswerSchemaKinds.Text, AllowedFieldPaths(), supportsMood: false, supportsMedia: false),
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
            PlannerAnswerSchemaKinds.SingleSelect => ["selected"],
            PlannerAnswerSchemaKinds.MultiSelect => ["selected"],
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
            "/constraints/budget"
        ];
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
    public const string AnswerSchemaInvalidCode = "answer_schema_invalid";
    public const string StaleGraphRevisionCode = "stale_graph_revision";
    public const string OwnershipInvalidCode = "ownership_invalid";
    public const string PrimitiveMetadataRedactedCode = "primitive_metadata_redacted";
    public const string ApprovalRequiredCode = "approval_required";

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
        var view = new ValidatedTurnView(
            TurnId: $"validated-turn-{SafeId(proposal.ProposalId)}",
            SessionId: SafeId(session.SessionId),
            GraphRevision: manifest.GraphRevision,
            Source: "live_provider_candidate",
            Code: code,
            Primitives: validated,
            TaskRailItemRefs: validated.SelectMany(primitive => new[] { primitive.TaskId }.Where(id => !string.IsNullOrWhiteSpace(id))).Select(SafeId).Distinct(StringComparer.Ordinal).Take(MaxRefs).ToArray(),
            TimelineAnchorRefs: validated.Where(primitive => primitive.PrimitiveId == PlannerPrimitiveIds.TimelineAnchor).Select(primitive => primitive.InstanceId).Take(MaxRefs).ToArray(),
            EvidenceReferences: validated.SelectMany(primitive => primitive.EvidenceReferences).Distinct(StringComparer.Ordinal).Take(MaxRefs).ToArray(),
            SanitizationStatus: "sanitized");

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
            || primitive.DependencyReferences is null)
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
            || !string.Equals(primitive.RendererKey, definition.RendererKey, StringComparison.Ordinal))
        {
            return Reject(InvalidManifestCode, "Planner primitive failed validation.");
        }

        if (primitive.Label?.Length > manifest.MaxTextLength
            || primitive.Prompt?.Length > manifest.MaxTextLength)
        {
            return Reject(InvalidManifestCode, "Planner primitive failed validation.");
        }

        if (Unsafe(primitive.InstanceId)
            || Unsafe(primitive.PrimitiveId)
            || Unsafe(primitive.RendererKey)
            || Unsafe(primitive.Label)
            || Unsafe(primitive.Prompt)
            || Unsafe(primitive.MoodToken)
            || Unsafe(primitive.MediaToken)
            || LooksLikeRawMediaOrCss(primitive.MediaToken)
            || primitive.Answers.Any(pair => Unsafe(pair.Key) || Unsafe(pair.Value)))
        {
            return Reject(PrimitiveMetadataRedactedCode, "Planner primitive metadata was unsafe.");
        }

        if (!string.IsNullOrWhiteSpace(primitive.FieldPath)
            && (!definition.AllowedFieldPaths.Contains(primitive.FieldPath, StringComparer.Ordinal)
                || !manifest.AllowedFieldPaths.Contains(primitive.FieldPath, StringComparer.Ordinal)))
        {
            return Reject(FieldPathNotAllowedCode, "Planner primitive field path is not allowed.");
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
            return Reject(AnswerSchemaInvalidCode, "Planner primitive answer schema failed validation.");
        }

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
                CleanRefs(primitive.EvidenceReferences)));
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
            PlannerAnswerSchemaKinds.Confirmation => HasAllowedValue(answers, "value", schema.Options.Count > 0 ? schema.Options : ["confirm", "correct", "defer"]),
            PlannerAnswerSchemaKinds.SingleSelect => HasValue(answers, "selected"),
            PlannerAnswerSchemaKinds.MultiSelect => !schema.Required || HasValue(answers, "selected"),
            PlannerAnswerSchemaKinds.RankedChoice => !schema.Required || HasValue(answers, "ranked"),
            PlannerAnswerSchemaKinds.DateRange => HasValue(answers, "start") && HasValue(answers, "end"),
            PlannerAnswerSchemaKinds.NumberRange => HasValue(answers, "min") && HasValue(answers, "max"),
            _ => false
        };
    }

    private static bool RequiresAnswer(PlannerAnswerSchema schema)
    {
        return schema.Required && schema.Kind is not PlannerAnswerSchemaKinds.None;
    }

    private static bool HasValue(IReadOnlyDictionary<string, string?> answers, string key)
    {
        return answers.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value);
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
