using Pmad.Git.LocalRepositories;

namespace Pmad.Git.Cli;

/// <summary>
/// CLI-specific options controlling how a <see cref="GitRepositorySynchronizer"/> synchronizes a local
/// repository with its remote counterpart using the Git CLI.
/// </summary>
public class GitCliSyncOptions : GitSyncOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GitCliSyncOptions"/> class with default settings.
    /// </summary>
    public GitCliSyncOptions()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GitCliSyncOptions"/> class copying settings from <paramref name="options"/>.
    /// </summary>
    /// <param name="options">The options to copy from.</param>
    public GitCliSyncOptions(GitSyncOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        Remote = options.Remote;
        Branch = options.Branch;
        PullInterval = options.PullInterval;
        PushDebounceDelay = options.PushDebounceDelay;
        if (options is GitCliSyncOptions cliOptions)
        {
            GitRunner = cliOptions.GitRunner;
        }
    }
    /// <summary>
    /// Path to the Git CLI executable used by the underlying <see cref="GitCliRepository"/>.
    /// Setting this property replaces <see cref="GitRunner"/> with a new default runner using the
    /// given path.
    /// </summary>
    public string GitCliPath
    {
        get => GitRunner.GitCliPath;
        set => GitRunner = new GitRunner(value);
    }

    /// <summary>
    /// The <see cref="IGitRunner"/> used by the underlying <see cref="GitCliRepository"/>. Defaults
    /// to a runner using <see cref="GitCliPath"/>; can be overridden directly, e.g. with a test
    /// double, to ease unit test creation. Not part of the public API.
    /// </summary>
    internal IGitRunner GitRunner { get; set; } = new GitRunner("git");
}
