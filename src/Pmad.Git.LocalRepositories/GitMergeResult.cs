namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Status of a git merge operation.
/// </summary>
public enum GitMergeStatus
{
    /// <summary>
    /// Current HEAD already contains all commits from the target branch.
    /// </summary>
    AlreadyUpToDate,

    /// <summary>
    /// Merge was resolved by fast-forwarding the current branch reference.
    /// </summary>
    FastForward,

    /// <summary>
    /// A 3-way merge commit was successfully created without conflicts.
    /// </summary>
    Merged,

    /// <summary>
    /// The merge encountered conflicts that require manual resolution.
    /// </summary>
    Conflicted
}

/// <summary>
/// Options controlling git merge behavior.
/// </summary>
public sealed class GitMergeOptions
{
    /// <summary>
    /// When true, creates a merge commit even if the merge could be resolved as a fast-forward (--no-ff).
    /// </summary>
    public bool NoFastForward { get; set; }

    /// <summary>
    /// When true, refuses to merge and fails if the merge cannot be resolved as a fast-forward (--ff-only).
    /// </summary>
    public bool FastForwardOnly { get; set; }

    /// <summary>
    /// Custom commit message to use if a merge commit is created.
    /// </summary>
    public string? CommitMessage { get; set; }

    /// <summary>
    /// Custom commit author and committer metadata.
    /// </summary>
    public GitCommitMetadata? Metadata { get; set; }
}

/// <summary>
/// Result of a merge operation.
/// </summary>
public class GitMergeResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GitMergeResult"/> class.
    /// </summary>
    /// <param name="isSuccess"><c>true</c> when the merge completed without conflict; otherwise <c>false</c>.</param>
    /// <param name="conflictedFiles">List of file paths relative to the repository root that are currently in conflict.</param>
    /// <param name="commitHash">The commit hash of the resulting commit, if created or fast-forwarded.</param>
    /// <param name="status">Detailed status of the merge operation.</param>
    /// <param name="message">Informational message describing the outcome.</param>
    public GitMergeResult(
        bool isSuccess,
        IReadOnlyList<string> conflictedFiles,
        GitHash? commitHash = null,
        GitMergeStatus status = GitMergeStatus.Merged,
        string? message = null)
    {
        IsSuccess = isSuccess;
        ConflictedFiles = conflictedFiles;
        CommitHash = commitHash;
        Status = status;
        Message = message;
    }

    /// <summary>
    /// <c>true</c> when the merge completed without any conflict.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// <c>true</c> when the merge stopped because of one or more conflicts.
    /// </summary>
    public bool HasConflicts => ConflictedFiles.Count > 0;

    /// <summary>
    /// Relative paths of the files that are in conflict. Empty when <see cref="IsSuccess"/> is <c>true</c>.
    /// </summary>
    public IReadOnlyList<string> ConflictedFiles { get; }

    /// <summary>
    /// The resulting commit hash after a successful merge or fast-forward; null if conflicted.
    /// </summary>
    public GitHash? CommitHash { get; }

    /// <summary>
    /// Detailed status of the merge.
    /// </summary>
    public GitMergeStatus Status { get; }

    /// <summary>
    /// Optional informational message describing the merge outcome.
    /// </summary>
    public string? Message { get; }
}
