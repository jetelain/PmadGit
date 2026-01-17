namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Describes a file operation applied when composing a commit.
/// </summary>
public abstract class GitCommitOperation
{
    protected GitCommitOperation(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be empty", nameof(path));
        }

        Path = path;
    }

    /// <summary>
    /// Gets the repository-relative path targeted by the operation.
    /// </summary>
    public string Path { get; }
}
