using Pmad.Git.LocalRepositories;

namespace Pmad.Git.Cli;

/// <summary>
/// Extension methods to easily create a CLI-backed <see cref="GitRepositorySynchronizer"/> from an
/// <see cref="IGitRepository"/>.
/// </summary>
public static class GitRepositorySynchronizerExtensions
{
    /// <summary>
    /// Creates a <see cref="GitRepositorySynchronizer"/> that keeps this repository synchronized
    /// with its remote using the Git CLI.
    /// </summary>
    /// <param name="repository">The local repository to synchronize.</param>
    /// <param name="options">Synchronization options; defaults are used when omitted.</param>
    /// <param name="start">When <c>true</c> (default), immediately starts the periodic pull loop.</param>
    public static GitRepositorySynchronizer CreateSynchronizer(this IGitRepository repository, GitSyncOptions? options = null, bool start = true)
    {
        var runner = (options as GitCliSyncOptions)?.GitRunner ?? new GitRunner((options as GitCliSyncOptions)?.GitCliPath ?? "git");
        var cli = new GitCliRepository(repository, runner);
        var synchronizer = new GitRepositorySynchronizer(cli, repository, options);
        if (start)
        {
            synchronizer.Start();
        }
        return synchronizer;
    }

    /// <summary>
    /// Creates a <see cref="GitRepositorySynchronizer"/> that keeps this repository synchronized
    /// with its remote using the Git CLI at the specified executable path.
    /// </summary>
    /// <param name="repository">The local repository to synchronize.</param>
    /// <param name="gitCliPath">Path to the Git CLI executable.</param>
    /// <param name="options">Synchronization options; defaults are used when omitted.</param>
    /// <param name="start">When <c>true</c> (default), immediately starts the periodic pull loop.</param>
    public static GitRepositorySynchronizer CreateSynchronizer(this IGitRepository repository, string gitCliPath, GitSyncOptions? options = null, bool start = true)
    {
        var cli = new GitCliRepository(repository, gitCliPath);
        var synchronizer = new GitRepositorySynchronizer(cli, repository, options);
        if (start)
        {
            synchronizer.Start();
        }
        return synchronizer;
    }

    internal static GitRepositorySynchronizer CreateSynchronizer(this IGitRepository repository, IGitRunner gitRunner, GitSyncOptions? options = null, bool start = true)
    {
        var cli = new GitCliRepository(repository, gitRunner);
        var synchronizer = new GitRepositorySynchronizer(cli, repository, options);
        if (start)
        {
            synchronizer.Start();
        }
        return synchronizer;
    }
}
