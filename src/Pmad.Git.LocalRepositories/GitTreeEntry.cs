namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Describes a single entry within a git tree, including its mode and object id.
/// </summary>
/// <param name="Name">Display name of the entry.</param>
/// <param name="Kind">The object kind represented by the entry.</param>
/// <param name="Hash">The hash referencing the underlying object.</param>
/// <param name="Mode">The original POSIX mode stored alongside the entry.</param>
public sealed record GitTreeEntry(string Name, GitTreeEntryKind Kind, GitHash Hash, int Mode);
