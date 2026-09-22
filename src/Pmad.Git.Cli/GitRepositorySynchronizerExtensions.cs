using Pmad.Git.LocalRepositories;

namespace Pmad.Git.Cli;

/// <summary>
/// Extension methods to easily create a <see cref="GitRepositorySynchronizer"/> from an
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
        var synchronizer = new GitRepositorySynchronizer(repository, options);
        if (start)
        {
            synchronizer.Start();
        }
        return synchronizer;
    }

    /// <summary>
    /// Creates a <see cref="GitRepositorySynchronizer"/> that keeps this remote repository synchronized.
    /// If the repository implements <see cref="IGitRepositoryCacheInvalidator"/>, it is automatically
    /// subscribed to detect local changes.
    /// </summary>
    /// <param name="remoteRepository">The remote repository to synchronize.</param>
    /// <param name="options">Synchronization options; defaults are used when omitted.</param>
    /// <param name="start">When <c>true</c> (default), immediately starts the periodic pull loop.</param>
    public static GitRepositorySynchronizer CreateSynchronizer(this IGitRemoteRepository remoteRepository, GitSyncOptions? options = null, bool start = true)
    {
        var synchronizer = new GitRepositorySynchronizer(remoteRepository, options);
        if (start)
        {
            synchronizer.Start();
        }
        return synchronizer;
    }

    /// <summary>
    /// Creates a <see cref="GitRepositorySynchronizer"/> that keeps this remote repository synchronized,
    /// subscribing to the given <paramref name="changeSource"/> to detect local changes.
    /// </summary>
    /// <param name="remoteRepository">The remote repository to synchronize.</param>
    /// <param name="changeSource">The change source to observe for local changes.</param>
    /// <param name="options">Synchronization options; defaults are used when omitted.</param>
    /// <param name="start">When <c>true</c> (default), immediately starts the periodic pull loop.</param>
    public static GitRepositorySynchronizer CreateSynchronizer(this IGitRemoteRepository remoteRepository, IGitRepositoryCacheInvalidator? changeSource, GitSyncOptions? options = null, bool start = true)
    {
        var synchronizer = new GitRepositorySynchronizer(remoteRepository, changeSource, options);
        if (start)
        {
            synchronizer.Start();
        }
        return synchronizer;
    }
}
