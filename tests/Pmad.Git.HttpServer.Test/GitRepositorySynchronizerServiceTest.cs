using Pmad.Git.Cli;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.HttpServer.Test;

public sealed class GitRepositorySynchronizerServiceTest : IDisposable
{
    private readonly string _testRoot;
    private readonly List<GitTestRepository> _repositories = new();

    public GitRepositorySynchronizerServiceTest()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "PmadGitSynchronizerServiceTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
    }

    private GitTestRepository CreateRepository()
    {
        var repository = GitTestRepository.Create();
        _repositories.Add(repository);
        return repository;
    }

    [Fact]
    public void GetSynchronizerByPath_WithoutSetup_ShouldReturnNull()
    {
        // Arrange
        var repository = CreateRepository();
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);

        // Act
        var synchronizer = service.GetSynchronizerByPath(repository.WorkingDirectory);

        // Assert
        Assert.Null(synchronizer);
    }

    [Fact]
    public async Task SetupSynchronizer_WithSynchronizerFactory_ShouldCreateAndCacheSynchronizer()
    {
        // Arrange
        var repository = CreateRepository();
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);

        // Act
        var synchronizer = service.SetupSynchronizer(repository.WorkingDirectory, repo => repo.CreateSynchronizer(new GitCliSyncOptions()));

        // Assert
        Assert.NotNull(synchronizer);
        Assert.Same(synchronizer, service.GetSynchronizerByPath(repository.WorkingDirectory));

        await synchronizer.DisposeAsync();
    }

    [Fact]
    public async Task SetupSynchronizer_WithRemoteRepositoryFactory_ShouldCreateAndCacheSynchronizer()
    {
        // Arrange
        var repository = CreateRepository();
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);
        var options = new GitSyncOptions { Branch = "main" };

        // Act
        var synchronizer = service.SetupSynchronizer(repository.WorkingDirectory, repo => new FakeRemoteRepository { RootPath = repo.RootPath }, options);

        // Assert
        Assert.NotNull(synchronizer);
        Assert.Same(synchronizer, service.GetSynchronizerByPath(repository.WorkingDirectory));

        await synchronizer.DisposeAsync();
    }

    [Fact]
    public async Task SetupSynchronizer_GuaranteesSameRepositoryInstanceIsUsed()
    {
        // Arrange
        var repository = CreateRepository();
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);

        IGitRepository? passedRepo = null;

        // Act
        var synchronizer = service.SetupSynchronizer(repository.WorkingDirectory, repo =>
        {
            passedRepo = repo;
            return new FakeRemoteRepository { RootPath = repo.RootPath };
        });

        // Assert
        var expectedRepo = repositoryService.GetRepositoryByPath(repository.WorkingDirectory);
        Assert.Same(expectedRepo, passedRepo);

        await synchronizer.DisposeAsync();
    }

    [Fact]
    public async Task GetSynchronizerByPath_WithTrailingDirectorySeparator_ShouldReturnSameSynchronizer()
    {
        // Arrange
        var repository = CreateRepository();
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);
        var pathWithoutSlash = repository.WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var pathWithSlash = pathWithoutSlash + Path.DirectorySeparatorChar;

        // Act
        var synchronizer = service.SetupSynchronizer(pathWithoutSlash, repo => repo.CreateSynchronizer(new GitCliSyncOptions()));

        // Assert
        Assert.NotNull(synchronizer);
        Assert.Same(synchronizer, service.GetSynchronizerByPath(pathWithSlash));

        await synchronizer.DisposeAsync();
    }

    [Fact]
    public void InvalidateSynchronizer_WithTrailingDirectorySeparator_ShouldRemoveCachedSynchronizer()
    {
        // Arrange
        var repository = CreateRepository();
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);
        var pathWithoutSlash = repository.WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var pathWithSlash = pathWithoutSlash + Path.DirectorySeparatorChar;
        service.SetupSynchronizer(pathWithoutSlash, repo => repo.CreateSynchronizer(new GitCliSyncOptions()));

        // Act
        service.InvalidateSynchronizer(pathWithSlash);

        // Assert
        Assert.Null(service.GetSynchronizerByPath(pathWithoutSlash));
        Assert.Null(service.GetSynchronizerByPath(pathWithSlash));
    }

    [Fact]
    public async Task SetupSynchronizer_CalledTwice_ShouldReplacePreviousInstanceAndDisposeIt()
    {
        // Arrange
        var repository = CreateRepository();
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);

        // Act
        var synchronizer1 = service.SetupSynchronizer(repository.WorkingDirectory, repo => repo.CreateSynchronizer(new GitCliSyncOptions()));
        var synchronizer2 = service.SetupSynchronizer(repository.WorkingDirectory, repo => repo.CreateSynchronizer(new GitCliSyncOptions()));

        // Assert
        Assert.NotSame(synchronizer1, synchronizer2);
        Assert.Same(synchronizer2, service.GetSynchronizerByPath(repository.WorkingDirectory));

        await synchronizer2.DisposeAsync();
    }

    [Fact]
    public void SetupSynchronizer_WithNullSynchronizerFactory_ShouldThrowArgumentNullException()
    {
        // Arrange
        var repository = CreateRepository();
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => service.SetupSynchronizer(repository.WorkingDirectory, (Func<IGitRepository, GitRepositorySynchronizer>)null!));
    }

    [Fact]
    public void SetupSynchronizer_WithNullRemoteRepositoryFactory_ShouldThrowArgumentNullException()
    {
        // Arrange
        var repository = CreateRepository();
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => service.SetupSynchronizer(repository.WorkingDirectory, (Func<IGitRepository, IGitRepositoryWithRemote>)null!));
    }

    [Fact]
    public void SetupSynchronizer_WhenFactoryReturnsNull_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var repository = CreateRepository();
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => service.SetupSynchronizer(repository.WorkingDirectory, _ => (GitRepositorySynchronizer)null!));
        Assert.Throws<InvalidOperationException>(() => service.SetupSynchronizer(repository.WorkingDirectory, _ => (IGitRepositoryWithRemote)null!));
    }

    [Fact]
    public async Task SetupSynchronizerAsync_WhenRepositoryDoesNotExist_InvokesCloneAsyncAndSetsUpSynchronizer()
    {
        // Arrange
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);
        var repoPath = Path.Combine(_testRoot, "cloned-repo");
        var cloneInvoked = false;

        // Act
        var synchronizer = await service.SetupSynchronizerAsync(
            repoPath,
            (path, ct) =>
            {
                cloneInvoked = true;
                GitRepository.Init(path);
                return Task.CompletedTask;
            },
            repo => repo.CreateSynchronizer(new GitCliSyncOptions()));

        // Assert
        Assert.True(cloneInvoked);
        Assert.NotNull(synchronizer);
        Assert.Same(synchronizer, service.GetSynchronizerByPath(repoPath));

        await synchronizer.DisposeAsync();
    }

    [Fact]
    public async Task SetupSynchronizerAsync_WhenEmptyDirectoryExists_InvokesCloneAsyncAndSetsUpSynchronizer()
    {
        // Arrange
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);
        var repoPath = Path.Combine(_testRoot, "empty-dir");
        Directory.CreateDirectory(repoPath);
        var cloneInvoked = false;

        // Act
        var synchronizer = await service.SetupSynchronizerAsync(
            repoPath,
            (path, ct) =>
            {
                cloneInvoked = true;
                GitRepository.Init(path);
                return Task.CompletedTask;
            },
            repo => repo.CreateSynchronizer(new GitCliSyncOptions()));

        // Assert
        Assert.True(cloneInvoked);
        Assert.NotNull(synchronizer);
        Assert.Same(synchronizer, service.GetSynchronizerByPath(repoPath));

        await synchronizer.DisposeAsync();
    }

    [Fact]
    public async Task SetupSynchronizerAsync_WhenRepositoryAlreadyExists_DoesNotInvokeCloneAsync()
    {
        // Arrange
        var repository = CreateRepository();
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);
        var cloneInvoked = false;

        // Act
        var synchronizer = await service.SetupSynchronizerAsync(
            repository.WorkingDirectory,
            (path, ct) =>
            {
                cloneInvoked = true;
                return Task.CompletedTask;
            },
            repo => repo.CreateSynchronizer(new GitCliSyncOptions()));

        // Assert
        Assert.False(cloneInvoked);
        Assert.NotNull(synchronizer);
        Assert.Same(synchronizer, service.GetSynchronizerByPath(repository.WorkingDirectory));

        await synchronizer.DisposeAsync();
    }

    [Fact]
    public async Task SetupSynchronizerAsync_WhenDirectoryExistsAndIsNotEmptyAndNotGitRepo_ThrowsInvalidOperationException()
    {
        // Arrange
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);
        var dirPath = Path.Combine(_testRoot, "non-empty-dir");
        Directory.CreateDirectory(dirPath);
        File.WriteAllText(Path.Combine(dirPath, "somefile.txt"), "hello");
        var cloneInvoked = false;

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetupSynchronizerAsync(
            dirPath,
            (path, ct) =>
            {
                cloneInvoked = true;
                return Task.CompletedTask;
            },
            repo => repo.CreateSynchronizer(new GitCliSyncOptions())));

        Assert.False(cloneInvoked);
    }

    [Fact]
    public async Task SetupSynchronizerAsync_WithNullArguments_ThrowsArgumentNullException()
    {
        // Arrange
        var repository = CreateRepository();
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.SetupSynchronizerAsync(
            repository.WorkingDirectory,
            null!,
            repo => repo.CreateSynchronizer(new GitCliSyncOptions())));

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.SetupSynchronizerAsync(
            repository.WorkingDirectory,
            (path, ct) => Task.CompletedTask,
            (Func<IGitRepository, GitRepositorySynchronizer>)null!));

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.SetupSynchronizerAsync(
            repository.WorkingDirectory,
            (path, ct) => Task.CompletedTask,
            (Func<IGitRepository, IGitRepositoryWithRemote>)null!));
    }

    [Fact]
    public async Task SetupSynchronizerAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.SetupSynchronizerAsync(
            Path.Combine(_testRoot, "cancelled-repo"),
            (path, ct) => Task.CompletedTask,
            repo => repo.CreateSynchronizer(new GitCliSyncOptions()),
            cts.Token));
    }

    [Fact]
    public async Task SetupSynchronizerAsync_WithRemoteRepositoryFactory_ShouldCreateAndCacheSynchronizer()
    {
        // Arrange
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);
        var repoPath = Path.Combine(_testRoot, "remote-factory-repo");
        var options = new GitSyncOptions { Branch = "main" };

        // Act
        var synchronizer = await service.SetupSynchronizerAsync(
            repoPath,
            (path, ct) =>
            {
                GitRepository.Init(path);
                return Task.CompletedTask;
            },
            repo => new FakeRemoteRepository { RootPath = repo.RootPath },
            options);

        // Assert
        Assert.NotNull(synchronizer);
        Assert.Same(synchronizer, service.GetSynchronizerByPath(repoPath));

        await synchronizer.DisposeAsync();
    }

    [Fact]
    public void SetupSynchronizer_WithNonExistentPath_ShouldThrowDirectoryNotFoundException()
    {
        // Arrange
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);
        var nonExistentPath = Path.Combine(_testRoot, "non-existent");

        // Act & Assert
        Assert.Throws<DirectoryNotFoundException>(() => service.SetupSynchronizer(nonExistentPath, repo => repo.CreateSynchronizer(new GitCliSyncOptions())));
    }

    [Fact]
    public void GetSynchronizerByPath_WithNonExistentPath_ShouldThrowDirectoryNotFoundException()
    {
        // Arrange
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);
        var nonExistentPath = Path.Combine(_testRoot, "non-existent");

        // Act & Assert
        Assert.Throws<DirectoryNotFoundException>(() => service.GetSynchronizerByPath(nonExistentPath));
    }

    [Fact]
    public void InvalidateSynchronizer_ShouldRemoveAndDisposeCachedSynchronizer()
    {
        // Arrange
        var repository = CreateRepository();
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);
        service.SetupSynchronizer(repository.WorkingDirectory, repo => repo.CreateSynchronizer(new GitCliSyncOptions()));

        // Act
        service.InvalidateSynchronizer(repository.WorkingDirectory);

        // Assert
        Assert.Null(service.GetSynchronizerByPath(repository.WorkingDirectory));
    }

    [Fact]
    public void InvalidateSynchronizer_WithNoCachedSynchronizer_ShouldNotThrow()
    {
        // Arrange
        var repository = CreateRepository();
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);

        // Act & Assert (should not throw)
        service.InvalidateSynchronizer(repository.WorkingDirectory);
    }

    [Fact]
    public void InvalidateSynchronizer_WithNullOrWhitespacePath_ShouldNotThrow()
    {
        // Arrange
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);

        // Act & Assert (should not throw)
        service.InvalidateSynchronizer(null!);
        service.InvalidateSynchronizer("");
        service.InvalidateSynchronizer("   ");
    }

    [Fact]
    public void Constructor_WithNullRepositoryService_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new GitRepositorySynchronizerService(null!));
    }

    [Fact]
    public async Task DisposeAsync_ShouldDisposeAllCachedSynchronizersAndClearCache()
    {
        // Arrange
        var repository1 = CreateRepository();
        var repository2 = CreateRepository();
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);
        var synchronizer1 = service.SetupSynchronizer(repository1.WorkingDirectory, repo => repo.CreateSynchronizer(new GitCliSyncOptions()));
        var synchronizer2 = service.SetupSynchronizer(repository2.WorkingDirectory, repo => repo.CreateSynchronizer(new GitCliSyncOptions()));

        // Act
        await service.DisposeAsync();

        // Assert
        Assert.Null(service.GetSynchronizerByPath(repository1.WorkingDirectory));
        Assert.Null(service.GetSynchronizerByPath(repository2.WorkingDirectory));

        synchronizer1.NotifyLocalChange();
        synchronizer2.NotifyLocalChange();

        await synchronizer1.DisposeAsync();
        await synchronizer2.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_WithNoCachedSynchronizers_ShouldNotThrow()
    {
        // Arrange
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);

        // Act & Assert (should not throw)
        await service.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_ShouldNotThrow()
    {
        // Arrange
        var repository = CreateRepository();
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);
        service.SetupSynchronizer(repository.WorkingDirectory, repo => repo.CreateSynchronizer(new GitCliSyncOptions()));

        // Act
        await service.DisposeAsync();

        // Assert (should not throw)
        await service.DisposeAsync();
    }

    private sealed class FakeRemoteRepository : IGitRepositoryWithRemote
    {
        public string RootPath { get; set; } = string.Empty;

        public Task FetchAsync(string? remote = null, string? branch = null, bool prune = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<GitMergeResult> PullAsync(string? remote = null, string? branch = null, bool rebase = false, CancellationToken cancellationToken = default) => Task.FromResult(new GitMergeResult(true, Array.Empty<string>()));
        public Task PushAsync(string? remote = null, string? branch = null, bool force = false, bool setUpstream = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<GitMergeResult> MergeAsync(string branch, CancellationToken cancellationToken = default) => Task.FromResult(new GitMergeResult(true, Array.Empty<string>()));
        public Task<bool> IsMergeInProgressAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<IReadOnlyList<string>> GetConflictedFilesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task ResolveConflictAsync(string relativeFilePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ContinueMergeAsync(string? commitMessage = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AbortMergeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<GitTrackingStatus> GetTrackingStatusAsync(string? branch = null, CancellationToken cancellationToken = default) => Task.FromResult(new GitTrackingStatus("main", "origin/main", 0, 0));
        public Task<bool> IsCommitPushedAsync(string commitHash, string? remoteBranch = null, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> IsCommitPushedAsync(GitHash commitHash, string? remoteBranch = null, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    public void Dispose()
    {
        foreach (var repository in _repositories)
        {
            repository.Dispose();
        }
        TestHelper.TryDeleteDirectory(_testRoot);
    }
}
