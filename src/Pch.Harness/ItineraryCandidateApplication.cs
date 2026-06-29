using Pch.Core;

namespace Pch.Harness;

public sealed class ItineraryCandidateApplication
{
    public ItinerarySlotApplicationResult Apply(TripSession session, ItinerarySlotDecisionRequest request)
    {
        var validation = Validate(session, request);
        if (!validation.IsAccepted || validation.Slot is null)
        {
            return Blocked(validation.Code, validation.Summary, session);
        }

        var decision = new ItinerarySlotDecision(
            DecisionId: $"itinerary-decision-{session.ItineraryDecisions.Count + 1}",
            SlotId: validation.Slot.SlotId,
            Kind: request.Kind,
            SlotKind: validation.Slot.Kind,
            CandidateId: validation.Candidate?.CandidateId,
            CandidateKind: validation.Candidate?.Kind,
            EvidenceIds: validation.Candidate?.EvidenceIds ?? []);

        session.RecordItineraryDecision(decision);
        session.RecordDecision(new DecisionRecord(
            DecisionId: decision.DecisionId,
            Stage: session.Stage.ToString(),
            ActionKind: request.Kind is ItinerarySlotDecisionKind.Selected ? "itinerary_select" : "itinerary_defer",
            Summary: request.Kind is ItinerarySlotDecisionKind.Selected
                ? "Accepted itinerary slot candidate selection."
                : "Accepted itinerary slot defer decision.",
            Source: AuthoritySource.User,
            RecordedAt: request.DecidedAt));

        return new(
            IsAccepted: true,
            IsBlocked: false,
            Code: "itinerary_decision_applied",
            Summary: "Itinerary slot decision applied.",
            Decision: decision,
            EvidenceIds: decision.EvidenceIds,
            Trace:
            [
                new(
                    $"trace-itinerary-{session.ItineraryDecisions.Count}",
                    session.Stage.ToString(),
                    request.Kind is ItinerarySlotDecisionKind.Selected ? "itinerary_select" : "itinerary_defer",
                    "accepted",
                    "Accepted itinerary slot decision.")
            ],
            SelectedCount: Count(session, ItinerarySlotDecisionKind.Selected),
            DeferredCount: Count(session, ItinerarySlotDecisionKind.Deferred));
    }

    private static ItinerarySlotApplicationValidation Validate(TripSession session, ItinerarySlotDecisionRequest request)
    {
        if (request is null)
        {
            return Reject("invalid_request", "Itinerary slot decision failed validation.");
        }

        if (string.IsNullOrWhiteSpace(request.SessionId)
            || !string.Equals(request.SessionId, session.SessionId, StringComparison.Ordinal))
        {
            return Reject("invalid_session", "Itinerary slot decision failed validation.");
        }

        if (string.IsNullOrWhiteSpace(request.SlotId))
        {
            return Reject("unknown_slot", "Itinerary slot decision references an unknown slot.");
        }

        if (session.LastItineraryCompilation is not { IsCompiled: true } compilation)
        {
            return Reject("no_compiled_itinerary", "Itinerary slot decision requires a compiled itinerary.");
        }

        var slot = compilation.Days
            .SelectMany(day => day.Slots)
            .FirstOrDefault(slot => string.Equals(slot.SlotId, request.SlotId, StringComparison.Ordinal));
        if (slot is null)
        {
            return Reject("unknown_slot", "Itinerary slot decision references an unknown slot.");
        }

        if (slot.Kind != request.SlotKind)
        {
            return Reject("category_mismatch", "Itinerary slot decision category did not match the compiled slot.");
        }

        return request.Kind switch
        {
            ItinerarySlotDecisionKind.Selected => ValidateSelection(session, request, slot),
            ItinerarySlotDecisionKind.Deferred => ValidateDefer(request, slot),
            _ => Reject("invalid_decision_kind", "Itinerary slot decision failed validation.")
        };
    }

