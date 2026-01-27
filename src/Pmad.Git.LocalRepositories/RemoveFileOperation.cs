namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Removes a file from the tree.
/// </summary>
public sealed class RemoveFileOperation : GitCommitOperation
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RemoveFileOperation"/> class.
    /// </summary>
    /// <param name="path">The repository-relative path of the file to remove.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null, empty, or whitespace.</exception>
    public RemoveFileOperation(string path) : base(path)
    {
    }
}
