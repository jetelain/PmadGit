namespace Pmad.Git.Cli;

/// <summary>
/// Represents the tracking and synchronization status of a local branch relative to its upstream remote branch.
/// </summary>
public sealed record GitTrackingStatus(
    string LocalBranch,
    string? UpstreamBranch,
    int AheadCount,
    int BehindCount)
{
    /// <summary>
    /// Gets a value indicating whether an upstream tracking branch is configured.
    /// </summary>
    public bool HasUpstream => !string.IsNullOrEmpty(UpstreamBranch);

    /// <summary>
    /// Gets a value indicating whether the local branch has unpushed commits ahead of upstream.
    /// </summary>
    public bool HasUnpushedCommits => AheadCount > 0;

    /// <summary>
    /// Gets a value indicating whether the upstream branch has unpulled commits behind local.
    /// </summary>
    public bool HasUnpulledCommits => BehindCount > 0;

    /// <summary>
    /// Gets a value indicating whether the local branch and its upstream are completely synchronized.
    /// </summary>
    public bool IsSynchronized => HasUpstream && AheadCount == 0 && BehindCount == 0;
}
