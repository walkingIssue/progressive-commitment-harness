using System.Text.Json;
using Pch.Providers.CandidateExpansion;
using Pch.Providers.EvidenceExport;
using Pch.Providers.HoldPreparation;
using Pch.Providers.MissionPlanning;
using Pch.Providers.ModelActions;
using Xunit;

namespace Pch.Providers.Tests;

public sealed class SanitizedEvalArtifactTests
{
    private const string RawPrompt = "RAW_PROMPT_TEXT_SHOULD_NOT_PERSIST";
    private const string RawProviderPayload = "RAW_PROVIDER_PAYLOAD_SHOULD_NOT_PERSIST";
    private const string ApprovalToken = "APPROVAL_TOKEN_SHOULD_NOT_PERSIST";
    private const string HoldReference = "RAW_HOLD_REFERENCE_SHOULD_NOT_PERSIST";
    private const string CandidateDisplayValue = "CANDIDATE_DISPLAY_VALUE_SHOULD_NOT_PERSIST";
    private const string Credential = "sk-credential-sentinel-should-not-persist";
    private const string RawException = "RAW_EXCEPTION_MESSAGE_SHOULD_NOT_PERSIST";
    private const string GenericSentinel = "GENERIC_SENTINEL_SHOULD_NOT_PERSIST";

    private static readonly string[] SensitiveSentinels =
    [
        RawPrompt,
        RawProviderPayload,
        ApprovalToken,
        HoldReference,
        CandidateDisplayValue,
        Credential,
        RawException,
        GenericSentinel
    ];

    [Fact]
    public async Task RejectedRowsAcrossProviderLanesDoNotPersistSensitiveValues()
    {
        var missionPacket = CreateMissionPlannerPacket();
        var candidatePacket = CreateCandidateExpansionPacket();
        var holdPacket = CreateHoldPreparationPacket();
        var evidencePacket = CreateEvidenceExportPacket();

        var missionRow = Assert.Single(await new MissionPlannerEvaluator(new StaticMissionPlanner(
            CreateMissionPlannerResult(missionPacket.PacketId) with
            {
                MissionKind = RawProviderPayload,
                Provider = Credential,
                Model = RawPrompt,
                RequestId = ApprovalToken
            })).EvaluateAsync([new MissionPlannerEvalCase("mission-rejected", missionPacket, "vacation")]));

        var candidateRow = Assert.Single(await new CandidateExpansionEvaluator(new StaticCandidateExpansionSource(
            new CandidateExpansionResult(
                candidatePacket.PacketId,
                [
                    new CandidateSlotExpansion(
                        "slot-dining",
                        CandidateCategory.Dining,
                        [
                            new ItineraryCandidate(
                                "candidate-provider-result",
                                CandidateCategory.Dining,
                                CandidateDisplayValue,
                                [RawProviderPayload, GenericSentinel],
                                60,
                                CandidateCostLevel.High,
                                RequiresBooking: true)
                        ])
                ],
                99,
                Credential,
                RawPrompt,
                ApprovalToken))).EvaluateAsync([new CandidateExpansionEvalCase("candidate-rejected", candidatePacket)]));

        var holdRow = Assert.Single(await new HoldPreparationEvaluator(new StaticHoldPreparationAdapter(
            CreateHoldPreparationResult(holdPacket.PacketId))).EvaluateAsync(
                [new HoldPreparationEvalCase("hold-rejected", holdPacket)],
                new HoldPreparationOptions(RequiredApprovalToken: "expected-token")));

        var evidenceRow = Assert.Single(await new EvidenceExportEvaluator(new StaticEvidenceExportProvider(
            CreateEvidenceExportResult(evidencePacket.PacketId))).EvaluateAsync(
                [new EvidenceExportEvalCase("evidence-rejected", evidencePacket)]));

        Assert.False(missionRow.Passed);
        Assert.Equal(MissionPlannerEvaluator.OutcomeUnsupportedMissionKind, missionRow.OutcomeCode);
        Assert.Null(missionRow.Provider);
        Assert.Null(missionRow.RequestId);

        Assert.False(candidateRow.Passed);
        Assert.Equal(CandidateExpansionEvaluator.OutcomeSlotMismatch, candidateRow.OutcomeCode);
        Assert.Empty(candidateRow.Slots);
        Assert.Null(candidateRow.Provider);

        Assert.False(holdRow.Passed);
        Assert.Equal(HoldPreparationEvaluator.OutcomeApprovalMismatch, holdRow.OutcomeCode);
        Assert.Empty(holdRow.Candidates);
        Assert.Null(holdRow.Provider);

        Assert.False(evidenceRow.Passed);
        Assert.Equal(EvidenceExportEvaluator.OutcomeResultMismatch, evidenceRow.OutcomeCode);
        Assert.Null(evidenceRow.PlanId);
        Assert.Null(evidenceRow.Provider);

        foreach (var artifact in new object[] { missionRow, candidateRow, holdRow, evidenceRow })
        {
            SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(artifact, SensitiveSentinels);
        }
    }

