namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Associates a file path with the most recent commit that changed it.
/// </summary>
/// <param name="Path">Repository-relative path of the file using / separators.</param>
/// <param name="Commit">The most recent commit that changed the file.</param>
public sealed record GitFileLastChange(string Path, GitCommit Commit);
