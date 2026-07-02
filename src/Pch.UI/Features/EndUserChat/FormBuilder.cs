namespace Pch.UI.Features.EndUserChat;

public sealed class FormBuilder
{
    public PrimitiveAnswerDto BuildAnswer(
        EndUserValidatedTurnView turn,
        ValidatedPrimitive form,
        IReadOnlyDictionary<string, string> fieldValues)
    {
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(fieldValues);

        var accepted = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in form.Fields)
        {
            accepted[field.FieldId] = fieldValues.TryGetValue(field.FieldId, out var value)
                ? value.Trim()
                : field.Value.Trim();
        }

        return new PrimitiveAnswerDto(
            turn.SessionId,
            turn.TurnId,
            turn.GraphRevision,
            form.InstanceId,
            accepted);
    }

    public IReadOnlyList<PrimitiveValidationError> Validate(ValidatedPrimitive form, PrimitiveAnswerDto answer)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(answer);

        var errors = new List<PrimitiveValidationError>();
        foreach (var field in form.Fields)
        {
            answer.FieldValues.TryGetValue(field.FieldId, out var value);
            if (field.IsRequired && string.IsNullOrWhiteSpace(value))
            {
                errors.Add(new(field.FieldId, "answer_required", "Required answer is missing."));
                continue;
            }

            if (field.AllowedValues.Count > 0 &&
                !string.IsNullOrWhiteSpace(value) &&
                !SubmittedValuesAllowed(field, value))
            {
                errors.Add(new(field.FieldId, "answer_choice_not_allowed", "Answer is outside the validated choices."));
            }
        }

        return errors;
    }

    private static bool SubmittedValuesAllowed(ValidatedPrimitiveField field, string value)
    {
        var submitted = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (submitted.Length == 0)
        {
            submitted = [value];
        }

        return submitted.All(item => field.AllowedValues.Contains(item, StringComparer.Ordinal));
    }
}
