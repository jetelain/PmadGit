namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Removes a file from the tree.
/// </summary>
public sealed class RemoveFileOperation : GitCommitOperation
{
    public RemoveFileOperation(string path) : base(path)
    {
    }
}
