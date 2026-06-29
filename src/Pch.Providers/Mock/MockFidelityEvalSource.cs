using Pch.Providers.Errors;
using Pch.Providers.Fidelity;

namespace Pch.Providers.Mock;

public sealed class MockFidelityEvalSource : IFidelityEvalSource
{
    public const string ProviderName = "mock-fidelity-eval";
    public const string ModelName = "mock-fidelity-deterministic";

    private readonly MockFidelityEvalBehavior _behavior;

    public MockFidelityEvalSource(
        FidelityEvalSourceKind sourceKind,
        MockFidelityEvalBehavior behavior = MockFidelityEvalBehavior.SchemaValid)
    {
        SourceKind = sourceKind;
        _behavior = behavior;
    }

    public FidelityEvalSourceKind SourceKind { get; }

    public Task<FidelityEvalSourceResult> EvaluateAsync(
        FidelityEvalPacket packet,
        FidelityEvalOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return _behavior switch
        {
            MockFidelityEvalBehavior.Timeout => Task.FromException<FidelityEvalSourceResult>(
                new TimeoutException("Mock fidelity source timed out.")),
            MockFidelityEvalBehavior.ProviderError => Task.FromException<FidelityEvalSourceResult>(
                new ProviderUnavailableException(ProviderName, "Mock fidelity provider unavailable.")),
            MockFidelityEvalBehavior.SchemaInvalid => Task.FromResult(CreateResult(
                packet,
                FidelityEvalSourceResultKind.SchemaInvalid,
                CreateVerdicts(packet, SourceKind),
                ["schema_valid"])),
            MockFidelityEvalBehavior.UnsupportedClaim => Task.FromResult(CreateResult(
                packet,
                FidelityEvalSourceResultKind.Completed,
                CreateVerdicts(packet, SourceKind),
                ["unsupported_model_claim"])),
            MockFidelityEvalBehavior.MissingCandidateId => Task.FromResult(CreateResult(
                packet,
                FidelityEvalSourceResultKind.Completed,
                CreateVerdicts(packet, SourceKind).Take(Math.Max(0, packet.Candidates.Count - 1)).ToArray(),
                ["schema_valid", "decision_allowlisted"])),
            MockFidelityEvalBehavior.FallbackRequired => Task.FromResult(CreateResult(
                packet,
                FidelityEvalSourceResultKind.FallbackRequired,
                CreateVerdicts(packet, SourceKind),
                ["schema_valid"])),
            MockFidelityEvalBehavior.SchemaValidDisagreement => Task.FromResult(CreateResult(
                packet,
                FidelityEvalSourceResultKind.Completed,
                CreateVerdicts(packet, SourceKind, disagree: true),
                AllowedClaimCodes())),
            _ => Task.FromResult(CreateResult(
                packet,
                FidelityEvalSourceResultKind.Completed,
                CreateVerdicts(packet, SourceKind),
                AllowedClaimCodes()))
        };
    }

    private FidelityEvalSourceResult CreateResult(
        FidelityEvalPacket packet,
        FidelityEvalSourceResultKind kind,
        IReadOnlyList<FidelityCandidateVerdict> verdicts,
        IReadOnlyList<string> claimCodes) =>
        new(
            packet.PacketId,
            SourceKind,
            kind,
            verdicts,
            claimCodes,
            ResponseContentLength: 256,
            ProviderName,
            ModelName,
            $"mock-fidelity-{SourceKind.ToString().ToLowerInvariant()}-{packet.PacketId}");

    private static IReadOnlyList<FidelityCandidateVerdict> CreateVerdicts(
        FidelityEvalPacket packet,
        FidelityEvalSourceKind sourceKind,
        bool disagree = false)
    {
        return packet.Candidates
            .Select(candidate => new FidelityCandidateVerdict(
                candidate.CandidateId,
                disagree && sourceKind == FidelityEvalSourceKind.StrongModel
                    ? FidelityCandidateDecision.Exclude
                    : DefaultDecision(candidate.Category)))
            .ToArray();
    }

    private static FidelityCandidateDecision DefaultDecision(FidelityCandidateCategory category) =>
        category switch
        {
            FidelityCandidateCategory.Dining or FidelityCandidateCategory.Activity => FidelityCandidateDecision.Include,
            FidelityCandidateCategory.Transit or FidelityCandidateCategory.Downtime => FidelityCandidateDecision.Defer,
            _ => FidelityCandidateDecision.Exclude
        };

    private static IReadOnlyList<string> AllowedClaimCodes() =>
        ["schema_valid", "candidate_id_set_complete", "decision_allowlisted", "harness_rule_comparable"];
}

public enum MockFidelityEvalBehavior
{
    SchemaValid,
    SchemaInvalid,
    UnsupportedClaim,
    MissingCandidateId,
    Timeout,
    ProviderError,
    FallbackRequired,
    SchemaValidDisagreement
}
