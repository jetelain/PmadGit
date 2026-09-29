namespace Pmad.Git.CliEmulator.Approval;

/// <summary>
/// Context passed to <see cref="IUserApproval.ApproveHistoryRewriteAsync"/> describing
/// a history-rewriting operation (amend of pushed commit, force-push).
/// </summary>
public sealed class HistoryRewriteContext
{
    /// <summary>Human-readable name of the operation (e.g. "commit --amend", "push --force").</summary>
    public string Operation { get; init; } = string.Empty;

    /// <summary>Name of the branch affected.</summary>
    public string BranchName { get; init; } = string.Empty;

    /// <summary>Commits that will be rewritten or replaced.</summary>
    public IReadOnlyList<GitCommitSummary> AffectedCommits { get; init; } = [];

    /// <summary><see langword="true"/> when the rewrite also involves a remote push (force-push).</summary>
    public bool InvolvesRemotePush { get; init; }
}
