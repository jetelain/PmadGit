namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Updates an existing file in the tree.
/// </summary>
public sealed class UpdateFileOperation : GitCommitOperation
{
    /// <summary>
    /// Initializes a new instance of the UpdateFileOperation class to update the contents of a file in a Git
    /// repository, optionally verifying the previous file hash before applying the update.
    /// </summary>
    /// <param name="path">The relative path of the file to update within the repository. Cannot be null or empty.</param>
    /// <param name="content">The new content to write to the file. Cannot be null.</param>
    /// <param name="expectedPreviousHash">The expected previous hash of the file. If specified, the update will only proceed if the file's current hash
    /// matches this value; otherwise, the update is aborted.</param>
    /// <exception cref="ArgumentNullException">Thrown if content is null.</exception>
    public UpdateFileOperation(string path, byte[] content, GitHash? expectedPreviousHash = null) : base(path)
    {
        Content = content ?? throw new ArgumentNullException(nameof(content));
        ExpectedPreviousHash = expectedPreviousHash;
    }

    /// <summary>
    /// Gets the new content to write to the file.
    /// </summary>
    public byte[] Content { get; }

    /// <summary>
    /// Gets the expected previous hash of the file. If specified, the update will only proceed if the file's current hash
    /// matches this value; otherwise, the update is aborted.
    /// </summary>
    public GitHash? ExpectedPreviousHash { get; }
}
