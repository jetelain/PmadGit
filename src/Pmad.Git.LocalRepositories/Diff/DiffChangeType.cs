namespace Pmad.Git.LocalRepositories.Diff;

/// <summary>
/// Specifies the type of difference operation.
/// </summary>
public enum DiffChangeType
{
    /// <summary>
    /// Item is unchanged and preserved between versions.
    /// </summary>
    Keep,

    /// <summary>
    /// Item was inserted in the new version.
    /// </summary>
    Insert,

    /// <summary>
    /// Item was deleted from the old version.
    /// </summary>
    Delete
}

