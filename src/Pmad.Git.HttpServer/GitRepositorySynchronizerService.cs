using System.Collections.Concurrent;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.HttpServer;

/// <summary>
/// Default implementation of <see cref="IGitRepositorySynchronizerService"/> that maintains a cache
/// of <see cref="GitRepositorySynchronizer"/> instances built on top of <see cref="IGitRepositoryService"/>.
/// This service is thread-safe and should be registered as a singleton.
/// </summary>
internal sealed class GitRepositorySynchronizerService : IGitRepositorySynchronizerService
{
    private readonly IGitRepositoryService _repositoryService;
    private readonly ConcurrentDictionary<string, GitRepositorySynchronizer> _synchronizers = new(StringComparer.OrdinalIgnoreCase);

    public GitRepositorySynchronizerService(IGitRepositoryService repositoryService)
    {
        _repositoryService = repositoryService ?? throw new ArgumentNullException(nameof(repositoryService));
    }

    public GitRepositorySynchronizer? GetSynchronizerByPath(string repositoryPath)
    {
        var normalizedPath = GitRepositoryService.NormalizeAndValidatePath(repositoryPath);

        return _synchronizers.TryGetValue(normalizedPath, out var synchronizer) ? synchronizer : null;
    }

    public GitRepositorySynchronizer SetupSynchronizer(string repositoryPath, Func<IGitRepository, IGitRepositoryWithRemote> remoteRepositoryFactory, GitSyncOptions? options = null)
    {
        if (remoteRepositoryFactory is null)
        {
            throw new ArgumentNullException(nameof(remoteRepositoryFactory));
        }

        return SetupSynchronizer(repositoryPath, localRepo =>
        {
            var remoteRepo = remoteRepositoryFactory(localRepo);
            if (remoteRepo is null)
            {
                throw new InvalidOperationException("The remote repository factory returned null.");
            }

            var synchronizer = new GitRepositorySynchronizer(remoteRepo, localRepo, options);
            synchronizer.Start();
            return synchronizer;
        });
    }

    public GitRepositorySynchronizer SetupSynchronizer(string repositoryPath, Func<IGitRepository, GitRepositorySynchronizer> synchronizerFactory)
    {
        if (synchronizerFactory is null)
        {
            throw new ArgumentNullException(nameof(synchronizerFactory));
        }

        var normalizedPath = GitRepositoryService.NormalizeAndValidatePath(repositoryPath);
        var localRepo = _repositoryService.GetRepositoryByPath(normalizedPath);

        var synchronizer = synchronizerFactory(localRepo);
        if (synchronizer is null)
        {
            throw new InvalidOperationException("The synchronizer factory returned null.");
        }

        return StoreSynchronizer(normalizedPath, synchronizer);
    }

    public Task<GitRepositorySynchronizer> SetupSynchronizerAsync(
        string repositoryPath,
        Func<string, CancellationToken, Task> cloneAsync,
        Func<IGitRepository, IGitRepositoryWithRemote> remoteRepositoryFactory,
        GitSyncOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (remoteRepositoryFactory is null)
        {
            throw new ArgumentNullException(nameof(remoteRepositoryFactory));
        }

        return SetupSynchronizerAsync(repositoryPath, cloneAsync, localRepo =>
        {
            var remoteRepo = remoteRepositoryFactory(localRepo);
            if (remoteRepo is null)
            {
                throw new InvalidOperationException("The remote repository factory returned null.");
            }

            var synchronizer = new GitRepositorySynchronizer(remoteRepo, localRepo, options);
            synchronizer.Start();
            return synchronizer;
        }, cancellationToken);
    }

    public async Task<GitRepositorySynchronizer> SetupSynchronizerAsync(
        string repositoryPath,
        Func<string, CancellationToken, Task> cloneAsync,
        Func<IGitRepository, GitRepositorySynchronizer> synchronizerFactory,
        CancellationToken cancellationToken = default)
    {
        if (cloneAsync is null)
        {
            throw new ArgumentNullException(nameof(cloneAsync));
        }
        if (synchronizerFactory is null)
        {
            throw new ArgumentNullException(nameof(synchronizerFactory));
        }

        var normalizedPath = GitRepositoryService.NormalizePath(repositoryPath);

        if (!GitRepositoryService.IsExistingRepository(normalizedPath))
        {
            if (Directory.Exists(normalizedPath) && Directory.EnumerateFileSystemEntries(normalizedPath).Any())
            {
                throw new InvalidOperationException($"Directory already exists and is not empty: '{normalizedPath}'.");
            }

            await cloneAsync(normalizedPath, cancellationToken).ConfigureAwait(false);
        }

        return SetupSynchronizer(normalizedPath, synchronizerFactory);
    }

    private GitRepositorySynchronizer StoreSynchronizer(string normalizedPath, GitRepositorySynchronizer synchronizer)
    {
        GitRepositorySynchronizer? previous = null;
        _synchronizers.AddOrUpdate(normalizedPath, synchronizer, (_, existing) =>
        {
            previous = existing;
            return synchronizer;
        });

        if (previous != null)
        {
            _ = previous.DisposeAsync();
        }

        return synchronizer;
    }

    public void InvalidateSynchronizer(string repositoryPath)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath))
        {
            return;
        }

        var normalizedPath = GitRepositoryService.NormalizePath(repositoryPath);
        if (_synchronizers.TryRemove(normalizedPath, out var synchronizer))
        {
            _ = synchronizer.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var path in _synchronizers.Keys)
        {
            if (_synchronizers.TryRemove(path, out var synchronizer))
            {
                await synchronizer.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
