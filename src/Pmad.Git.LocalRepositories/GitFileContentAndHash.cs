namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Represents the contents of a file in a Git repository along with its computed hash.
/// </summary>
/// <param name="Content">The raw byte array containing the file's content. Cannot be null.</param>
/// <param name="Hash">The Git hash corresponding to the file's content.</param>
public record struct GitFileContentAndHash(byte[] Content, GitHash Hash);
