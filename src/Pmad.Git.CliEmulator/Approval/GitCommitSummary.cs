namespace Pmad.Git.CliEmulator.Approval;

/// <summary>
/// Lightweight commit information included in approval context objects.
/// </summary>
public sealed class GitCommitSummary
{
    /// <summary>Short (7-character) commit hash.</summary>
    public string ShortHash { get; init; } = string.Empty;

    /// <summary>Full commit hash.</summary>
    public string Hash { get; init; } = string.Empty;

    /// <summary>First line of the commit message.</summary>
    public string Subject { get; init; } = string.Empty;

    /// <summary>Author name.</summary>
    public string Author { get; init; } = string.Empty;

    /// <summary>Author email.</summary>
    public string AuthorEmail { get; init; } = string.Empty;

    /// <summary>Commit author timestamp.</summary>
    public DateTimeOffset Date { get; init; }

    /// <inheritdoc/>
    public override string ToString() => $"{ShortHash} {Subject}";
}
