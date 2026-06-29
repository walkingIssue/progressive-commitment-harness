using Pch.Core;

namespace Pch.Harness;

public sealed class TripSession
{
    private readonly List<CandidatePool> _candidatePools = [];
    private readonly List<HarnessAction> _actions = [];
    private readonly Dictionary<string, string?> _formValues = new(StringComparer.Ordinal);
    private readonly List<string> _selectedCandidateIds = [];
    private readonly List<ApprovalToken> _approvalTokens = [];
    private readonly List<DeferredSlot> _deferredSlots = [];
    private readonly List<HandoffRequest> _handoffs = [];
    private readonly List<ItinerarySlotDecision> _itineraryDecisions = [];
    private readonly Dictionary<string, HashSet<string>> _itineraryCandidatePoolIdsBySlot = new(StringComparer.Ordinal);
    private StructuredMemoryDigest? _memoryDigest;
    private ItineraryCompilationResult? _lastItineraryCompilation;

    public TripSession(
        string sessionId,
        TripMission mission,
        DecisionLedger? decisionLedger = null,
        EvidenceTrace? evidenceTrace = null,
        ClaimLedger? claimLedger = null,
        StateAuthorityPolicy? authorityPolicy = null)
    {
        SessionId = sessionId;
        Mission = mission;
        DecisionLedger = decisionLedger ?? DecisionLedger.Empty;
        EvidenceTrace = evidenceTrace ?? EvidenceTrace.Empty;
        ClaimLedger = claimLedger ?? ClaimLedger.Empty;
        AuthorityPolicy = authorityPolicy ?? StateAuthorityPolicy.Default;
    }

    public string SessionId { get; }

    public TripMission Mission { get; private set; }

    public HarnessStage Stage { get; private set; } = HarnessStage.Intake;

    public DecisionLedger DecisionLedger { get; private set; }

    public EvidenceTrace EvidenceTrace { get; private set; }

    public ClaimLedger ClaimLedger { get; private set; }

    public StateAuthorityPolicy AuthorityPolicy { get; }

    public IReadOnlyList<CandidatePool> CandidatePools => _candidatePools;

    public IReadOnlyList<HarnessAction> Actions => _actions;

    public IReadOnlyDictionary<string, string?> FormValues => _formValues;

    public IReadOnlyList<string> SelectedCandidateIds => _selectedCandidateIds;

    public IReadOnlyList<ApprovalToken> ApprovalTokens => _approvalTokens;

    public IReadOnlyList<DeferredSlot> DeferredSlots => _deferredSlots;

    public IReadOnlyList<HandoffRequest> Handoffs => _handoffs;

    public IReadOnlyList<ItinerarySlotDecision> ItineraryDecisions => _itineraryDecisions;

    public StructuredMemoryDigest? MemoryDigest => _memoryDigest;

    public ItineraryCompilationResult? LastItineraryCompilation => _lastItineraryCompilation;

    public void AddCandidatePool(CandidatePool pool) => _candidatePools.Add(pool);

    public void AddItineraryCandidatePool(string slotId, CandidatePool pool)
    {
        AddCandidatePool(pool);
        AssociateItineraryCandidatePool(slotId, pool.PoolId);
    }

    public void AssociateItineraryCandidatePool(string slotId, string poolId)
    {
        if (string.IsNullOrWhiteSpace(slotId) || string.IsNullOrWhiteSpace(poolId))
        {
            return;
        }

        if (!_itineraryCandidatePoolIdsBySlot.TryGetValue(slotId, out var associatedPoolIds))
        {
            associatedPoolIds = new HashSet<string>(StringComparer.Ordinal);
            _itineraryCandidatePoolIdsBySlot[slotId] = associatedPoolIds;
        }

        associatedPoolIds.Add(poolId);
    }

    public void RecordAction(HarnessAction action) => _actions.Add(action);

    public void RecordDecision(DecisionRecord decision)
    {
        DecisionLedger = new DecisionLedger([.. DecisionLedger.Records, decision]);
    }

    public void MoveTo(HarnessStage stage) => Stage = stage;

    public void ReplaceMission(TripMission mission) => Mission = mission;

    public void ReplaceMemoryDigest(StructuredMemoryDigest digest) => _memoryDigest = digest;

    public void ReplaceItineraryCompilation(ItineraryCompilationResult result) => _lastItineraryCompilation = result;

    public void ApplyFormResponse(FormResponse response)
    {
        foreach (var (key, value) in response.Values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            _formValues[$"{response.FormId}.{key}"] = value;
        }
    }

    public CandidateSelectionResult SelectCandidates(ChoiceSelection selection)
    {
        var unknownIds = selection.CandidateIds
            .Where(candidateId => !IsKnownCandidateId(candidateId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (unknownIds.Length > 0)
        {
            return CandidateSelectionResult.Failed(unknownIds);
        }

        foreach (var candidateId in selection.CandidateIds)
        {
            if (!_selectedCandidateIds.Contains(candidateId, StringComparer.Ordinal))
            {
                _selectedCandidateIds.Add(candidateId);
            }
        }

        return CandidateSelectionResult.Selected(selection.CandidateIds);
    }

    public void RecordApproval(ApprovalToken token) => _approvalTokens.Add(token);

    public void DeferSlot(string slotId, string reason) => _deferredSlots.Add(new(slotId, reason));

    public void RecordHandoff(string target, string reason) => _handoffs.Add(new(target, reason));

    public void RecordItineraryDecision(ItinerarySlotDecision decision) => _itineraryDecisions.Add(decision);

    public bool HasApprovalToken(string approvalId)
    {
        return _approvalTokens.Any(token => string.Equals(token.ApprovalId, approvalId, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(token.Token));
    }

    public bool HasItineraryCandidateForSlot(string slotId, string candidateId)
    {
        if (!_itineraryCandidatePoolIdsBySlot.TryGetValue(slotId, out var poolIds))
        {
            return false;
        }

        return _candidatePools
            .Where(pool => poolIds.Contains(pool.PoolId))
            .SelectMany(pool => pool.Candidates)
            .Any(candidate => string.Equals(candidate.CandidateId, candidateId, StringComparison.Ordinal));
    }

    private bool IsKnownCandidateId(string candidateId)
    {
        return _candidatePools.SelectMany(pool => pool.Candidates)
            .Any(candidate => string.Equals(candidate.CandidateId, candidateId, StringComparison.Ordinal));
    }
}

public sealed record DeferredSlot(string SlotId, string Reason);

public sealed record HandoffRequest(string Target, string Reason);

public sealed record CandidateSelectionResult(
    bool IsAccepted,
    IReadOnlyList<string> AcceptedCandidateIds,
    IReadOnlyList<string> UnknownCandidateIds)
{
    public static CandidateSelectionResult Selected(IReadOnlyList<string> candidateIds)
    {
        return new(true, candidateIds.Distinct(StringComparer.Ordinal).ToArray(), []);
    }

    public static CandidateSelectionResult Failed(IReadOnlyList<string> unknownCandidateIds)
    {
        return new(false, [], unknownCandidateIds);
    }
}
