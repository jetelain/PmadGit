using System.Collections.Concurrent;
using Pmad.Git.Cli;
using Pmad.Git.LocalRepositories.Utilities;

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

    public GitRepositorySynchronizer SetupSynchronizer(string repositoryPath, GitSyncOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var normalizedPath = GitRepositoryService.NormalizeAndValidatePath(repositoryPath);

        var synchronizer = _repositoryService.GetRepositoryByPath(normalizedPath).CreateSynchronizer(options);

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

        var normalizedPath = Path.GetFullPath(repositoryPath);
        if (_synchronizers.TryRemove(normalizedPath, out var synchronizer))
        {
            _ = synchronizer.DisposeAsync();
        }
    }
}
