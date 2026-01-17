namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Adds a new file to the tree.
/// </summary>
public sealed class AddFileOperation : GitCommitOperation
{
    public AddFileOperation(string path, byte[] content) : base(path)
    {
        Content = content ?? throw new ArgumentNullException(nameof(content));
    }

    public byte[] Content { get; }
}
