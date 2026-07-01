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

        if (result.Primitives.Any(HasSemanticTextInputMismatch))
        {
            return new(
                PlannerPrimitiveRunner.OutcomePrimitiveRendererMismatch,
                "primitive_renderer_mismatch");
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
}

internal sealed record PlannerPrimitiveSemanticFailure(string OutcomeCode, string FailureClassCode);
