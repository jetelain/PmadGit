namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Updates an existing file in the tree from a stream.
/// </summary>
public sealed class UpdateFileStreamOperation : GitCommitOperation
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateFileStreamOperation"/> class to update the contents of a file
    /// in a Git repository, optionally verifying the previous file hash before applying the update.
    /// </summary>
    /// <param name="path">The relative path of the file to update within the repository. Cannot be null or empty.</param>
    /// <param name="content">The stream providing the new content to write to the file. Cannot be null.</param>
    /// <param name="expectedPreviousHash">The expected previous hash of the file. If specified, the update will only proceed if the file's current hash
    /// matches this value; otherwise, a <see cref="GitFileConflictException"/> is thrown.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="content"/> is null.</exception>
    /// <exception cref="GitFileConflictException">Thrown if the file's current hash does not match the expected previous hash.</exception>
    public UpdateFileStreamOperation(string path, Stream content, GitHash? expectedPreviousHash = null) : base(path)
    {
        Content = content ?? throw new ArgumentNullException(nameof(content));
        ExpectedPreviousHash = expectedPreviousHash;
    }

    /// <summary>
    /// Gets the stream providing the new content to write to the file.
    /// </summary>
    public Stream Content { get; }

    /// <summary>
    /// Gets the expected previous hash of the file. If specified, the update will only proceed if the file's current hash
    /// matches this value; otherwise, a <see cref="GitFileConflictException"/> is thrown.
    /// </summary>
    public GitHash? ExpectedPreviousHash { get; }
}
