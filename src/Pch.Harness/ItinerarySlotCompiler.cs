using System.Text.Json.Serialization;
using Pch.Core;

namespace Pch.Harness;

public sealed class ItinerarySlotCompiler
{
    private const int MaxDays = 14;
    private const int MaxPendingConfirmations = 8;
    private const int MaxSlotsPerDay = 12;
    private const int MaxConflicts = 8;

    public ItineraryCompilationResult Compile(TripSession session, ItineraryCompilationRequest request)
    {
        var validation = Validate(session, request);
        if (!validation.IsAccepted)
        {
            return Blocked(validation.Code, validation.Summary, [], []);
        }

        var startDate = request.StartDate ?? session.Mission.StartDate;
        var endDate = request.EndDate ?? session.Mission.EndDate;
        var commitments = session.Mission.Commitments
            .Where(commitment => commitment.StartsAt is not null && commitment.EndsAt is not null)
            .OrderBy(commitment => commitment.StartsAt)
            .ThenBy(commitment => commitment.CommitmentId, StringComparer.Ordinal)
            .ToArray();
        var conflicts = DetectConflicts(commitments).Take(MaxConflicts).ToArray();
        if (conflicts.Length > 0)
        {
            var result = Blocked(
                "fixed_commitment_conflict",
                "Itinerary slot compilation found conflicting fixed commitments.",
                [],
                conflicts);
            session.ReplaceItineraryCompilation(result);
            return result;
        }

        var days = Enumerable.Range(0, endDate.DayNumber - startDate.DayNumber + 1)
            .Select(index => CompileDay(
                session,
                startDate.AddDays(index),
                commitments,
                request.CurrentMemory?.PendingConfirmations ?? []))
            .ToArray();

        var compiled = new ItineraryCompilationResult(
            IsCompiled: true,
            Code: "itinerary_slots_compiled",
            Summary: "Itinerary slots compiled.",
            Days: days,
            Conflicts: [],
            SlotCount: days.Sum(day => day.Slots.Count),
            ConflictCount: 0);
        session.ReplaceItineraryCompilation(compiled);
        return compiled;
    }

    private static ItineraryCompilerValidation Validate(TripSession session, ItineraryCompilationRequest request)
    {
        if (request is null)
        {
            return Reject("invalid_request", "Itinerary slot compilation request failed validation.");
        }

        if (string.IsNullOrWhiteSpace(request.SessionId)
            || !string.Equals(request.SessionId, session.SessionId, StringComparison.Ordinal))
        {
            return Reject("invalid_session", "Itinerary slot compilation request failed validation.");
        }

        var startDate = request.StartDate ?? session.Mission.StartDate;
        var endDate = request.EndDate ?? session.Mission.EndDate;
        if (startDate == default || endDate == default)
        {
            return Reject("missing_date_window", "Itinerary slot compilation requires a date window.");
        }

        var dayCount = endDate.DayNumber - startDate.DayNumber + 1;
        if (dayCount <= 0)
        {
            return Reject("invalid_date_window", "Itinerary slot compilation requires a valid date window.");
        }

        if (dayCount > MaxDays)
        {
            return Reject("too_many_days", "Itinerary slot compilation exceeded day limits.");
        }

        if (request.CurrentMemory is not null && request.CurrentMemory.PendingConfirmations.Count > MaxPendingConfirmations)
        {
            return Reject("too_many_pending_confirmations", "Itinerary slot compilation exceeded pending confirmation limits.");
        }

        return new(true, "accepted", "Itinerary slot compilation request accepted.");
    }