    [Fact]
    public async Task ErrorRowsAcrossProviderLanesUseFixedCodesWithoutRawExceptionText()
    {
        var exception = new InvalidOperationException(
            $"{RawException} {RawPrompt} {RawProviderPayload} {ApprovalToken} {HoldReference} {CandidateDisplayValue} {Credential} {GenericSentinel}");

        var modelPacket = CreateModelActionPacket();
        var missionPacket = CreateMissionPlannerPacket();
        var candidatePacket = CreateCandidateExpansionPacket();
        var holdPacket = CreateHoldPreparationPacket();
        var evidencePacket = CreateEvidenceExportPacket();

        var legacyModelRow = Assert.Single(await new ModelActionEvaluator(new ThrowingModelActionRunner(exception))
            .EvaluateAsync([new ModelActionEvalCase("model-error", modelPacket, "emit_form")]));
        var sanitizedModelRow = Assert.Single(await new SanitizedModelActionEvalRunner(new ThrowingModelActionRunner(exception))
            .EvaluateAsync([new ModelActionEvalCase("model-sanitized-error", modelPacket, "emit_form")]));
        var missionRow = Assert.Single(await new MissionPlannerEvaluator(new ThrowingMissionPlanner(exception))
            .EvaluateAsync([new MissionPlannerEvalCase("mission-error", missionPacket, "vacation")]));
        var candidateRow = Assert.Single(await new CandidateExpansionEvaluator(new ThrowingCandidateExpansionSource(exception))
            .EvaluateAsync([new CandidateExpansionEvalCase("candidate-error", candidatePacket)]));
        var holdRow = Assert.Single(await new HoldPreparationEvaluator(new ThrowingHoldPreparationAdapter(exception))
            .EvaluateAsync([new HoldPreparationEvalCase("hold-error", holdPacket)]));
        var evidenceRow = Assert.Single(await new EvidenceExportEvaluator(new ThrowingEvidenceExportProvider(exception))
            .EvaluateAsync([new EvidenceExportEvalCase("evidence-error", evidencePacket)]));

        Assert.Equal("unexpected_error", legacyModelRow.Error);
        Assert.Equal("unexpected_error", sanitizedModelRow.ErrorCode);
        Assert.Equal("mission_planner_error", missionRow.ErrorCode);
        Assert.Equal("candidate_expansion_error", candidateRow.ErrorCode);
        Assert.Equal("hold_preparation_error", holdRow.ErrorCode);
        Assert.Equal("evidence_export_error", evidenceRow.ErrorCode);

        foreach (var artifact in new object[] { legacyModelRow, sanitizedModelRow, missionRow, candidateRow, holdRow, evidenceRow })
        {
            SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(artifact, SensitiveSentinels);
        }
    }

    private static ModelActionPacket CreateModelActionPacket()
    {
        var input = JsonSerializer.SerializeToElement(new
        {
            prompt = RawPrompt,
            credential = Credential,
            payload = RawProviderPayload
        });

        return new ModelActionPacket(
            "packet-model-action",
            "stage-test",
            RawPrompt,
            [RawProviderPayload],
            new Dictionary<string, JsonElement> { ["payload"] = input },
            [new ModelActionDefinition("emit_form", "Emit a safe form.")]);
    }

    private static MissionPlannerPacket CreateMissionPlannerPacket() =>
        new(
            "packet-mission",
            "vacation",
            RawPrompt,
            "en-US",
            [RawProviderPayload, Credential]);

    private static MissionPlannerResult CreateMissionPlannerResult(string packetId) =>
        new(
            packetId,
            "vacation",
            [
                new MissionFieldProposal(
                    "/mission/purpose",
                    RawProviderPayload,
                    MissionProposalSource.UserStated,
                    [GenericSentinel],
                    false)
            ],
            [
                new MissionCommitmentProposal(
                    "commitment-safe",
                    "downtime",
                    CandidateDisplayValue,
                    StartsAt: null,
                    EndsAt: null,
                    Location: null,
                    IsIrreversible: false,
                    RequiresSpend: true,
                    MissionCommitmentPriority.High,
                    MissionProposalSource.UserStated,
                    [GenericSentinel])
            ],
            [
                new MissionConstraintProposal(
                    "constraint-safe",
                    "Safe constraint",
                    RawPrompt,
                    MissionProposalSource.ModelInferred,
                    IsHard: true,
                    [GenericSentinel])
            ],
            [ApprovalToken],
            RawProviderPayload,
            101,
            "provider-safe-unless-replaced",
            "model-safe-unless-replaced",
            "request-safe-unless-replaced");

