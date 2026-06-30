using System.Text.Json;
using Pch.Harness;
using Pch.Providers.Errors;
using Pch.Providers.LiveMissionProposal;
using Pch.Providers.LivePreflight;
using Pch.Providers.ModelCompletion;
using Pch.Providers.ModelRoles;
using Pch.UI.Features.EndUserChat;
using Xunit;

namespace Pch.UI.Tests;

public sealed class EndUserChatServiceTests
{
    [Fact]
    public void InitialStateShowsDeterministicOfflineModeAndAccessibleTranscriptSeed()
    {
        var service = new EndUserChatService();

        var state = service.CreateInitialState();

        Assert.Equal("offline-deterministic", state.ModeState);
        Assert.Equal(ModelRoleStatusEvaluator.OutcomeReady, state.RoleStatusOutcome);
        Assert.Equal("deterministic-offline", state.RoleStatusActiveRole);
        Assert.Equal("deterministic-offline", state.SelectedModelRole);
        Assert.Equal("deterministic_default", state.LivePreflightState);
        Assert.Equal("not_requested", state.LiveProposalState);
        Assert.Equal("not_run", state.HarnessValidationState);
        Assert.Equal("deterministic_fallback", state.LatestTurnSource);
        Assert.Equal("not_attempted", state.ProviderRequestState);
        Assert.Equal("verified", state.RawAbsenceState);
        Assert.Equal("idle", state.FinalState);
        Assert.Equal("not_requested", state.ApprovalState);
        Assert.Equal("deterministic_fallback_active", state.ProviderOutcome);
        Assert.Contains(state.Turns, turn => turn.TurnId == "turn-system-ready"
            && turn.Role == "system"
            && turn.OutcomeCode == "offline-deterministic");
        Assert.Contains(state.Turns, turn => turn.TurnId == "turn-assistant-start"
            && turn.Role == "assistant"
            && turn.State == "ready");
    }

    [Fact]
    public async Task HappyPathProducesFinalTranscriptAndEvidenceMarkers()
    {
        var service = new EndUserChatService();

        var state = await service.SendAsync("Plan a calm family trip to Japan with one quiet day.");
        var serialized = Serialize(state);

        Assert.Equal("applied", state.FinalState);
        Assert.Null(state.ErrorCode);
        Assert.Equal(ModelRoleStatusEvaluator.OutcomeReady, state.RoleStatusOutcome);
        Assert.Equal("deterministic-offline", state.RoleStatusActiveRole);
        Assert.Equal("deterministic-offline", state.SelectedModelRole);
        Assert.Equal("deterministic_default", state.LivePreflightState);
        Assert.Equal("not_requested", state.LiveProposalState);
        Assert.Equal("not_run", state.HarnessValidationState);
        Assert.Equal("deterministic_fallback", state.LatestTurnSource);
        Assert.Equal("not_attempted", state.ProviderRequestState);
        Assert.NotNull(state.FormCard);
        Assert.NotNull(state.ChoiceSet);
        Assert.NotNull(state.ApprovalPlate);
        Assert.Contains(state.ChoiceSet.Candidates, candidate => candidate.CandidateId == "candidate-japan-classic-highlights"
            && candidate.Mood == "reflective-culture"
            && candidate.Media.AssetId == "reflective_culture"
            && candidate.Media.State == "ready");
        Assert.All(state.ChoiceSet.Candidates, candidate =>
        {
            Assert.StartsWith("/media/japan-card-pack/", candidate.Media.Path, StringComparison.Ordinal);
            Assert.Equal("generated_local", candidate.Media.SourceClass);
            Assert.Equal("project-generated", candidate.Media.License);
        });
        Assert.Contains(state.PlanTrail, item => item.TrailId == "trail-mission-facts"
            && item.State == "accepted"
            && item.Media?.AssetId == "calm_morning");
        Assert.Contains(state.PlanningTimeline, item => item.TimelineId == "timeline-day-1-mission"
            && item.Mode == "day"
            && item.DayId == "day-japan-01"
            && item.SlotId == "slot-morning"
            && item.OriginTurnId == "turn-03"
            && item.Media?.AssetId == "calm_morning");
        Assert.Contains(state.PlanningTimeline, item => item.TimelineId == "timeline-task-itinerary"
            && item.Mode == "task"
            && item.TaskId == "task-itinerary"
            && item.EvidenceId == "evidence-chat-candidate");
        Assert.Contains(state.Turns, turn => turn.TurnId == "turn-user-1"
            && turn.Role == "user"
            && turn.Kind == "prompt"
            && turn.OutcomeCode == "prompt_received");
        Assert.Contains(state.Turns, turn => turn.TurnId == "turn-provider-role-status"
            && turn.Role == "provider"
            && turn.Kind == "role-status"
            && turn.OutcomeCode == ModelRoleStatusEvaluator.OutcomeReady);
        Assert.Contains(state.Turns, turn => turn.TurnId == "turn-03"
            && turn.Role == "harness"
            && turn.Kind == "harness"
            && turn.OutcomeCode == "mission_intake_applied");
        Assert.Contains(state.Turns, turn => turn.TurnId == "turn-assistant-final"
            && turn.Kind == "final"
            && turn.State == "applied"
            && turn.OutcomeCode == GoldenTurnTraceRunner.TraceCompleteCode);
        Assert.Contains(state.Turns, turn => turn.Kind == "evidence"
            && turn.OutcomeCode == "complete");
        AssertChatRawTextAbsent(serialized);
    }