    private static ItinerarySlotApplicationValidation ValidateSelection(
        TripSession session,
        ItinerarySlotDecisionRequest request,
        ItinerarySlot slot)
    {
        if (string.IsNullOrWhiteSpace(request.CandidateId) || request.CandidateKind is null)
        {
            return Reject("invalid_candidate", "Itinerary slot decision candidate failed validation.");
        }

        var candidate = session.CandidatePools
            .SelectMany(pool => pool.Candidates)
            .FirstOrDefault(candidate => string.Equals(candidate.CandidateId, request.CandidateId, StringComparison.Ordinal));
        if (candidate is null)
        {
            return Reject("unknown_candidate", "Itinerary slot decision references an unknown candidate.");
        }

        if (candidate.Kind != request.CandidateKind)
        {
            return Reject("category_mismatch", "Itinerary slot decision category did not match the candidate.");
        }

        if (!session.HasItineraryCandidateForSlot(slot.SlotId, candidate.CandidateId))
        {
            return Reject("candidate_pool_mismatch", "Itinerary candidate is not associated with the compiled slot.");
        }

        if (!CandidateMatchesSlot(slot.Kind, candidate.Kind))
        {
            return Reject("candidate_slot_mismatch", "Itinerary candidate does not match the compiled slot.");
        }

        return new(true, "accepted", "Accepted.", slot, candidate);
    }

    private static ItinerarySlotApplicationValidation ValidateDefer(
        ItinerarySlotDecisionRequest request,
        ItinerarySlot slot)
    {
        if (!string.IsNullOrWhiteSpace(request.CandidateId) || request.CandidateKind is not null)
        {
            return Reject("invalid_defer", "Itinerary slot defer decision failed validation.");
        }

        return new(true, "accepted", "Accepted.", slot, null);
    }

    private static bool CandidateMatchesSlot(ItinerarySlotKind slotKind, CandidateKind candidateKind)
    {
        return slotKind switch
        {
            ItinerarySlotKind.Activity => candidateKind is CandidateKind.Activity,
            ItinerarySlotKind.Transit => candidateKind is CandidateKind.Transit or CandidateKind.Flight,
            ItinerarySlotKind.Meal => candidateKind is CandidateKind.Restaurant,
            ItinerarySlotKind.Sleep => candidateKind is CandidateKind.Hotel,
            ItinerarySlotKind.Downtime => candidateKind is CandidateKind.Activity or CandidateKind.ScheduleBlock,
            ItinerarySlotKind.FixedCommitment => candidateKind is CandidateKind.ScheduleBlock,
            ItinerarySlotKind.UnresolvedConfirmation => false,
            _ => false
        };
    }

    private static ItinerarySlotApplicationResult Blocked(string code, string summary, TripSession session)
    {
        return new(
            IsAccepted: false,
            IsBlocked: true,
            Code: code,
            Summary: summary,
            Decision: null,
            EvidenceIds: [],
            Trace:
            [
                new(
                    $"trace-itinerary-blocked-{code}",
                    session.Stage.ToString(),
                    "itinerary_decision",
                    code,
                    summary)
            ],
            SelectedCount: Count(session, ItinerarySlotDecisionKind.Selected),
            DeferredCount: Count(session, ItinerarySlotDecisionKind.Deferred));
    }

    private static int Count(TripSession session, ItinerarySlotDecisionKind kind)
    {
        return session.ItineraryDecisions.Count(decision => decision.Kind == kind);
    }

    private static ItinerarySlotApplicationValidation Reject(string code, string summary)
    {
        return new(false, code, summary, null, null);
    }
}

public sealed record ItinerarySlotDecisionRequest(
    string SessionId,
    string SlotId,
    ItinerarySlotDecisionKind Kind,
    ItinerarySlotKind SlotKind,
    string? CandidateId,
    CandidateKind? CandidateKind,
    DateTimeOffset DecidedAt);

public sealed record ItinerarySlotApplicationResult(
    bool IsAccepted,
    bool IsBlocked,
    string Code,
    string Summary,
    ItinerarySlotDecision? Decision,
    IReadOnlyList<string> EvidenceIds,
    IReadOnlyList<SessionTraceEvent> Trace,
    int SelectedCount,
    int DeferredCount);

public sealed record ItinerarySlotDecision(
    string DecisionId,
    string SlotId,
    ItinerarySlotDecisionKind Kind,
    ItinerarySlotKind SlotKind,
    string? CandidateId,
    CandidateKind? CandidateKind,
    IReadOnlyList<string> EvidenceIds);

public enum ItinerarySlotDecisionKind
{
    Selected,
    Deferred
}

public sealed record ItinerarySlotApplicationValidation(
    bool IsAccepted,
    string Code,
    string Summary,
    ItinerarySlot? Slot,
    Candidate? Candidate);
