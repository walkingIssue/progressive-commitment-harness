using Pch.Core;

namespace Pch.Harness;

public sealed class TripSession
{
    private readonly List<CandidatePool> _candidatePools = [];
    private readonly List<HarnessAction> _actions = [];

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

    public void AddCandidatePool(CandidatePool pool) => _candidatePools.Add(pool);

    public void RecordAction(HarnessAction action) => _actions.Add(action);

    public void MoveTo(HarnessStage stage) => Stage = stage;
}
