namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Represents the different object types that can be stored in a git database.
/// </summary>
public enum GitObjectType
{
    /// <summary>
    /// A commit object that represents a snapshot of the repository at a point in time.
    /// </summary>
    Commit = 1,

    /// <summary>
    /// A tree object that represents a directory structure.
    /// </summary>
    Tree = 2,

    /// <summary>
    /// A blob object that represents file content.
    /// </summary>
    Blob = 3,

    /// <summary>
    /// A tag object that represents a named reference to another object.
    /// </summary>
    Tag = 4
}