    private static ItineraryDayPlan CompileDay(
        TripSession session,
        DateOnly date,
        IReadOnlyList<Commitment> commitments,
        IReadOnlyList<MissionPendingConfirmation> pendingConfirmations)
    {
        var slots = new List<ItinerarySlot>
        {
            Slot("sleep", ItinerarySlotKind.Sleep, date, new TimeOnly(0, 0), new TimeOnly(7, 0), "Overnight rest."),
            Slot("transit-start", ItinerarySlotKind.Transit, date, new TimeOnly(8, 0), new TimeOnly(9, 0), "Local transit buffer."),
            Slot("breakfast", ItinerarySlotKind.Meal, date, new TimeOnly(9, 0), new TimeOnly(10, 0), "Breakfast window.")
        };

        slots.AddRange(commitments
            .Where(commitment => DateOnly.FromDateTime(commitment.StartsAt!.Value.DateTime) == date)
            .Select(commitment => new ItinerarySlot(
                SlotId: $"slot-{date:yyyyMMdd}-{commitment.CommitmentId}",
                Kind: ItinerarySlotKind.FixedCommitment,
                Date: date,
                StartsAt: TimeOnly.FromDateTime(commitment.StartsAt!.Value.DateTime),
                EndsAt: TimeOnly.FromDateTime(commitment.EndsAt!.Value.DateTime),
                Summary: "Fixed commitment.",
                CommitmentId: commitment.CommitmentId,
                PendingFieldPath: null)));

        slots.Add(Slot("lunch", ItinerarySlotKind.Meal, date, new TimeOnly(12, 0), new TimeOnly(13, 0), "Lunch window."));
        slots.Add(Slot("activity", ItinerarySlotKind.Activity, date, new TimeOnly(14, 0), new TimeOnly(16, 0), "Candidate activity block."));
        slots.Add(Slot("downtime", ItinerarySlotKind.Downtime, date, new TimeOnly(16, 0), new TimeOnly(17, 0), "Recovery buffer."));
        slots.Add(Slot("dinner", ItinerarySlotKind.Meal, date, new TimeOnly(18, 0), new TimeOnly(19, 0), "Dinner window."));

        slots.AddRange(pendingConfirmations
            .Take(MaxPendingConfirmations)
            .Select((pending, index) => new ItinerarySlot(
                SlotId: $"slot-{date:yyyyMMdd}-unresolved-{index + 1}",
                Kind: ItinerarySlotKind.UnresolvedConfirmation,
                Date: date,
                StartsAt: null,
                EndsAt: null,
                Summary: "Pending confirmation blocks final slot choice.",
                CommitmentId: null,
                PendingFieldPath: pending.FieldPath)));

        return new ItineraryDayPlan(
            DayId: $"day-{date:yyyyMMdd}",
            Date: date,
            Slots: slots
                .OrderBy(slot => slot.StartsAt ?? TimeOnly.MaxValue)
                .ThenBy(slot => slot.SlotId, StringComparer.Ordinal)
                .Take(MaxSlotsPerDay)
                .ToArray());
    }

    private static IReadOnlyList<ItineraryConflict> DetectConflicts(IReadOnlyList<Commitment> commitments)
    {
        var conflicts = new List<ItineraryConflict>();
        for (var index = 0; index < commitments.Count; index++)
        {
            for (var compare = index + 1; compare < commitments.Count; compare++)
            {
                var left = commitments[index];
                var right = commitments[compare];
                if (left.StartsAt!.Value < right.EndsAt!.Value && right.StartsAt!.Value < left.EndsAt!.Value)
                {
                    conflicts.Add(new ItineraryConflict(
                        ConflictId: $"conflict-{left.CommitmentId}-{right.CommitmentId}",
                        Code: "overlapping_fixed_commitments",
                        Summary: "Fixed commitments overlap.",
                        CommitmentIds: [left.CommitmentId, right.CommitmentId]));
                }
            }
        }

        return conflicts;
    }

    private static ItinerarySlot Slot(
        string suffix,
        ItinerarySlotKind kind,
        DateOnly date,
        TimeOnly startsAt,
        TimeOnly endsAt,
        string summary)
    {
        return new(
            SlotId: $"slot-{date:yyyyMMdd}-{suffix}",
            Kind: kind,
            Date: date,
            StartsAt: startsAt,
            EndsAt: endsAt,
            Summary: summary,
            CommitmentId: null,
            PendingFieldPath: null);
    }

    private static ItineraryCompilationResult Blocked(
        string code,
        string summary,
        IReadOnlyList<ItineraryDayPlan> days,
        IReadOnlyList<ItineraryConflict> conflicts)
    {
        return new(
            IsCompiled: false,
            Code: code,
            Summary: summary,
            Days: days,
            Conflicts: conflicts,
            SlotCount: days.Sum(day => day.Slots.Count),
            ConflictCount: conflicts.Count);
    }

    private static ItineraryCompilerValidation Reject(string code, string summary)
    {
        return new(false, code, summary);
    }
}

public sealed record ItineraryCompilationRequest(
    string SessionId,
    DateOnly? StartDate,
    DateOnly? EndDate,
    StructuredMemoryDigest? CurrentMemory,
    IReadOnlyList<string> ScenarioHints);

public sealed record ItineraryCompilationResult(
    bool IsCompiled,
    string Code,
    string Summary,
    IReadOnlyList<ItineraryDayPlan> Days,
    IReadOnlyList<ItineraryConflict> Conflicts,
    int SlotCount,
    int ConflictCount);

public sealed record ItineraryDayPlan(
    string DayId,
    DateOnly Date,
    IReadOnlyList<ItinerarySlot> Slots);

public sealed record ItinerarySlot(
    string SlotId,
    ItinerarySlotKind Kind,
    DateOnly Date,
    TimeOnly? StartsAt,
    TimeOnly? EndsAt,
    string Summary,
    string? CommitmentId,
    string? PendingFieldPath);

[JsonConverter(typeof(JsonStringEnumConverter<ItinerarySlotKind>))]
public enum ItinerarySlotKind
{
    Sleep,
    Meal,
    Transit,
    FixedCommitment,
    Downtime,
    Activity,
    UnresolvedConfirmation
}

public sealed record ItineraryConflict(
    string ConflictId,
    string Code,
    string Summary,
    IReadOnlyList<string> CommitmentIds);

public sealed record ItineraryCompilerValidation(
    bool IsAccepted,
    string Code,
    string Summary);
