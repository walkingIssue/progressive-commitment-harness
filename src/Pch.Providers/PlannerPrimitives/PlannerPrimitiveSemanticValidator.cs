namespace Pch.Providers.PlannerPrimitives;

internal static class PlannerPrimitiveSemanticValidator
{
    public static PlannerPrimitiveSemanticFailure? Validate(PlannerModelRequest request, PlannerModelResult result)
    {
        if (result.OutputKind != PlannerModelOutputKind.CompositeForm)
        {
            return null;
        }

        var primitiveIds = result.Primitives.Select(primitive => primitive.PrimitiveId).ToArray();
        var taskIds = result.Tasks.Select(task => task.TaskId).ToHashSet(StringComparer.Ordinal);
        var allowedFieldPaths = request.Manifest.AllowedFieldPaths.ToHashSet(StringComparer.Ordinal);

        if (result.Primitives.Any(primitive =>
            !string.IsNullOrWhiteSpace(primitive.FieldPath) &&
            !allowedFieldPaths.Contains(primitive.FieldPath)))
        {
            return new(
                PlannerPrimitiveRunner.OutcomeFieldPathNotAllowed,
                "field_path_not_allowed");
        }

        if (result.Primitives.Any(HasSemanticTextInputMismatch))
        {
            return new(
                PlannerPrimitiveRunner.OutcomePrimitiveRendererMismatch,
                "primitive_renderer_mismatch");
        }

        if (result.Primitives.Any(HasAnswerSchemaMismatch))
        {
            return new(
                PlannerPrimitiveRunner.OutcomeAnswerSchemaInvalid,
                "answer_schema_invalid");
        }

        if (!primitiveIds.Any(id => PlannerPrimitiveToolCatalog.NonTextInteractivePrimitiveIds.Contains(id, StringComparer.Ordinal)))
        {
            return new(
                PlannerPrimitiveRunner.OutcomePrimitiveRendererMismatch,
                "primitive_renderer_mismatch");
        }

        var taskPrimitive = result.Primitives.FirstOrDefault(primitive =>
            string.Equals(primitive.PrimitiveId, "task_decomposition", StringComparison.Ordinal));
        if (taskPrimitive is null ||
            result.Tasks.Count == 0 ||
            taskPrimitive.TaskRefs.Count == 0 ||
            taskPrimitive.TaskRefs.Any(taskRef => !taskIds.Contains(taskRef)))
        {
            return new(
                PlannerPrimitiveRunner.OutcomeTaskDecompositionMissing,
                "task_decomposition_missing");
        }

        if (result.Tasks.Any(task =>
            !PlannerPrimitiveRunner.IsSafeIdentifier(task.TaskId) ||
            string.IsNullOrWhiteSpace(task.Title) ||
            PlannerPrimitiveRunner.ContainsUnsafeMarker(task.Title) ||
            !IsAllowedTaskState(task.State) ||
            task.Order < 0 ||
            task.Order > request.Manifest.MaxPrimitiveCount * 4))
        {
            return new(
                PlannerPrimitiveRunner.OutcomeTaskDecompositionMissing,
                "task_decomposition_missing");
        }

        return null;
    }

    private static bool HasSemanticTextInputMismatch(PlannerPrimitiveInvocation primitive)
    {
        if (primitive.PrimitiveId is not ("text_input" or "textarea"))
        {
            return false;
        }

        var fieldPath = primitive.FieldPath ?? string.Empty;
        if (fieldPath.Contains("destination", StringComparison.OrdinalIgnoreCase) ||
            fieldPath.Contains("date", StringComparison.OrdinalIgnoreCase) ||
            fieldPath.Contains("start", StringComparison.OrdinalIgnoreCase) ||
            fieldPath.Contains("end", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return fieldPath.Contains("pace", StringComparison.OrdinalIgnoreCase) &&
            primitive.Options.Count > 0;
    }

    private static bool IsAllowedTaskState(string? state) =>
        state is "pending" or "active" or "blocked" or "complete";

    private static bool HasAnswerSchemaMismatch(PlannerPrimitiveInvocation primitive) =>
        primitive.PrimitiveId switch
        {
            "slider" or "number_input" =>
                primitive.Options.Count > 0 ||
                primitive.CandidateIds.Count > 0 ||
                (!string.IsNullOrWhiteSpace(primitive.DefaultValue) &&
                    !decimal.TryParse(
                        primitive.DefaultValue,
                        System.Globalization.NumberStyles.Number,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out _)),
            "select" or "radio_group" or "multi_select" or "choice_card" =>
                primitive.Options.Count == 0,
            "candidate_deck" =>
                primitive.CandidateIds.Count == 0 && primitive.Options.Count == 0,
            "date" or "date_range" =>
                primitive.Options.Count > 0 || primitive.CandidateIds.Count > 0,
            _ => false
        };
}

internal sealed record PlannerPrimitiveSemanticFailure(string OutcomeCode, string FailureClassCode);