    private static CandidateExpansionPacket CreateCandidateExpansionPacket() =>
        new(
            "packet-candidates",
            [
                new CandidateExpansionSlot("slot-dining", CandidateCategory.Dining, "safe area hint", 75),
                new CandidateExpansionSlot("slot-activity", CandidateCategory.Activity, "safe area hint", 120)
            ],
            "en-US",
            $"{RawPrompt} {RawProviderPayload} {Credential}");

    private static HoldPreparationPacket CreateHoldPreparationPacket() =>
        new(
            "packet-hold",
            HoldPreparationOperation.Hold,
            [
                new SelectedItineraryCandidate("slot-dining", "candidate-dining", CandidateCategory.Dining)
            ],
            "en-US",
            ApprovalToken,
            $"{RawPrompt} {RawProviderPayload} {CandidateDisplayValue}");

    private static HoldPreparationResult CreateHoldPreparationResult(string packetId) =>
        new(
            packetId,
            HoldPreparationResultKind.HoldPrepared,
            [
                new HoldPreparationCandidateResult(
                    "slot-dining",
                    "candidate-dining",
                    CandidateCategory.Dining,
                    HoldPreparationCandidateStatus.HoldPrepared,
                    HoldReference)
            ],
            202,
            Credential,
            RawPrompt,
            GenericSentinel);

    private static EvidenceExportPacket CreateEvidenceExportPacket() =>
        new(
            "packet-export",
            new TripPlanEvidenceSummary(
                "plan-safe",
                DayCount: 2,
                SelectedCandidateCount: 1,
                DeferredCandidateCount: 1,
                PreparedHoldCount: 0,
                EvidenceCount: 2),
            [
                new TripPlanEvidenceItem("evidence-safe-1", EvidenceKind.Candidate, "candidate-dining"),
                new TripPlanEvidenceItem("evidence-safe-2", EvidenceKind.Hold, "slot-dining")
            ],
            [
                new TripPlanHoldOutcome("slot-dining", "candidate-dining", HoldOutcomeKind.Deferred, "evidence-safe-2")
            ],
            "en-US",
            $"{RawPrompt} {RawProviderPayload} {HoldReference} {CandidateDisplayValue}");

    private static EvidenceExportResult CreateEvidenceExportResult(string packetId) =>
        new(
            packetId,
            EvidenceExportResultKind.ExportReady,
            new TripPlanEvidenceExport(
                RawProviderPayload,
                DayCount: 2,
                SelectedCandidateCount: 1,
                DeferredCandidateCount: 1,
                PreparedHoldCount: 0,
                [GenericSentinel],
                [RawPrompt],
                [CandidateDisplayValue]),
            303,
            Credential,
            RawProviderPayload,
            ApprovalToken);

    private sealed class ThrowingModelActionRunner(Exception exception) : IModelActionRunner
    {
        public Task<ModelActionRunResult> RunAsync(
            ModelActionPacket packet,
            ModelActionRunnerOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ModelActionRunResult>(exception);
    }

    private sealed class StaticMissionPlanner(MissionPlannerResult result) : IMissionPlannerClient
    {
        public Task<MissionPlannerResult> PlanAsync(
            MissionPlannerPacket packet,
            MissionPlannerOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class ThrowingMissionPlanner(Exception exception) : IMissionPlannerClient
    {
        public Task<MissionPlannerResult> PlanAsync(
            MissionPlannerPacket packet,
            MissionPlannerOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<MissionPlannerResult>(exception);
    }

    private sealed class StaticCandidateExpansionSource(CandidateExpansionResult result) : ICandidateExpansionSource
    {
        public Task<CandidateExpansionResult> ExpandAsync(
            CandidateExpansionPacket packet,
            CandidateExpansionOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class ThrowingCandidateExpansionSource(Exception exception) : ICandidateExpansionSource
    {
        public Task<CandidateExpansionResult> ExpandAsync(
            CandidateExpansionPacket packet,
            CandidateExpansionOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<CandidateExpansionResult>(exception);
    }

    private sealed class StaticHoldPreparationAdapter(HoldPreparationResult result) : IHoldPreparationAdapter
    {
        public Task<HoldPreparationResult> PrepareAsync(
            HoldPreparationPacket packet,
            HoldPreparationOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class ThrowingHoldPreparationAdapter(Exception exception) : IHoldPreparationAdapter
    {
        public Task<HoldPreparationResult> PrepareAsync(
            HoldPreparationPacket packet,
            HoldPreparationOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<HoldPreparationResult>(exception);
    }

    private sealed class StaticEvidenceExportProvider(EvidenceExportResult result) : IEvidenceExportProvider
    {
        public Task<EvidenceExportResult> ExportAsync(
            EvidenceExportPacket packet,
            EvidenceExportOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class ThrowingEvidenceExportProvider(Exception exception) : IEvidenceExportProvider
    {
        public Task<EvidenceExportResult> ExportAsync(
            EvidenceExportPacket packet,
            EvidenceExportOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<EvidenceExportResult>(exception);
    }
}
