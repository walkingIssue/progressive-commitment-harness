namespace Pch.Core;

public sealed record StateAuthorityPolicy(
    IReadOnlySet<AuthoritySource> AutoApplySources,
    IReadOnlySet<string> ProtectedPaths)
{
    public static StateAuthorityPolicy Default { get; } = new(
        new HashSet<AuthoritySource>
        {
            AuthoritySource.User,
            AuthoritySource.TrustedTool,
            AuthoritySource.HarnessDefault
        },
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "/mission/purpose",
            "/mission/dates",
            "/commitments/fixed",
            "/spend",
            "/booking"
        });

    public bool CanAutoApply(StatePatchProposal proposal)
    {
        return AutoApplySources.Contains(proposal.Source)
            && !ProtectedPaths.Any(path => proposal.Path.StartsWith(path, StringComparison.OrdinalIgnoreCase));
    }

    public bool RequiresConfirmation(StatePatchProposal proposal) => !CanAutoApply(proposal);
}
