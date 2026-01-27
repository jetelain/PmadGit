namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Describes the metadata recorded in a commit object.
/// </summary>
public sealed class GitCommitMetadata
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GitCommitMetadata"/> class.
    /// </summary>
    /// <param name="message">The commit message.</param>
    /// <param name="author">The author signature.</param>
    /// <param name="committer">The committer signature. If null, the author signature is used.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="message"/> is null, empty, or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="author"/> is null.</exception>
    public GitCommitMetadata(string message, GitCommitSignature author, GitCommitSignature? committer = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Commit message cannot be empty", nameof(message));
        }

        Author = author ?? throw new ArgumentNullException(nameof(author));
        Committer = committer ?? author;
        Message = message;
    }

    /// <summary>
    /// Gets the commit message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the author signature.
    /// </summary>
    public GitCommitSignature Author { get; }

    /// <summary>
    /// Gets the committer signature.
    /// </summary>
    public GitCommitSignature Committer { get; }

    /// <summary>
    /// Gets the name of the commit author.
    /// </summary>
    public string AuthorName => Author.Name;

    /// <summary>
    /// Gets the email address of the commit author.
    /// </summary>
    public string AuthorEmail => Author.Email;

    /// <summary>
    /// Gets the timestamp when the commit was authored.
    /// </summary>
    public DateTimeOffset AuthorDate => Author.Timestamp;

    /// <summary>
    /// Gets the name of the committer.
    /// </summary>
    public string CommitterName => Committer.Name;

    /// <summary>
    /// Gets the email address of the committer.
    /// </summary>
    public string CommitterEmail => Committer.Email;

    /// <summary>
    /// Gets the timestamp when the commit was committed.
    /// </summary>
    public DateTimeOffset CommitterDate => Committer.Timestamp;
}
