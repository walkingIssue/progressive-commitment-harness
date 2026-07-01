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
                !field.AllowedValues.Contains(value, StringComparer.Ordinal))
            {
                errors.Add(new(field.FieldId, "answer_choice_not_allowed", "Answer is outside the validated choices."));
            }
        }

        return errors;
    }
}
