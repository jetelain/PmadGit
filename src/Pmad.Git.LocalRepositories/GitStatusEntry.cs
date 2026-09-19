namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Represents the Git status of an individual file across HEAD, index, and working tree.
/// </summary>
public sealed record GitStatusEntry(
    string Path,
    GitFileStatus StagedStatus,
    GitFileStatus WorkingTreeStatus,
    GitHash? HeadHash = null,
    GitHash? IndexHash = null,
    GitHash? WorkingTreeHash = null)
{
    /// <summary>
    /// Gets a value indicating whether this file has staged changes relative to HEAD.
    /// </summary>
    public bool IsStaged => StagedStatus is GitFileStatus.StagedNew
        or GitFileStatus.StagedModified
        or GitFileStatus.StagedDeleted;

    /// <summary>
    /// Gets a value indicating whether this file has unstaged modifications, deletions, or is untracked.
    /// </summary>
    public bool HasWorkingTreeChanges => WorkingTreeStatus is GitFileStatus.Modified
        or GitFileStatus.Deleted
        or GitFileStatus.Untracked;

    /// <summary>
    /// Gets a value indicating whether this file is in an unresolved merge conflict state.
    /// </summary>
    public bool IsConflicted => StagedStatus == GitFileStatus.Conflicted
        || WorkingTreeStatus == GitFileStatus.Conflicted;

    /// <summary>
    /// Gets a value indicating whether this file is completely clean (no staged or working tree changes, not conflicted).
    /// </summary>
    public bool IsClean => !IsStaged && !HasWorkingTreeChanges && !IsConflicted;
}
