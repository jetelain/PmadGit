namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Represents a git object payload as read from the object store.
/// </summary>
/// <param name="Type">The decoded type stored in the object header.</param>
/// <param name="Content">The raw decompressed object content.</param>
public sealed record GitObjectData(GitObjectType Type, byte[] Content);
