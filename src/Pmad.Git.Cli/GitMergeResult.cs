namespace Pmad.Git.Cli;

/// <summary>
/// Result of a merge (or pull) operation.
/// </summary>
public sealed class GitMergeResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GitMergeResult"/> class.
    /// </summary>
    /// <param name="isSuccess"><c>true</c> when the merge completed without conflict; otherwise <c>false</c>.</param>
    /// <param name="conflictedFiles">List of file paths relative to the repository root that are currently in conflict.</param>
    public GitMergeResult(bool isSuccess, IReadOnlyList<string> conflictedFiles)
    {
        IsSuccess = isSuccess;
        ConflictedFiles = conflictedFiles;
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
}
