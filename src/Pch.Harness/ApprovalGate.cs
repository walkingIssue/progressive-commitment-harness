using Pch.Core;

namespace Pch.Harness;

public sealed class ApprovalGate
{
    public ApprovalGateResult Evaluate(HarnessAction action)
    {
        if (!RequiresApproval(action))
        {
            return ApprovalGateResult.Allowed();
        }

        if (action is RequestApprovalAction { Approval.ApprovalToken: { Length: > 0 } token })
        {
            return ApprovalGateResult.Allowed(token);
        }

        return ApprovalGateResult.Refused("Irreversible, spend, or booking actions require an approval token.");
    }

    public static bool RequiresApproval(HarnessAction action)
    {
        return action switch
        {
            RequestApprovalAction request => request.Approval.RiskFlags.Any(IsGatedRisk)
                || request.Approval.SpendAmount is > 0,
            StatePatchAction patch => patch.Patch.Path.StartsWith("/booking", StringComparison.OrdinalIgnoreCase)
                || patch.Patch.Path.StartsWith("/spend", StringComparison.OrdinalIgnoreCase),
            HandoffAction handoff => handoff.Target.Contains("booking", StringComparison.OrdinalIgnoreCase)
                || handoff.Reason.Contains("irreversible", StringComparison.OrdinalIgnoreCase)
                || handoff.Reason.Contains("spend", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool IsGatedRisk(string risk)
    {
        return risk.Equals("irreversible", StringComparison.OrdinalIgnoreCase)
            || risk.Equals("spend", StringComparison.OrdinalIgnoreCase)
            || risk.Equals("booking", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record ApprovalGateResult(bool IsAllowed, string? ApprovalToken, string? RefusalReason)
{
    public static ApprovalGateResult Allowed(string? approvalToken = null) => new(true, approvalToken, null);

    public static ApprovalGateResult Refused(string reason) => new(false, null, reason);
}
