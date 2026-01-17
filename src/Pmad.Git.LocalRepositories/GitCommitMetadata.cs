namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Describes the metadata recorded in a commit object.
/// </summary>
public sealed class GitCommitMetadata
{
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

    public string Message { get; }
    public GitCommitSignature Author { get; }
    public GitCommitSignature Committer { get; }

    public string AuthorName => Author.Name;
    public string AuthorEmail => Author.Email;
    public DateTimeOffset AuthorDate => Author.Timestamp;
    public string CommitterName => Committer.Name;
    public string CommitterEmail => Committer.Email;
    public DateTimeOffset CommitterDate => Committer.Timestamp;
}
