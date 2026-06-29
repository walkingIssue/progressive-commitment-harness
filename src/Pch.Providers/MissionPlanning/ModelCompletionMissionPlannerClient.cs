using System.Text.Json;
using System.Text.Json.Serialization;
using Pch.Providers.Errors;
using Pch.Providers.ModelCompletion;

namespace Pch.Providers.MissionPlanning;

public sealed class ModelCompletionMissionPlannerClient : IMissionPlannerClient
{
    public const string JsonSchemaName = "mission_planner_result";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IModelCompletionClient _completionClient;

    public ModelCompletionMissionPlannerClient(IModelCompletionClient completionClient)
    {
        _completionClient = completionClient ?? throw new ArgumentNullException(nameof(completionClient));
    }

    public async Task<MissionPlannerResult> PlanAsync(
        MissionPlannerPacket packet,
        MissionPlannerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);

        var completion = await _completionClient.CompleteAsync(
            CreateCompletionRequest(packet, options),
            cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(completion.Content))
        {
            throw new ProviderEmptyResponseException(completion.Provider, "Mission planner returned empty structured content.");
        }

        MissionPlannerWireResult? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<MissionPlannerWireResult>(completion.Content, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new ProviderMalformedResponseException(completion.Provider, "Mission planner returned malformed structured JSON.", ex);
        }

        if (parsed is null)
        {
            throw new ProviderMalformedResponseException(completion.Provider, "Mission planner returned missing structured JSON.");
        }

        if (!string.Equals(packet.PacketId, parsed.PacketId, StringComparison.Ordinal))
        {
            throw new ProviderMalformedResponseException(completion.Provider, "Mission planner returned a packet id mismatch.");
        }

        var missionKind = parsed.MissionKind;
        if (!MissionKindPolicy.IsAllowed(missionKind))
        {
            throw new ProviderMalformedResponseException(completion.Provider, "Mission planner returned an unsupported mission kind.");
        }

