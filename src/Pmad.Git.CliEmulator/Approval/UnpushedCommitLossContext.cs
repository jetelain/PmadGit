namespace Pmad.Git.CliEmulator.Approval;

/// <summary>
/// Context passed to <see cref="IUserApproval.ApproveUnpushedCommitLossAsync"/> describing
/// local commits that are not reachable from any remote ref and will be lost.
/// </summary>
public sealed class UnpushedCommitLossContext : IApprovalContext
{
    /// <summary>Human-readable name of the operation (e.g. "branch -D", "reset --mixed").</summary>
    public string Operation { get; init; } = string.Empty;

    /// <summary>Name of the local branch affected.</summary>
    public string BranchName { get; init; } = string.Empty;

    /// <summary>Commits that will be orphaned (not reachable from any remote tracking ref).</summary>
    public IReadOnlyList<GitCommitSummary> CommitsToLose { get; init; } = [];
}
