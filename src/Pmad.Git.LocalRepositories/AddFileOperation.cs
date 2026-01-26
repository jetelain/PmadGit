namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Adds a new file to the tree.
/// </summary>
public sealed class AddFileOperation : GitCommitOperation
{
    /// <summary>
    /// Initializes a new instance of the AddFileOperation class to add a new file to a Git repository.
    /// </summary>
    /// <param name="path">The relative path of the file to add within the repository. Cannot be null or empty.</param>
    /// <param name="content">The content to write to the file. Cannot be null.</param>
    /// <exception cref="ArgumentNullException">Thrown if content is null.</exception>
    public AddFileOperation(string path, byte[] content) : base(path)
    {
        Content = content ?? throw new ArgumentNullException(nameof(content));
    }

    /// <summary>
    /// Gets the content to write to the file.
    /// </summary>
    public byte[] Content { get; }
}
