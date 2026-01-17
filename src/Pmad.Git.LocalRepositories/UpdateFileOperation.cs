namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Updates an existing file in the tree.
/// </summary>
public sealed class UpdateFileOperation : GitCommitOperation
{
    public UpdateFileOperation(string path, byte[] content) : base(path)
    {
        Content = content ?? throw new ArgumentNullException(nameof(content));
    }

    public byte[] Content { get; }
}
