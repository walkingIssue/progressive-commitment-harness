namespace Pch.Core;

public sealed record StagePacket(
    string PacketId,
    string SessionId,
    string Stage,
    string CurrentSubtask,
    IReadOnlyList<string> LoadBearingFacts,
    IReadOnlyList<CandidateSummary> Candidates,
    IReadOnlyList<string> Constraints,
    IReadOnlyList<string> AuthorityHints,
    IReadOnlyList<string> AllowedActions,
    IReadOnlyList<string> TraceRequirements);

public sealed record CandidateSummary(
    string CandidateId,
    string Kind,
    string Title,
    string Summary,
    IReadOnlyList<string> EvidenceIds);

public sealed record FormRequest(
    string FormId,
    string Title,
    string SubmitLabel,
    IReadOnlyList<FormField> Fields);

public sealed record FormField(
    string FieldId,
    string Label,
    string FieldType,
    bool Required,
    string? CurrentValue,
    IReadOnlyList<string> Options);

public sealed record FormResponse(
    string FormId,
    IReadOnlyDictionary<string, string?> Values,
    DateTimeOffset SubmittedAt);

public sealed record ChoiceSelection(
    string ChoiceSetId,
    IReadOnlyList<string> CandidateIds,
    DateTimeOffset SelectedAt);

public sealed record ApprovalToken(
    string ApprovalId,
    string Token,
    DateTimeOffset ApprovedAt);

public sealed record ApprovalRequest(
    string ApprovalId,
    string ActionId,
    string Prompt,
    IReadOnlyList<string> RiskFlags,
    decimal? SpendAmount,
    string? Currency,
    string? ApprovalToken);

public sealed record StatePatchProposal(
    string PatchId,
    AuthoritySource Source,
    string Path,
    string? CurrentValue,
    string ProposedValue,
    IReadOnlyList<string> EvidenceIds);

public abstract record HarnessAction(string ActionId, string Kind)
{
    public const string EmitFormKind = "emit_form";
    public const string EmitChoiceSetKind = "emit_choice_set";
    public const string ProposeSearchKind = "propose_search";
    public const string SummarizeKind = "summarize";
    public const string RequestApprovalKind = "request_approval";
    public const string StatePatchKind = "state_patch";
    public const string DeferSlotKind = "defer_slot";
    public const string HandoffKind = "handoff";

    public static readonly IReadOnlySet<string> KnownKinds = new HashSet<string>
    {
        EmitFormKind,
        EmitChoiceSetKind,
        ProposeSearchKind,
        SummarizeKind,
        RequestApprovalKind,
        StatePatchKind,
        DeferSlotKind,
        HandoffKind
    };
}

public sealed record EmitFormAction(string ActionId, FormRequest Form)
    : HarnessAction(ActionId, EmitFormKind);

public sealed record EmitChoiceSetAction(
    string ActionId,
    string Title,
    IReadOnlyList<CandidateSummary> Choices,
    int MaxSelectable)
    : HarnessAction(ActionId, EmitChoiceSetKind);

public sealed record ProposeSearchAction(
    string ActionId,
    string Query,
    string SearchSurface,
    IReadOnlyList<string> RequiredEvidenceKinds)
    : HarnessAction(ActionId, ProposeSearchKind);

public sealed record SummarizeAction(
    string ActionId,
    string Audience,
    IReadOnlyList<string> ClaimIds)
    : HarnessAction(ActionId, SummarizeKind);

public sealed record RequestApprovalAction(string ActionId, ApprovalRequest Approval)
    : HarnessAction(ActionId, RequestApprovalKind);

public sealed record StatePatchAction(string ActionId, StatePatchProposal Patch)
    : HarnessAction(ActionId, StatePatchKind);

public sealed record DeferSlotAction(string ActionId, string SlotId, string Reason)
    : HarnessAction(ActionId, DeferSlotKind);

public sealed record HandoffAction(string ActionId, string Target, string Reason)
    : HarnessAction(ActionId, HandoffKind);
