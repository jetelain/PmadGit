using System.Collections.Concurrent;
using Pmad.Git.Cli;
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

    public GitRepositorySynchronizer SetupCliSynchronizer(string repositoryPath, GitCliSyncOptions options)
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

    public async Task<GitRepositorySynchronizer> SetupCliSynchronizerAsync(string repositoryPath, string remoteUrl, GitCliSyncOptions options, CancellationToken cancellationToken = default)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }
        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            throw new ArgumentException("Remote URL cannot be null or whitespace.", nameof(remoteUrl));
        }

        var normalizedPath = GitRepositoryService.NormalizePath(repositoryPath);

        if (!GitRepositoryService.IsExistingRepository(normalizedPath))
        {
            if (Directory.Exists(normalizedPath) && Directory.EnumerateFileSystemEntries(normalizedPath).Any())
            {
                throw new InvalidOperationException($"Directory '{normalizedPath}' already exists, is not empty, but does not contain a git repository.");
            }

            var gitCliPath = options.GitCliPath ?? "git";
            await GitCliRepository.CloneAsync(remoteUrl, normalizedPath, options.Branch, options.Remote, gitCliPath, cancellationToken).ConfigureAwait(false);
        }

        return SetupCliSynchronizer(normalizedPath, options);
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
