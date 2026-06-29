using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pch.Providers.ModelActions;

public sealed record ModelActionPacket(
    string PacketId,
    string Stage,
    string Goal,
    IReadOnlyList<string> Instructions,
    IReadOnlyDictionary<string, JsonElement> Inputs,
    IReadOnlyList<ModelActionDefinition> AllowedActions);

public sealed record ModelActionDefinition(
    string Name,
    string Description,
    string? ArgumentsJsonSchema = null);

public sealed record ModelActionRunnerOptions(
    string? Model = null,
    double Temperature = 0,
    int MaxTokens = 800);

public sealed record ModelActionRunResult(
    string PacketId,
    string ActionName,
    JsonElement Arguments,
    string? Summary,
    int ResponseContentLength,
    string Provider,
    string Model,
    string? RequestId);

public sealed record ModelActionEvalCase(
    string Name,
    ModelActionPacket Packet,
    string ExpectedActionName);

public sealed record ModelActionEvalResult(
    string Name,
    bool Passed,
    string ExpectedActionName,
    string? ActualActionName,
    string? Error);

public sealed record GoldenPacketEvalDocument(IReadOnlyList<ModelActionEvalCase> Cases);

public sealed record SanitizedModelActionEvalRow(
    string Name,
    string PacketId,
    bool Passed,
    string ExpectedActionName,
    string? ActualActionName,
    string DecodeOutcomeCode,
    string IntakeOutcomeCode,
    string? ErrorCode,
    int? ResponseContentLength,
    string? Provider,
    string? Model,
    string? RequestId);

public sealed record ProviderLocalExternalActionProposal(
    string ProposalId,
    string PacketId,
    string ActionKind,
    IReadOnlyList<string> ArgumentKeys,
    string Provider,
    string Model,
    string? RequestId);

public sealed record ProviderRuntimeActionProposal(
    string ActionId,
    string Kind,
    JsonElement Arguments);

public sealed record ProviderActionBridgeResult(
    bool IsAccepted,
    string DecodeOutcomeCode,
    string IntakeOutcomeCode,
    [property: JsonIgnore] ProviderRuntimeActionProposal? RuntimeProposal,
    ProviderLocalExternalActionProposal? Proposal);
