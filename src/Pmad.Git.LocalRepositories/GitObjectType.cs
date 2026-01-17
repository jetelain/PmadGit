namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Represents the different object types that can be stored in a git database.
/// </summary>
public enum GitObjectType
{
    Commit = 1,
    Tree = 2,
    Blob = 3,
    Tag = 4
}
