namespace Pmad.Git.Cli;

/// <summary>
/// Options controlling how a <see cref="GitRepositorySynchronizer"/> synchronizes a local
/// repository with its remote counterpart.
/// </summary>
public sealed class GitSyncOptions
{
    /// <summary>
    /// Delay used to debounce local-to-remote pushes: after a local change is notified via
    /// <see cref="GitRepositorySynchronizer.NotifyLocalChange"/>, the synchronizer waits for this
    /// delay of inactivity before actually pushing. Defaults to 5 minutes.
    /// </summary>
    public TimeSpan PushDebounceDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Interval at which the synchronizer automatically pulls from the remote repository.
    /// Defaults to 1 hour.
    /// </summary>
    public TimeSpan PullInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Name of the remote to synchronize with. Defaults to <c>origin</c> when <c>null</c>.
    /// </summary>
    public string? Remote { get; set; }

    /// <summary>
    /// Name of the branch to synchronize. Defaults to the current branch when <c>null</c>.
    /// </summary>
    public string? Branch { get; set; }

    /// <summary>
    /// Path to the Git CLI executable used by the underlying <see cref="GitCliRepository"/>.
    /// Setting this property replaces <see cref="GitRunner"/> with a new default runner using the
    /// given path.
    /// </summary>
    public string GitCliPath
    {
        get => GitRunner.GitCliPath;
        set { GitRunner = new GitRunner(value); }
    }

    /// <summary>
    /// The <see cref="IGitRunner"/> used by the underlying <see cref="GitCliRepository"/>. Defaults
    /// to a runner using <see cref="GitCliPath"/>; can be overridden directly, e.g. with a test
    /// double, to ease unit test creation. Not part of the public API.
    /// </summary>
    internal IGitRunner GitRunner { get; set; } = new GitRunner("git");
}
