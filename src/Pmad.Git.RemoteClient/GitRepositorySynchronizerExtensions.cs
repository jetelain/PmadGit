using Pmad.Git.LocalRepositories;

namespace Pmad.Git.RemoteClient;

/// <summary>
/// Extension methods to easily create a managed <see cref="GitRemoteClientRepository"/>-backed
/// <see cref="GitRepositorySynchronizer"/> from an <see cref="IGitRepository"/>.
/// </summary>
public static class GitRepositorySynchronizerExtensions
{
    /// <summary>
    /// Creates a <see cref="GitRepositorySynchronizer"/> that keeps this repository synchronized
    /// with its remote using the managed Git Smart HTTP client.
    /// </summary>
    /// <param name="repository">The local repository to synchronize.</param>
    /// <param name="options">Remote client synchronization options.</param>
    /// <param name="start">When <c>true</c> (default), immediately starts the periodic pull loop.</param>
    /// <returns>A new <see cref="GitRepositorySynchronizer"/> instance.</returns>
    public static GitRepositorySynchronizer CreateSynchronizer(
        this IGitRepository repository,
        GitRemoteClientSyncOptions options,
        bool start = true)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(options);

        var clientOptions = options.ClientOptions != null
            ? new GitRemoteClientOptions
            {
                Credentials = options.Credentials ?? options.ClientOptions.Credentials,
                HttpClient = options.ClientOptions.HttpClient,
                Agent = options.ClientOptions.Agent,
                Timeout = options.ClientOptions.Timeout,
                OnProgress = options.ClientOptions.OnProgress,
                SanitizeRemoteUrlInConfig = options.ClientOptions.SanitizeRemoteUrlInConfig
            }
            : new GitRemoteClientOptions
            {
                Credentials = options.Credentials
            };

        var remoteRepo = new GitRemoteClientRepository(repository, options.Url, clientOptions);
        var synchronizer = new GitRepositorySynchronizer(remoteRepo, repository, options);
        if (start)
        {
            synchronizer.Start();
        }
        return synchronizer;
    }
}

