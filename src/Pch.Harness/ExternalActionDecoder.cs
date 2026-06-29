using System.Text.Json;
using Pch.Core;

namespace Pch.Harness;

public sealed record ExternalActionProposal(
    string ActionId,
    string Kind,
    JsonElement Arguments);

public sealed record ActionDecodeResult(
    bool IsDecoded,
    HarnessAction? Action,
    string Code,
    string Summary)
{
    public static ActionDecodeResult Decoded(HarnessAction action) => new(true, action, "decoded", "Decoded action proposal.");

    public static ActionDecodeResult Failed(string code, string summary) => new(false, null, code, summary);
}

public sealed class ExternalActionDecoder
{
    public ActionDecodeResult Decode(ExternalActionProposal proposal)
    {
        if (string.IsNullOrWhiteSpace(proposal.ActionId))
        {
            return ActionDecodeResult.Failed("missing_action_id", "Action proposal is missing an action id.");
        }

        if (!HarnessAction.KnownKinds.Contains(proposal.Kind))
        {
            return ActionDecodeResult.Failed("unknown_action_kind", "Action proposal has an unknown kind.");
        }

        if (proposal.Arguments.ValueKind is not JsonValueKind.Object)
        {
            return ActionDecodeResult.Failed("invalid_arguments", "Action proposal arguments must be an object.");
        }

        return proposal.Kind switch
        {
            HarnessAction.EmitFormKind => DecodeEmitForm(proposal),
            HarnessAction.EmitChoiceSetKind => DecodeChoiceSet(proposal),
            HarnessAction.ProposeSearchKind => DecodeSearch(proposal),
            HarnessAction.SummarizeKind => DecodeSummary(proposal),
            HarnessAction.RequestApprovalKind => DecodeApproval(proposal),
            HarnessAction.StatePatchKind => DecodePatch(proposal),
            HarnessAction.DeferSlotKind => DecodeDefer(proposal),
            HarnessAction.HandoffKind => DecodeHandoff(proposal),
            _ => ActionDecodeResult.Failed("unknown_action_kind", "Action proposal has an unknown kind.")
        };
    }

    public ActionDecodeResult DecodeJson(string actionId, string kind, string jsonArguments)
    {
        try
        {
            using var document = JsonDocument.Parse(jsonArguments);
            return Decode(new ExternalActionProposal(actionId, kind, document.RootElement.Clone()));
        }
        catch (JsonException)
        {
            return ActionDecodeResult.Failed("malformed_json", "Action proposal arguments are malformed JSON.");
        }
    }

    private static ActionDecodeResult DecodeEmitForm(ExternalActionProposal proposal)
    {
        var formId = RequiredString(proposal.Arguments, "form_id");
        var title = RequiredString(proposal.Arguments, "title");
        if (formId is null || title is null)
        {
            return MissingRequired();
        }

        return ActionDecodeResult.Decoded(new EmitFormAction(
            proposal.ActionId,
            new FormRequest(formId, title, OptionalString(proposal.Arguments, "submit_label") ?? "Continue", [])));
    }

    private static ActionDecodeResult DecodeChoiceSet(ExternalActionProposal proposal)
    {
        var title = RequiredString(proposal.Arguments, "title");
        var ids = RequiredStringArray(proposal.Arguments, "candidate_ids");
        var maxSelectable = OptionalInt(proposal.Arguments, "max_selectable") ?? 1;
        if (title is null || ids is null)
        {
            return MissingRequired();
        }

        if (maxSelectable <= 0)
        {
            return ActionDecodeResult.Failed("invalid_arguments", "Action proposal arguments failed validation.");
        }

        var choices = ids
            .Select(id => new CandidateSummary(id, "Unknown", id, "Decoded external candidate reference.", []))
            .ToArray();

        return ActionDecodeResult.Decoded(new EmitChoiceSetAction(proposal.ActionId, title, choices, maxSelectable));
    }

    private static ActionDecodeResult DecodeSearch(ExternalActionProposal proposal)
    {
        var query = RequiredString(proposal.Arguments, "query");
        var surface = RequiredString(proposal.Arguments, "search_surface");
        if (query is null || surface is null)
        {
            return MissingRequired();
        }

        return ActionDecodeResult.Decoded(new ProposeSearchAction(
            proposal.ActionId,
            query,
            surface,
            OptionalStringArray(proposal.Arguments, "required_evidence_kinds") ?? []));
    }