    [Fact]
    public async Task LiveRoleWithoutExplicitConfigRendersSanitizedGuardAndDeterministicFallback()
    {
        var service = new EndUserChatService(
            new GoldenTurnTraceRunner(),
            new ModelRoleStatusEvaluator(new Pch.Providers.Mock.MockModelRoleStatusSource()),
            new EndUserLiveModelTurnService(() => new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["PCH_LIVE_MODEL_ENABLED"] = "false",
                ["PCH_LIVE_MODEL_KEY_AVAILABLE"] = "false",
                ["PCH_LIVE_MODEL_PROVIDER"] = "openrouter",
                ["PCH_LIVE_IN_HARNESS_MODEL"] = "qwen/qwen3-14b"
            }));

        var state = await service.SendAsync(
            "RAW_USER_PROMPT_SHOULD_NOT_LEAK Plan Japan with live mode.",
            EndUserModelRoleSelection.InHarnessActionGenerator);
        var serialized = Serialize(state);

        Assert.Equal("in-harness-action-generator", state.SelectedModelRole);
        Assert.Equal("openrouter", state.SelectedProvider);
        Assert.Equal("blocked_by_guard", state.LivePreflightState);
        Assert.Equal("not_run", state.LiveProposalState);
        Assert.Equal("not_run", state.HarnessValidationState);
        Assert.Equal("deterministic_fallback", state.LatestTurnSource);
        Assert.Equal("not_attempted", state.ProviderRequestState);
        Assert.Equal("live_preflight_disabled", state.ProviderOutcome);
        Assert.Equal("live_guard_blocked", state.ProviderHealth);
        Assert.Equal("PCH_UI_LIVE_MODEL_GUARDED", state.ErrorCode);
        Assert.Equal("live_preflight_disabled", state.BlockedReason);
        Assert.Equal("live_preflight_disabled", state.LastProviderFailureCode);
        Assert.Equal("notice-live-model-guard", state.ProviderFailure?.NoticeId);
        Assert.Contains(state.Turns, turn => turn.TurnId == "turn-live-model-run"
            && turn.Kind == "live-model"
            && turn.State == "blocked"
            && turn.OutcomeCode == "live_preflight_disabled"
            && turn.ErrorCode == "PCH_UI_LIVE_MODEL_GUARDED");
        Assert.Contains(state.Turns, turn => turn.TurnId == "turn-assistant-final"
            && turn.State == "applied"
            && turn.OutcomeCode == GoldenTurnTraceRunner.TraceCompleteCode);
        AssertChatRawTextAbsent(serialized);
    }

    [Fact]
    public async Task LiveRoleWithConfiguredPreflightShowsProviderUnavailableThroughCanonicalProposalRunner()
    {
        var completion = new PreflightCompletionClient();
        var credit = new PreflightCreditClient();
        var service = new EndUserChatService(
            new GoldenTurnTraceRunner(),
            new ModelRoleStatusEvaluator(new Pch.Providers.Mock.MockModelRoleStatusSource()),
            new EndUserLiveModelTurnService(
                () => new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["PCH_LIVE_MODEL_ENABLED"] = "true",
                    ["PCH_LIVE_MODEL_KEY_AVAILABLE"] = "true",
                    ["PCH_LIVE_MODEL_PROVIDER"] = "openrouter",
                    ["PCH_LIVE_IN_HARNESS_MODEL"] = "qwen/qwen3-14b"
                },
                new LivePreflightEvaluator(new LivePreflightRunner(completion, credit))));

        var state = await service.SendAsync(
            "RAW_USER_PROMPT_SHOULD_NOT_LEAK Plan Japan with live preflight.",
            EndUserModelRoleSelection.InHarnessActionGenerator);
        var serialized = Serialize(state);

        Assert.Equal("in-harness-action-generator", state.SelectedModelRole);
        Assert.Equal("openrouter", state.SelectedProvider);
        Assert.Equal("preflight_passed", state.LivePreflightState);
        Assert.Equal("live_model_proposal_blocked", state.LiveProposalState);
        Assert.Equal("not_run", state.HarnessValidationState);
        Assert.Equal("live_model_proposal_blocked", state.LatestTurnSource);
        Assert.Equal("attempted", state.ProviderRequestState);
        Assert.Equal("live_mission_proposal_provider_unavailable", state.ProviderOutcome);
        Assert.Equal("credit_guard_checked", state.CreditState);
        Assert.Equal("provider_error", state.LastProviderFailureCode);
        Assert.DoesNotContain("RAW_USER_PROMPT_SHOULD_NOT_LEAK", completion.LastUserMessage, StringComparison.Ordinal);
        Assert.Contains(state.Turns, turn => turn.TurnId == "turn-live-model-run"
            && turn.Kind == "live-model"
            && turn.State == "blocked"
            && turn.OutcomeCode == "live_mission_proposal_provider_unavailable");
        Assert.Contains(state.Turns, turn => turn.TurnId == "turn-assistant-final"
            && turn.State == "applied"
            && turn.OutcomeCode == GoldenTurnTraceRunner.TraceCompleteCode);
        AssertChatRawTextAbsent(serialized);
    }

    [Fact]
    public async Task LiveProposalAcceptedRunsThroughHarnessConductorMarkers()
    {
        var service = LiveProposalService(new EndUserLiveMissionProposalGateway(
            LiveEnvironment,
            new LiveMissionProposalRunner(
                new ProposalCompletionClient(CreateProposalContent()),
                new PreflightCreditClient())));

        var state = await service.SendAsync(
            "RAW_USER_PROMPT_SHOULD_NOT_LEAK Plan Japan with live proposal.",
            EndUserModelRoleSelection.InHarnessActionGenerator);
        var serialized = Serialize(state);

        Assert.Equal("preflight_passed", state.LivePreflightState);
        Assert.Equal("live_model_proposal_accepted", state.LiveProposalState);
        Assert.Equal("accepted", state.HarnessValidationState);
        Assert.Equal("live_model_proposal_accepted", state.LatestTurnSource);
        Assert.Equal("live_model_proposal_accepted", state.ProviderOutcome);
        Assert.Equal("harness_conductor_accepted", state.ProviderHealth);
        Assert.Null(state.ErrorCode);
        Assert.Contains(state.Turns, turn => turn.TurnId == "turn-live-model-run"
            && turn.State == "applied"
            && turn.OutcomeCode == "live_model_proposal_accepted");
        AssertChatRawTextAbsent(serialized);
    }

    [Fact]
    public async Task LiveProposalAcceptedButHarnessValidationBlockedUsesSanitizedMarkers()
    {
        var service = LiveProposalService(new EndUserLiveMissionProposalGateway(
            LiveEnvironment,
            new LiveMissionProposalRunner(
                new ProposalCompletionClient(CreateProposalContent(fieldPath: "/mission/freeform_secret_note")),
                new PreflightCreditClient())));

        var state = await service.SendAsync(
            "RAW_USER_PROMPT_SHOULD_NOT_LEAK Plan Japan with invalid live proposal.",
            EndUserModelRoleSelection.InHarnessActionGenerator);
        var serialized = Serialize(state);

        Assert.Equal("preflight_passed", state.LivePreflightState);
        Assert.Equal("live_model_proposal_accepted", state.LiveProposalState);
        Assert.Equal("harness_validation_blocked", state.HarnessValidationState);
        Assert.Equal("harness_validation_blocked", state.LatestTurnSource);
        Assert.Equal("live_model_proposal_accepted", state.ProviderOutcome);
        Assert.Equal("harness_conductor_mission_proposal_blocked", state.ProviderHealth);
        Assert.Equal("PCH_UI_LIVE_PROPOSAL_BLOCKED", state.ErrorCode);
        Assert.Equal("mission_proposal_blocked", state.BlockedReason);
        Assert.Contains(state.Turns, turn => turn.TurnId == "turn-live-model-run"
            && turn.State == "blocked"
            && turn.ErrorCode == "PCH_UI_LIVE_PROPOSAL_BLOCKED");
        AssertChatRawTextAbsent(serialized);
    }

    [Fact]
    public async Task LiveProposalPacketMismatchStopsAsStaleSessionWithoutHarnessMutation()
    {
        var service = LiveProposalService(new EndUserLiveMissionProposalGateway(
            LiveEnvironment,
            new LiveMissionProposalRunner(
                new ProposalCompletionClient(CreateProposalContent(packetId: "packet-stale-live-proposal")),
                new PreflightCreditClient())));

        var state = await service.SendAsync(
            "RAW_USER_PROMPT_SHOULD_NOT_LEAK Plan Japan with stale packet.",
            EndUserModelRoleSelection.InHarnessActionGenerator);
        var serialized = Serialize(state);

        Assert.Equal("preflight_passed", state.LivePreflightState);
        Assert.Equal("stale_packet_or_session", state.LiveProposalState);
        Assert.Equal("not_run", state.HarnessValidationState);
        Assert.Equal("live_model_proposal_blocked", state.LatestTurnSource);
        Assert.Equal("live_mission_proposal_packet_mismatch", state.ProviderOutcome);
        Assert.Equal("live_mission_proposal_packet_mismatch", state.LastProviderFailureCode);
        Assert.Contains(state.Turns, turn => turn.TurnId == "turn-live-model-run"
            && turn.State == "blocked"
            && turn.OutcomeCode == "live_mission_proposal_packet_mismatch");
        AssertChatRawTextAbsent(serialized);
    }

    [Fact]
    public async Task FormChoiceApprovalAndTrailInteractionsMutateTypedState()
    {
        var service = new EndUserChatService();
        var sent = await service.SendAsync("Plan a calm family trip to Japan with one quiet day.");

        var formSubmitted = service.SubmitForm(sent);
        var selected = service.SelectCandidate(formSubmitted, "candidate-japan-classic-highlights");
        var blocked = service.RequestApproval(selected);

        Assert.Equal("accepted", formSubmitted.FormCard?.State);
        Assert.Equal("candidate_selected", selected.FinalState);
        Assert.Equal("candidate-japan-classic-highlights", selected.ChoiceSet?.SelectedCandidateId);
        Assert.Contains(selected.Turns, turn => turn.TurnId == "turn-choice-selected"
            && turn.CandidateId == "candidate-japan-classic-highlights"
            && turn.CandidateCategory == "trip-style");
        Assert.Contains(selected.PlanTrail, item => item.TrailId == "trail-selected-option"
            && item.CandidateId == "candidate-japan-classic-highlights"
            && item.OutcomeCode == "choice_candidate_selected"
            && item.Media?.AssetId == "reflective_culture");
        Assert.Contains(selected.PlanningTimeline, item => item.TimelineId == "timeline-selected-option"
            && item.Mode == "day"
            && item.CandidateId == "candidate-japan-classic-highlights"
            && item.OriginTurnId == "turn-choice-selected"
            && item.Media?.AssetId == "reflective_culture");
        Assert.Equal("blocked", blocked.FinalState);
        Assert.Equal("blocked_missing_approval", blocked.ApprovalState);
        Assert.Equal("approval_required_preview", blocked.ApprovalPlate?.BlockedReason);
        Assert.Contains(blocked.PlanTrail, item => item.TrailId == "trail-approval-blocked"
            && item.State == "blocked");
        Assert.Contains(blocked.PlanningTimeline, item => item.TimelineId == "timeline-approval-blocked"
            && item.Mode == "task"
            && item.TaskId == "task-approval"
            && item.OriginTurnId == "turn-approval-blocked"
            && item.OutcomeCode == "approval_required_preview");
    }

    [Fact]
    public async Task DeferCandidatePreservesCandidateIdAndEvidenceTrail()
    {
        var service = new EndUserChatService();
        var sent = await service.SendAsync("Plan a calm family trip to Japan with one quiet day.");

        var deferred = service.DeferCandidate(sent, "candidate-japan-scenic-explorer");

        Assert.Equal("candidate_deferred", deferred.FinalState);
        Assert.Equal("candidate-japan-scenic-explorer", deferred.ChoiceSet?.DeferredCandidateId);
        Assert.Contains(deferred.PlanTrail, item => item.TrailId == "trail-deferred-option"
            && item.CandidateId == "candidate-japan-scenic-explorer"
            && item.OutcomeCode == "choice_candidate_deferred"
            && item.Media?.AssetId == "soft_nature");
        Assert.Contains(deferred.PlanningTimeline, item => item.TimelineId == "timeline-deferred-option"
            && item.CandidateId == "candidate-japan-scenic-explorer"
            && item.OriginTurnId == "turn-choice-deferred"
            && item.Media?.AssetId == "soft_nature");
    }

    [Fact]
    public async Task MissingCandidateMediaFallsBackToCommittedPlaceholder()
    {
        var service = new EndUserChatService();
        var sent = await service.SendAsync("Plan a calm family trip to Japan with one quiet day.");

        var fallbackCandidate = Assert.Single(sent.ChoiceSet!.Candidates, candidate => candidate.CandidateId == "candidate-japan-transit-rhythm");

        Assert.Equal("mood_placeholder", fallbackCandidate.Media.AssetId);
        Assert.Equal("fallback", fallbackCandidate.Media.State);
        Assert.Equal("/media/japan-card-pack/mood-placeholder.svg", fallbackCandidate.Media.Path);
        AssertChatRawTextAbsent(Serialize(sent));
    }

    [Fact]
    public void JapanMediaManifestCoversRequiredMoodPack()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(FindManifestPath()));
        var assets = manifest.RootElement.GetProperty("assets").EnumerateArray().ToArray();
        var assetIds = assets.Select(asset => asset.GetProperty("assetId").GetString()).ToHashSet(StringComparer.Ordinal);

        foreach (var required in new[]
        {
            "cultural_immersive",
            "scenic_relaxed",
            "lively_food",
            "calm_morning",
            "reflective_culture",
            "soft_nature",
            "restorative_downtime",
            "logistics_transit",
            "mood_placeholder"
        })
        {
            Assert.Contains(required, assetIds);
        }

        Assert.All(assets, asset =>
        {
            Assert.Equal("generated_local", asset.GetProperty("sourceClass").GetString());
            Assert.Equal("project-generated", asset.GetProperty("license").GetString());
            Assert.StartsWith("/media/japan-card-pack/", asset.GetProperty("path").GetString(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task BlockedSafetyPromptShowsBlockedStateWithoutLiveHoldOrBookingImplication()
    {
        var service = new EndUserChatService();

        var state = await service.SendAsync("Please test the approval safety block before any booking.");
        var serialized = Serialize(state);

        Assert.Equal("blocked", state.FinalState);
        Assert.Equal(GoldenTurnTraceRunner.TraceBlockedCode, state.ErrorCode);
        Assert.Contains(state.Turns, turn => turn.TurnId == "turn-06"
            && turn.Kind == "blocked"
            && turn.State == "blocked"
            && turn.OutcomeCode == "approval_required_preview");
        Assert.Contains(state.Turns, turn => turn.TurnId == "turn-assistant-final"
            && turn.Kind == "blocked"
            && turn.State == "blocked"
            && turn.ErrorCode == GoldenTurnTraceRunner.TraceBlockedCode);
        Assert.DoesNotContain("real hold", serialized, StringComparison.OrdinalIgnoreCase);
        AssertChatRawTextAbsent(serialized);
    }

    [Fact]
    public async Task PendingPromptKeepsConfirmationReadyTranscript()
    {
        var service = new EndUserChatService();

        var state = await service.SendAsync("Maybe Japan if the dates and destination can be confirmed.");

        Assert.Equal("pending", state.FinalState);
        Assert.Equal("end_user_chat_pending_confirmation", state.ErrorCode);
        Assert.Contains(state.Turns, turn => turn.TurnId == "turn-03"
            && turn.State == "pending"
            && turn.OutcomeCode == "mission_intake_applied");
        Assert.Contains(state.Turns, turn => turn.TurnId == "turn-assistant-final"
            && turn.State == "pending"
            && turn.OutcomeCode == "end_user_chat_pending_confirmation");
    }

    [Fact]
    public async Task RawSentinelPromptIsNotEchoedIntoTranscriptSerialization()
    {
        var service = new EndUserChatService();

        var state = await service.SendAsync("RAW_USER_PROMPT_SHOULD_NOT_LEAK RAW_PROVIDER_PAYLOAD_SHOULD_NOT_PERSIST SECRET_SENTINEL");
        var serialized = Serialize(state);

        Assert.Equal("applied", state.FinalState);
        Assert.Contains(state.Turns, turn => turn.TurnId == "turn-user-1"
            && turn.Text.Contains("characters", StringComparison.Ordinal));
        AssertChatRawTextAbsent(serialized);
    }

    private static string Serialize(EndUserChatState state) =>
        JsonSerializer.Serialize(state);

    private static EndUserChatService LiveProposalService(IEndUserLiveProposalGateway gateway)
    {
        var completion = new PreflightCompletionClient();
        var credit = new PreflightCreditClient();
        return new EndUserChatService(
            new GoldenTurnTraceRunner(),
            new ModelRoleStatusEvaluator(new Pch.Providers.Mock.MockModelRoleStatusSource()),
            new EndUserLiveModelTurnService(
                () => new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["PCH_LIVE_MODEL_ENABLED"] = "true",
                    ["PCH_LIVE_MODEL_KEY_AVAILABLE"] = "true",
                    ["PCH_LIVE_MODEL_PROVIDER"] = "openrouter",
                    ["PCH_LIVE_IN_HARNESS_MODEL"] = "qwen/qwen3-14b"
                },
                new LivePreflightEvaluator(new LivePreflightRunner(completion, credit)),
                proposalGateway: gateway));
    }

    private static IReadOnlyDictionary<string, string?> LiveEnvironment() =>
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["PCH_LIVE_MODEL_ENABLED"] = "true",
            ["PCH_LIVE_MODEL_KEY_AVAILABLE"] = "true",
            ["PCH_LIVE_MODEL_PROVIDER"] = "openrouter",
            ["PCH_LIVE_IN_HARNESS_MODEL"] = "qwen/qwen3-14b"
        };

    private static string CreateProposalContent(
        string packetId = "packet-end-user-live-proposal",
        string sessionId = "session-end-user-live-proposal",
        string role = "in_harness_action_generator",
        string fieldPath = "/mission/purpose") =>
        JsonSerializer.Serialize(new
        {
            packetId,
            sessionId,
            role,
            outputKind = "mission_proposal",
            missionKind = "vacation",
            fields = new[]
            {
                new
                {
                    fieldPath,
                    value = "vacation",
                    authoritySource = "user_stated",
                    evidenceIds = new[] { "evidence-live-purpose" }
                },
                new
                {
                    fieldPath = "/mission/destination_country",
                    value = "Japan",
                    authoritySource = "user_stated",
                    evidenceIds = new[] { "evidence-live-destination" }
                },
                new
                {
                    fieldPath = "/mission/start_date",
                    value = "2026-10-05",
                    authoritySource = "user_stated",
                    evidenceIds = new[] { "evidence-live-dates" }
                },
                new
                {
                    fieldPath = "/mission/end_date",
                    value = "2026-10-12",
                    authoritySource = "user_stated",
                    evidenceIds = new[] { "evidence-live-dates" }
                }
            },
            commitments = Array.Empty<object>(),
            pendingConfirmations = Array.Empty<object>()
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static string FindManifestPath()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Pch.UI", "wwwroot", "media", "japan-card-pack", "manifest.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not find Japan card media manifest.");
    }

    private static void AssertChatRawTextAbsent(string serialized)
    {
        Assert.DoesNotContain("Plan a calm family trip", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("Please test the approval safety block", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("Maybe Japan", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_USER_PROMPT_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_PROVIDER_PAYLOAD_SHOULD_NOT_PERSIST", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_END_TO_END_PROMPT_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("APPROVAL_TOKEN_SHOULD_NOT_PERSIST", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_HOLD_REFERENCE_SHOULD_NOT_PERSIST", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_HOLD_REFERENCE_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("CANDIDATE_DISPLAY_VALUE_SHOULD_NOT_PERSIST", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_CANDIDATE_DISPLAY_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET_SENTINEL", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("live booking", serialized, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class PreflightCompletionClient : IModelCompletionClient
    {
        public string LastUserMessage { get; private set; } = string.Empty;

        public Task<ModelCompletionResponse> CompleteAsync(
            ModelCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            LastUserMessage = request.Messages.Last(message => message.Role == ModelMessageRole.User).Content;
            var content = JsonSerializer.Serialize(new
            {
                packetId = "packet-end-user-live-preflight",
                roles = new[]
                {
                    new
                    {
                        role = "in_harness_action_generator",
                        probeId = "probe-in-harness-action-generator",
                        modelId = "qwen/qwen3-14b",
                        outputKind = "structured_output_ready"
                    }
                }
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

            return Task.FromResult(new ModelCompletionResponse(
                "qwen/qwen3-14b",
                content,
                "openrouter",
                RequestId: "request-preflight-safe"));
        }
    }

    private sealed class PreflightCreditClient : IProviderCreditClient
    {
        public Task<ProviderCreditStatus> GetCreditStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProviderCreditStatus(1m, 0m, 1m, IsExhausted: false));
    }

    private sealed class ProposalCompletionClient(string content) : IModelCompletionClient
    {
        public Task<ModelCompletionResponse> CompleteAsync(
            ModelCompletionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ModelCompletionResponse(
                "qwen/qwen3-14b",
                content,
                "openrouter",
                RequestId: "request-live-proposal-safe"));
    }
}
