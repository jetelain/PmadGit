using Pmad.Git.LocalRepositories;

namespace Pmad.Git.Cli;

/// <summary>
/// Defines remote and merge synchronization operations for a Git repository.
/// </summary>
public interface IGitRemoteRepository
{
    /// <summary>
    /// Absolute path to the repository working tree root.
    /// </summary>
    string RootPath { get; }

    /// <summary>
    /// Downloads objects and refs from a remote repository, without integrating them.
    /// </summary>
    /// <param name="remote">Name of the remote to fetch from (defaults to <c>origin</c> when not specified).</param>
    /// <param name="branch">Name of the remote branch to fetch (defaults to all branches when not specified).</param>
    /// <param name="prune">When <c>true</c>, removes remote-tracking references that no longer exist on the remote.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task FetchAsync(string? remote = null, string? branch = null, bool prune = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pulls changes from a remote repository (fetch + merge or rebase).
    /// </summary>
    /// <param name="remote">Name of the remote (defaults to the current branch's remote when not specified).</param>
    /// <param name="branch">Name of the remote branch to pull (defaults to the current branch's upstream when not specified).</param>
    /// <param name="rebase">When <c>true</c>, rebases the current branch on top of the pulled branch instead of merging.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="GitMergeResult"/> describing whether the pull succeeded or stopped because of conflicts.</returns>
    Task<GitMergeResult> PullAsync(string? remote = null, string? branch = null, bool rebase = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pushes changes to a remote repository.
    /// </summary>
    /// <param name="remote">Name of the remote (defaults to the current branch's remote when not specified).</param>
    /// <param name="branch">Name of the branch to push (defaults to the current branch when not specified).</param>
    /// <param name="force">When <c>true</c>, forces the push (using <c>--force-with-lease</c>).</param>
    /// <param name="setUpstream">When <c>true</c>, sets the pushed branch as the upstream of the current local branch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PushAsync(string? remote = null, string? branch = null, bool force = false, bool setUpstream = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Merges the specified branch into the current branch.
    /// </summary>
    /// <param name="branch">Name of the branch to merge into the current branch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="GitMergeResult"/> describing whether the merge succeeded or stopped because of conflicts.</returns>
    Task<GitMergeResult> MergeAsync(string branch, CancellationToken cancellationToken = default);

    /// <summary>
    /// Indicates whether a merge (or pull) is currently in progress.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> IsMergeInProgressAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the relative paths of the files that are currently in conflict.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<string>> GetConflictedFilesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a conflicted file as resolved, by staging its current content.
    /// </summary>
    /// <param name="relativeFilePath">Path of the file, relative to <see cref="RootPath"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ResolveConflictAsync(string relativeFilePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes an in-progress merge after all conflicts have been resolved (via <see cref="ResolveConflictAsync"/>).
    /// </summary>
    /// <param name="commitMessage">Optional commit message to use for the merge commit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ContinueMergeAsync(string? commitMessage = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Aborts an in-progress merge, restoring the repository to the state it had before the merge started.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AbortMergeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns tracking status (ahead/behind counts and upstream branch name) for a local branch.
    /// </summary>
    /// <param name="branch">Name of the local branch to check, or <see langword="null"/> to check the current branch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="GitTrackingStatus"/> describing upstream configuration and ahead/behind commit counts.</returns>
    Task<GitTrackingStatus> GetTrackingStatusAsync(string? branch = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the specified commit has been pushed to a remote branch.
    /// </summary>
    /// <param name="commitHash">The commit hash to check.</param>
    /// <param name="remoteBranch">The specific remote branch (e.g. "origin/main"), or <see langword="null"/> to check any remote branch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> if the commit is reachable from the remote branch (or any remote branch); otherwise, <see langword="false"/>.</returns>
    Task<bool> IsCommitPushedAsync(string commitHash, string? remoteBranch = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the specified commit has been pushed to a remote branch.
    /// </summary>
    /// <param name="commitHash">The commit hash to check.</param>
    /// <param name="remoteBranch">The specific remote branch (e.g. "origin/main"), or <see langword="null"/> to check any remote branch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> if the commit is reachable from the remote branch (or any remote branch); otherwise, <see langword="false"/>.</returns>
    Task<bool> IsCommitPushedAsync(GitHash commitHash, string? remoteBranch = null, CancellationToken cancellationToken = default);
}
