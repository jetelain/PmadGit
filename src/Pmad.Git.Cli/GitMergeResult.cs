namespace Pmad.Git.Cli;

/// <summary>
/// Result of a merge (or pull) operation.
/// </summary>
public sealed class GitMergeResult
{
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
