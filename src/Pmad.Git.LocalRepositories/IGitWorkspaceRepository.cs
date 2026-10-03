namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Represents a Git repository with an active working tree and index (.git/index),
/// providing in-process status, staging, committing, amending, resetting, and undo/revert operations.
/// </summary>
public interface IGitWorkspaceRepository : IGitRepository, IDisposable
{
    /// <summary>
    /// Gets the index manager for working tree and staging operations.
    /// </summary>
    new GitIndexManager IndexManager { get; }

    /// <summary>
    /// Inspects the working tree and staging area, returning status for modified,
    /// staged, deleted, untracked, and conflicted files.
    /// </summary>
    /// <param name="includeUntracked">Whether to detect untracked files.</param>
    /// <param name="includeClean">Whether to include unmodified files in the result.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>A <see cref="GitStatusResult"/> representing repository state.</returns>
    Task<GitStatusResult> GetStatusAsync(
        bool includeUntracked = true,
        bool includeClean = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Quick check returning true if the working tree has no staged, unstaged, or conflicted changes.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>True if the working tree is clean; false otherwise.</returns>
    Task<bool> IsWorkingTreeCleanAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages a single file into the index (.git/index).
    /// </summary>
    /// <param name="relativePath">Repository-relative file path.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    Task StageAsync(string relativePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages multiple files into the index in a single batch.
    /// </summary>
    /// <param name="relativePaths">Collection of repository-relative file paths.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    Task StageAsync(IEnumerable<string> relativePaths, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages all modified, added, and deleted files in the working tree.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    Task StageAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Unstages a single file by reverting its index entry to match HEAD.
    /// </summary>
    /// <param name="relativePath">Repository-relative file path.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    Task UnstageAsync(string relativePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unstages multiple files by reverting their index entries to match HEAD.
    /// </summary>
    /// <param name="relativePaths">Collection of repository-relative file paths.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    Task UnstageAsync(IEnumerable<string> relativePaths, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unstages all files, resetting the entire index (.git/index) to match HEAD.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    Task UnstageAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Discards working tree modifications for a file by restoring its content from the index or HEAD.
    /// </summary>
    /// <param name="relativePath">Repository-relative file path.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    Task RestoreFileAsync(string relativePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Discards working tree modifications for a file by restoring its content from a specified source tree-ish.
    /// </summary>
    /// <param name="relativePath">Repository-relative file path.</param>
    /// <param name="source">Commit or tree-ish to restore from.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    Task RestoreFileAsync(string relativePath, string? source, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores index entries for the specified files from the specified tree-ish (or HEAD if not specified).
    /// </summary>
    /// <param name="relativePaths">Collection of repository-relative file paths.</param>
    /// <param name="source">Optional commit or tree-ish to restore from.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    Task RestoreIndexAsync(IEnumerable<string> relativePaths, string? source = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Discards all working tree modifications and deletions.
    /// </summary>
    /// <param name="removeUntracked">Whether to also delete untracked files from disk.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    Task RestoreAllAsync(bool removeUntracked = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new commit from currently staged changes (or all tracked changes if stageAll is true),
    /// advances the current branch reference, and synchronizes the index stat cache.
    /// </summary>
    /// <param name="message">The commit message.</param>
    /// <param name="metadata">Optional commit author and committer metadata.</param>
    /// <param name="stageAll">When true, stages all modified and deleted files before committing.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>The hash of the created commit.</returns>
    Task<GitHash> CommitAsync(
        string message,
        GitCommitMetadata? metadata = null,
        bool stageAll = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Amends the current HEAD commit using staged changes, preserving original author and parents.
    /// </summary>
    /// <param name="message">New commit message; if null, keeps existing commit message.</param>
    /// <param name="metadata">Optional commit author and committer metadata.</param>
    /// <param name="stageAll">When true, stages all modified and deleted files before amending.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>The hash of the amended commit.</returns>
    Task<GitHash> CommitAmendAsync(
        string? message = null,
        GitCommitMetadata? metadata = null,
        bool stageAll = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets the current branch to a specific commit using the specified mode.
    /// </summary>
    /// <param name="targetCommitHash">Target commit hash to reset to.</param>
    /// <param name="mode">Reset mode (Soft, Mixed, Hard).</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    Task ResetAsync(
        GitHash targetCommitHash,
        GitResetMode mode = GitResetMode.Mixed,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Squashes all commits between baseCommitHash and current HEAD into a single milestone commit,
    /// keeping working tree and index in sync.
    /// </summary>
    /// <param name="baseCommitHash">Ancestor base commit to squash onto.</param>
    /// <param name="message">Milestone commit message.</param>
    /// <param name="metadata">Optional commit author and committer metadata.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>The hash of the squashed commit.</returns>
    Task<GitHash> SquashRangeAsync(
        GitHash baseCommitHash,
        string message,
        GitCommitMetadata? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a revert commit that inverses the changes introduced by the specified commit.
    /// </summary>
    /// <param name="commitHash">The commit to revert.</param>
    /// <param name="metadata">Optional commit metadata (uses default revert message if null).</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>The hash of the revert commit.</returns>
    Task<GitHash> RevertAsync(
        GitHash commitHash,
        GitCommitMetadata? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes the unified diff of unstaged changes (working tree vs index).
    /// </summary>
    /// <param name="path">Optional path filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The unified diff text.</returns>
    Task<string> GetUnstagedDiffAsync(
        string? path = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes the unified diff of staged changes (index vs HEAD).
    /// </summary>
    /// <param name="path">Optional path filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The unified diff text.</returns>
    Task<string> GetStagedDiffAsync(
        string? path = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes the unified diff between the specified commit and the current working tree (including staged and unstaged changes).
    /// </summary>
    /// <param name="commitIsh">The commit or tree-ish to compare the working tree against.</param>
    /// <param name="path">Optional path filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The unified diff text.</returns>
    Task<string> GetWorktreeDiffAsync(
        string commitIsh,
        string? path = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Merges the specified branch or commit into the current HEAD.
    /// </summary>
    /// <param name="branchOrCommit">Branch name or commit-ish to merge.</param>
    /// <param name="options">Optional merge options (fast-forward controls, custom commit message, metadata).</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>A <see cref="GitMergeResult"/> describing the merge outcome.</returns>
    Task<GitMergeResult> MergeAsync(
        string branchOrCommit,
        GitMergeOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a merge operation is currently in progress.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>True if a merge is in progress (MERGE_HEAD exists); false otherwise.</returns>
    Task<bool> IsMergeInProgressAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the list of repository-relative file paths that are currently in conflict.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>A list of conflicted file paths.</returns>
    Task<IReadOnlyList<string>> GetConflictedFilesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a conflicted file as resolved by staging its current working tree content.
    /// </summary>
    /// <param name="relativeFilePath">Path of the file relative to the repository root.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    Task ResolveConflictAsync(string relativeFilePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Concludes an in-progress merge after all conflicts have been resolved, creating a merge commit with two parents.
    /// </summary>
    /// <param name="commitMessage">Optional commit message; if null, uses the default message in MERGE_MSG.</param>
    /// <param name="metadata">Optional commit metadata (author/committer).</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>The hash of the created merge commit.</returns>
    Task<GitHash> ContinueMergeAsync(
        string? commitMessage = null,
        GitCommitMetadata? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Aborts an in-progress merge and restores the repository state prior to the merge.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    Task AbortMergeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Switches the working tree, index, and HEAD to the specified branch, optionally creating it.
    /// </summary>
    /// <param name="branchName">The branch name to checkout (e.g. "main" or "feature").</param>
    /// <param name="createBranch">When true, creates a new branch before switching to it.</param>
    /// <param name="startPoint">Optional commit hash or reference to start from when <paramref name="createBranch"/> is true (defaults to HEAD).</param>
    /// <param name="force">When true, discards uncommitted working tree changes.</param>
    /// <param name="overwriteBranch">When true, allows overwriting an existing branch (when creating) without forcing workspace changes.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    Task CheckoutBranchAsync(
        string branchName,
        bool createBranch = false,
        string? startPoint = null,
        bool force = false,
        bool overwriteBranch = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks out the specified commit in a detached HEAD state, updating index and working tree.
    /// </summary>
    /// <param name="commitIsh">Commit hash or reference to check out.</param>
    /// <param name="force">When true, discards uncommitted working tree changes.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    Task CheckoutCommitAsync(
        string commitIsh,
        bool force = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates and switches to a new orphan branch (unborn branch with no commits yet).
    /// </summary>
    /// <param name="branchName">The name of the new orphan branch.</param>
    /// <param name="empty">When true (e.g. switch --orphan), starts with an empty index and working tree. When false (e.g. checkout --orphan), keeps index and working tree (or synchronizes to startPoint).</param>
    /// <param name="startPoint">Optional commit or tree-ish to start from when <paramref name="empty"/> is false.</param>
    /// <param name="force">When true, discards uncommitted working tree changes when synchronizing to <paramref name="startPoint"/>.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    Task CheckoutOrphanBranchAsync(
        string branchName,
        bool empty = false,
        string? startPoint = null,
        bool force = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves or renames a tracked file or directory in the working tree and index.
    /// </summary>
    /// <param name="sourcePath">Repository-relative source file or directory path.</param>
    /// <param name="destinationPath">Repository-relative destination file or directory path.</param>
    /// <param name="force">When true, allows overwriting existing destination files.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    Task MoveAsync(
        string sourcePath,
        string destinationPath,
        bool force = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves multiple tracked files or directories into a target destination directory in the working tree and index.
    /// </summary>
    /// <param name="sourcePaths">Collection of repository-relative source paths.</param>
    /// <param name="destinationDirectory">Repository-relative destination directory path.</param>
    /// <param name="force">When true, allows overwriting existing destination files.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    Task MoveAsync(
        IEnumerable<string> sourcePaths,
        string destinationDirectory,
        bool force = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves tracked files or directories with advanced options (force, skip errors, dry-run).
    /// </summary>
    /// <param name="sourcePaths">Collection of repository-relative source paths.</param>
    /// <param name="destinationPath">Repository-relative destination file or directory path.</param>
    /// <param name="options">Options controlling force, skip errors, and dry-run behavior.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>A <see cref="GitMoveResult"/> describing the moved items.</returns>
    Task<GitMoveResult> MoveAsync(
        IEnumerable<string> sourcePaths,
        string destinationPath,
        GitMoveOptions options,
        CancellationToken cancellationToken = default);
}
