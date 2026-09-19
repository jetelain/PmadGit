namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Represents the status of a file in the Git working tree or staging area.
/// </summary>
public enum GitFileStatus
{
    /// <summary>
    /// File is unmodified between HEAD, index, and working tree.
    /// </summary>
    Clean = 0,

    /// <summary>
    /// File is present in working tree but not tracked in the index or HEAD.
    /// </summary>
    Untracked = 1,

    /// <summary>
    /// File is modified in the working tree compared to the index.
    /// </summary>
    Modified = 2,

    /// <summary>
    /// File has been deleted from the working tree but is still in the index.
    /// </summary>
    Deleted = 3,

    /// <summary>
    /// File is newly added to the index (not present in HEAD).
    /// </summary>
    StagedNew = 4,

    /// <summary>
    /// File is modified in the index compared to HEAD.
    /// </summary>
    StagedModified = 5,

    /// <summary>
    /// File is marked as deleted in the index compared to HEAD.
    /// </summary>
    StagedDeleted = 6,

    /// <summary>
    /// File has an unresolved merge conflict (stage > 0).
    /// </summary>
    Conflicted = 7,

    /// <summary>
    /// File is ignored by .gitignore rules.
    /// </summary>
    Ignored = 8
}
