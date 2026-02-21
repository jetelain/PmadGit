namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Adds a new file to the tree from a stream.
/// </summary>
public sealed class AddFileStreamOperation : GitCommitOperation
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AddFileStreamOperation"/> class to add a new file to a Git repository.
    /// </summary>
    /// <param name="path">The relative path of the file to add within the repository. Cannot be null or empty.</param>
    /// <param name="content">The stream providing the content to write to the file. Cannot be null.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="content"/> is null.</exception>
    public AddFileStreamOperation(string path, Stream content) : base(path)
    {
        Content = content ?? throw new ArgumentNullException(nameof(content));
    }

    /// <summary>
    /// Gets the stream providing the content to write to the file.
    /// </summary>
    public Stream Content { get; }
}
