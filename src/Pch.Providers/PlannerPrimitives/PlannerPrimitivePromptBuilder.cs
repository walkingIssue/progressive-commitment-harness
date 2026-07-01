using System.Text.Json;

namespace Pch.Providers.PlannerPrimitives;

internal static class PlannerPrimitivePromptBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Build(PlannerModelRequest request, bool isRepair, string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(request);

        var manifest = request.Manifest;
        return JsonSerializer.Serialize(new
        {
            Instruction = "Build the next validated planner interaction by invoking explicit HTML/form primitive tools with data. Do not emit generic form-ish prose blobs. Make primitive ids, primitive kinds, field paths, labels, prompts, tasks, and options specific to runtimePrompt, submitted answers, and current context. Do not invent live search, booking, availability, hotel, price, or web facts without toolContextRefs.",
            RuntimePrompt = request.RuntimePrompt,
            request.RunId,
            request.TurnId,
            manifest.ManifestId,
            manifest.ManifestVersion,
            manifest.GraphRevision,
            manifest.SessionId,
            manifest.Stage,
            request.Locale,
            request.PromptDigest,
            request.SanitizedStateSummary,
            RepairAttempt = isRepair,
            SchemaFailure = failureReason,
            SubmittedAnswers = request.SubmittedAnswers.Select(answer => new
            {
                answer.AnswerId,
                answer.FieldPath,
                answer.Value,
                answer.SourcePrimitiveInstanceId
            }).ToArray(),
            ContextToolResults = request.ContextToolResults.Select(result => new
            {
                result.ToolId,
                result.ResultId,
                result.Category,
                result.SourceClass,
                Title = SafeContextText(result.Title),
                Summary = SafeContextText(result.Summary),
                result.Freshness,
                result.EvidenceRefs
            }).ToArray(),
            AllowedPrimitives = manifest.AllowedPrimitives.Select(primitive => new
            {
                primitive.PrimitiveId,
                primitive.PrimitiveKind,
                primitive.RendererKey
            }).ToArray(),
            PrimitiveToolMenu = PlannerPrimitiveToolCatalog.RequiredPrimitiveIds.Select(id => new
            {
                PrimitiveId = id,
                PrimitiveKind = id,
                RendererKey = id
            }).ToArray(),
            PrimitiveSelectionRules = new[]
            {
                "destination confirmation must use radio_group or select, never text_input",
                "exact dates must use date or date_range, never text_input",
                "pace must use select, radio_group, or slider when options are available",
                "multiple preferences must use multi_select, choice_card, or candidate_deck",
                "an accepted planning turn must include task_decomposition plus task records",
                "each task record must include safe taskId, title, summary, state, and order",
                "tasks[].primitiveRefs must reference primitive instanceId values exactly, not primitiveId values",
                "primitive taskRefs must reference taskId values exactly",
                "use only allowedFieldPaths exactly as provided",
                "use only allowed mood/media/tool ids exactly as provided",
                "tool_search_request and tool_gap_request are non-mutating outcomes for missing external context only"
            },
            manifest.AllowedFieldPaths,
            manifest.AllowedMoodTokens,
            manifest.AllowedMediaTokens,
            manifest.AllowedToolIds,
            manifest.MaxPrimitiveCount,
            OutputRequirements = new
            {
                PromptSpecific = true,
                IncludeDynamicLabels = true,
                IncludeDynamicPrompts = true,
                IncludeTasksWhenUseful = true,
                IncludeOptionsWhenAskingChoice = true,
                IncludeAtLeastOneNonTextPrimitive = true,
                IncludeTaskDecompositionForCompositeForm = true,
                ToolClaimsRequireToolContextRefs = true
            }
        }, JsonOptions);
    }

    private static string? SafeContextText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return PlannerPrimitiveRunner.ContainsUnsafeMarker(value) || value.Length > 240
            ? "context_redacted"
            : value;
    }
}
