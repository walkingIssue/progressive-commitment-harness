namespace Pch.Harness;

public sealed class RuntimeActionApplication
{
    private readonly ExternalActionDecoder _decoder;
    private readonly HarnessActionIntake _intake;
    private readonly ProjectionService _projectionService;

    public RuntimeActionApplication(
        ExternalActionDecoder? decoder = null,
        HarnessActionIntake? intake = null,
        ProjectionService? projectionService = null)
    {
        _decoder = decoder ?? new ExternalActionDecoder();
        _intake = intake ?? new HarnessActionIntake();
        _projectionService = projectionService ?? new ProjectionService();
    }

    public RuntimeActionApplicationResult Apply(TripSession session, ExternalActionProposal proposal)
    {
        var startingPacket = _projectionService.Project(session, session.Stage);
        var decode = _decoder.Decode(proposal);
        if (!decode.IsDecoded || decode.Action is null)
        {
            return RuntimeActionApplicationResult.FromDecodeFailure(
                session.Stage.ToString(),
                startingPacket.PacketId,
                decode.Code,
                decode.Summary);
        }

        var intake = _intake.Accept(session, decode.Action);
        return RuntimeActionApplicationResult.FromIntake(
            intake,
            decode.Code,
            decode.Summary);
    }

    public RuntimeActionApplicationResult ApplyJson(
        TripSession session,
        string actionId,
        string kind,
        string jsonArguments)
    {
        var startingPacket = _projectionService.Project(session, session.Stage);
        var decode = _decoder.DecodeJson(actionId, kind, jsonArguments);
        if (!decode.IsDecoded || decode.Action is null)
        {
            return RuntimeActionApplicationResult.FromDecodeFailure(
                session.Stage.ToString(),
                startingPacket.PacketId,
                decode.Code,
                decode.Summary);
        }

        var intake = _intake.Accept(session, decode.Action);
        return RuntimeActionApplicationResult.FromIntake(
            intake,
            decode.Code,
            decode.Summary);
    }
}

public sealed record RuntimeActionApplicationResult(
    bool IsAccepted,
    bool IsBlocked,
    string DecodeCode,
    string IntakeCode,
    string Stage,
    string PacketId,
    string Summary,
    IReadOnlyList<SessionTraceEvent> Trace)
{
    public static RuntimeActionApplicationResult FromDecodeFailure(
        string stage,
        string packetId,
        string decodeCode,
        string summary)
    {
        return new(
            IsAccepted: false,
            IsBlocked: true,
            DecodeCode: decodeCode,
            IntakeCode: "not_run",
            Stage: stage,
            PacketId: packetId,
            Summary: summary,
            Trace:
            [
                new(
                    $"trace-decode-{decodeCode}",
                    stage,
                    "external_action",
                    decodeCode,
                    summary)
            ]);
    }

    public static RuntimeActionApplicationResult FromIntake(
        SessionTurnResult intake,
        string decodeCode,
        string decodeSummary)
    {
        var intakeCode = intake.IsBlocked
            ? intake.Trace.FirstOrDefault()?.Outcome ?? "intake_blocked"
            : "accepted";
        var summary = intake.IsBlocked
            ? intake.BlockedReason ?? "Action proposal was blocked."
            : decodeSummary;

        return new(
            IsAccepted: !intake.IsBlocked,
            IsBlocked: intake.IsBlocked,
            DecodeCode: decodeCode,
            IntakeCode: intakeCode,
            Stage: intake.Stage.ToString(),
            PacketId: intake.Packet.PacketId,
            Summary: summary,
            Trace: intake.Trace);
    }
}