        return new MissionPlannerResult(
            packet.PacketId,
            missionKind!,
            MapFields(completion.Provider, parsed.Fields),
            MapCommitments(completion.Provider, parsed.Commitments),
            MapConstraints(completion.Provider, parsed.Constraints),
            parsed.PendingConfirmations ?? [],
            parsed.MemoryDigest ?? string.Empty,
            completion.Content.Length,
            completion.Provider,
            completion.Model,
            completion.RequestId);
    }

    private static ModelCompletionRequest CreateCompletionRequest(MissionPlannerPacket packet, MissionPlannerOptions? options) =>
        new(
            [
                new ModelMessage(
                    ModelMessageRole.System,
                    "Return only strict JSON matching the supplied mission planner schema. Do not include markdown or commentary."),
                new ModelMessage(
                    ModelMessageRole.User,
                    JsonSerializer.Serialize(new
                    {
                        packet_id = packet.PacketId,
                        scenario = packet.Scenario,
                        locale = packet.Locale,
                        prompt = packet.Prompt,
                        known_constraints = packet.KnownConstraints
                    }, SerializerOptions))
            ],
            options?.Model,
            JsonSchemaName,
            MissionPlannerJsonSchema.Schema,
            options?.Temperature,
            options?.MaxTokens);

    private static IReadOnlyList<MissionFieldProposal> MapFields(string provider, IReadOnlyList<MissionFieldWire>? fields)
    {
        if (fields is null)
        {
            throw new ProviderMalformedResponseException(provider, "Mission planner returned missing fields.");
        }

        return fields.Select(field => new MissionFieldProposal(
            RequireText(provider, field.FieldPath, "field path"),
            RequireText(provider, field.Value, "field value"),
            MapSource(provider, field.AuthoritySource),
            field.EvidenceIds ?? [],
            field.RequiresConfirmation)).ToArray();
    }

    private static IReadOnlyList<MissionCommitmentProposal> MapCommitments(
        string provider,
        IReadOnlyList<MissionCommitmentWire>? commitments)
    {
        if (commitments is null)
        {
            throw new ProviderMalformedResponseException(provider, "Mission planner returned missing commitments.");
        }

        return commitments.Select(commitment => new MissionCommitmentProposal(
            RequireText(provider, commitment.CommitmentId, "commitment id"),
            RequireText(provider, commitment.CommitmentKind, "commitment kind"),
            RequireText(provider, commitment.Title, "commitment title"),
            commitment.StartsAt,
            commitment.EndsAt,
            commitment.Location,
            commitment.IsIrreversible,
            commitment.RequiresSpend,
            MapPriority(provider, commitment.CommitmentPriority),
            MapSource(provider, commitment.AuthoritySource),
            commitment.EvidenceIds ?? [])).ToArray();
    }

    private static IReadOnlyList<MissionConstraintProposal> MapConstraints(
        string provider,
        IReadOnlyList<MissionConstraintWire>? constraints)
    {
        if (constraints is null)
        {
            throw new ProviderMalformedResponseException(provider, "Mission planner returned missing constraints.");
        }

        return constraints.Select(constraint => new MissionConstraintProposal(
            RequireText(provider, constraint.ConstraintId, "constraint id"),
            RequireText(provider, constraint.Label, "constraint label"),
            RequireText(provider, constraint.Value, "constraint value"),
            MapSource(provider, constraint.AuthoritySource),
            constraint.IsHard,
            constraint.EvidenceIds ?? [])).ToArray();
    }

    private static string RequireText(string provider, string? value, string fieldName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ProviderMalformedResponseException(provider, $"Mission planner returned missing {fieldName}.")
            : value;

    private static MissionProposalSource MapSource(string provider, string? source) =>
        source switch
        {
            "user_stated" => MissionProposalSource.UserStated,
            "model_inferred" => MissionProposalSource.ModelInferred,
            _ => throw new ProviderMalformedResponseException(provider, "Mission planner returned an unsupported authority source.")
        };

    private static MissionCommitmentPriority MapPriority(string provider, string? priority) =>
        priority switch
        {
            "normal" => MissionCommitmentPriority.Normal,
            "high" => MissionCommitmentPriority.High,
            "critical" => MissionCommitmentPriority.Critical,
            _ => throw new ProviderMalformedResponseException(provider, "Mission planner returned an unsupported commitment priority.")
        };

    private sealed record MissionPlannerWireResult(
        [property: JsonPropertyName("packet_id")] string? PacketId,
        [property: JsonPropertyName("mission_kind")] string? MissionKind,
        IReadOnlyList<MissionFieldWire>? Fields,
        IReadOnlyList<MissionCommitmentWire>? Commitments,
        IReadOnlyList<MissionConstraintWire>? Constraints,
        [property: JsonPropertyName("pending_confirmations")] IReadOnlyList<string>? PendingConfirmations,
        [property: JsonPropertyName("memory_digest")] string? MemoryDigest);

    private sealed record MissionFieldWire(
        [property: JsonPropertyName("field_path")] string? FieldPath,
        string? Value,
        [property: JsonPropertyName("authority_source")] string? AuthoritySource,
        [property: JsonPropertyName("evidence_ids")] IReadOnlyList<string>? EvidenceIds,
        [property: JsonPropertyName("requires_confirmation")] bool RequiresConfirmation);

    private sealed record MissionConstraintWire(
        [property: JsonPropertyName("constraint_id")] string? ConstraintId,
        string? Label,
        string? Value,
        [property: JsonPropertyName("authority_source")] string? AuthoritySource,
        [property: JsonPropertyName("is_hard")] bool IsHard,
        [property: JsonPropertyName("evidence_ids")] IReadOnlyList<string>? EvidenceIds);

    private sealed record MissionCommitmentWire(
        [property: JsonPropertyName("commitment_id")] string? CommitmentId,
        [property: JsonPropertyName("commitment_kind")] string? CommitmentKind,
        string? Title,
        [property: JsonPropertyName("starts_at")] DateTimeOffset? StartsAt,
        [property: JsonPropertyName("ends_at")] DateTimeOffset? EndsAt,
        string? Location,
        [property: JsonPropertyName("is_irreversible")] bool IsIrreversible,
        [property: JsonPropertyName("requires_spend")] bool RequiresSpend,
        [property: JsonPropertyName("commitment_priority")] string? CommitmentPriority,
        [property: JsonPropertyName("authority_source")] string? AuthoritySource,
        [property: JsonPropertyName("evidence_ids")] IReadOnlyList<string>? EvidenceIds);
}
