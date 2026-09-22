namespace Pmad.Git.Cli;

/// <summary>
/// Result of a merge (or pull) operation.
/// </summary>
public sealed class GitMergeResult : Pmad.Git.LocalRepositories.GitMergeResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GitMergeResult"/> class.
    /// </summary>
    /// <param name="isSuccess"><c>true</c> when the merge completed without conflict; otherwise <c>false</c>.</param>
    /// <param name="conflictedFiles">List of file paths relative to the repository root that are currently in conflict.</param>
    public GitMergeResult(bool isSuccess, IReadOnlyList<string> conflictedFiles)
        : base(isSuccess, conflictedFiles)
    {
    }
}