    private static ActionDecodeResult DecodeSummary(ExternalActionProposal proposal)
    {
        var audience = RequiredString(proposal.Arguments, "audience");
        if (audience is null)
        {
            return MissingRequired();
        }

        return ActionDecodeResult.Decoded(new SummarizeAction(
            proposal.ActionId,
            audience,
            OptionalStringArray(proposal.Arguments, "claim_ids") ?? []));
    }

    private static ActionDecodeResult DecodeApproval(ExternalActionProposal proposal)
    {
        var approvalId = RequiredString(proposal.Arguments, "approval_id");
        var actionId = RequiredString(proposal.Arguments, "approval_action_id");
        var prompt = RequiredString(proposal.Arguments, "prompt");
        if (approvalId is null || actionId is null || prompt is null)
        {
            return MissingRequired();
        }

        return ActionDecodeResult.Decoded(new RequestApprovalAction(
            proposal.ActionId,
            new ApprovalRequest(
                approvalId,
                actionId,
                prompt,
                OptionalStringArray(proposal.Arguments, "risk_flags") ?? [],
                OptionalDecimal(proposal.Arguments, "spend_amount"),
                OptionalString(proposal.Arguments, "currency"),
                OptionalString(proposal.Arguments, "approval_token"))));
    }

    private static ActionDecodeResult DecodePatch(ExternalActionProposal proposal)
    {
        var patchId = RequiredString(proposal.Arguments, "patch_id");
        var path = RequiredString(proposal.Arguments, "path");
        var proposedValue = RequiredString(proposal.Arguments, "proposed_value");
        if (patchId is null || path is null || proposedValue is null)
        {
            return MissingRequired();
        }

        return ActionDecodeResult.Decoded(new StatePatchAction(
            proposal.ActionId,
            new StatePatchProposal(
                patchId,
                AuthoritySource.SmallModelDraft,
                path,
                OptionalString(proposal.Arguments, "current_value"),
                proposedValue,
                OptionalStringArray(proposal.Arguments, "evidence_ids") ?? [])));
    }

    private static ActionDecodeResult DecodeDefer(ExternalActionProposal proposal)
    {
        var slotId = RequiredString(proposal.Arguments, "slot_id");
        var reason = RequiredString(proposal.Arguments, "reason");
        return slotId is null || reason is null
            ? MissingRequired()
            : ActionDecodeResult.Decoded(new DeferSlotAction(proposal.ActionId, slotId, reason));
    }

    private static ActionDecodeResult DecodeHandoff(ExternalActionProposal proposal)
    {
        var target = RequiredString(proposal.Arguments, "target");
        var reason = RequiredString(proposal.Arguments, "reason");
        return target is null || reason is null
            ? MissingRequired()
            : ActionDecodeResult.Decoded(new HandoffAction(proposal.ActionId, target, reason));
    }

    private static ActionDecodeResult MissingRequired()
    {
        return ActionDecodeResult.Failed("missing_required_argument", "Action proposal is missing a required argument.");
    }

    private static string? RequiredString(JsonElement arguments, string name)
    {
        var value = OptionalString(arguments, name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? OptionalString(JsonElement arguments, string name)
    {
        return arguments.TryGetProperty(name, out var property) && property.ValueKind is JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static IReadOnlyList<string>? RequiredStringArray(JsonElement arguments, string name)
    {
        var values = OptionalStringArray(arguments, name);
        return values is { Count: > 0 } ? values : null;
    }

    private static IReadOnlyList<string>? OptionalStringArray(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var property) || property.ValueKind is not JsonValueKind.Array)
        {
            return null;
        }

        var values = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind is not JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                return null;
            }

            values.Add(item.GetString()!);
        }

        return values;
    }

    private static int? OptionalInt(JsonElement arguments, string name)
    {
        return arguments.TryGetProperty(name, out var property) && property.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static decimal? OptionalDecimal(JsonElement arguments, string name)
    {
        return arguments.TryGetProperty(name, out var property) && property.TryGetDecimal(out var value)
            ? value
            : null;
    }
}
