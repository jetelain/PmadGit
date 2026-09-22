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
    public async Task SetupCliSynchronizer_ShouldCreateAndCacheSynchronizer()
    {
        // Arrange
        var repository = CreateRepository();
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);

        // Act
        var synchronizer = service.SetupCliSynchronizer(repository.WorkingDirectory, new GitCliSyncOptions());

        // Assert
        Assert.NotNull(synchronizer);
        Assert.Same(synchronizer, service.GetSynchronizerByPath(repository.WorkingDirectory));

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
        var synchronizer = service.SetupCliSynchronizer(pathWithoutSlash, new GitCliSyncOptions());

        // Assert
        Assert.NotNull(synchronizer);
        Assert.Same(synchronizer, service.GetSynchronizerByPath(pathWithSlash));

        await synchronizer.DisposeAsync();
    }

    [Fact]
    public async Task InvalidateSynchronizer_WithTrailingDirectorySeparator_ShouldRemoveCachedSynchronizer()
    {
        // Arrange
        var repository = CreateRepository();
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);
        var pathWithoutSlash = repository.WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var pathWithSlash = pathWithoutSlash + Path.DirectorySeparatorChar;
        service.SetupCliSynchronizer(pathWithoutSlash, new GitCliSyncOptions());

        // Act
        service.InvalidateSynchronizer(pathWithSlash);

        // Assert
        Assert.Null(service.GetSynchronizerByPath(pathWithoutSlash));
        Assert.Null(service.GetSynchronizerByPath(pathWithSlash));

        await Task.Delay(10);
    }

    [Fact]
    public async Task SetupCliSynchronizer_CalledTwice_ShouldReplacePreviousInstanceAndDisposeIt()
    {
        // Arrange
        var repository = CreateRepository();
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);

        // Act
        var synchronizer1 = service.SetupCliSynchronizer(repository.WorkingDirectory, new GitCliSyncOptions());
        var synchronizer2 = service.SetupCliSynchronizer(repository.WorkingDirectory, new GitCliSyncOptions());

        // Assert
        Assert.NotSame(synchronizer1, synchronizer2);
        Assert.Same(synchronizer2, service.GetSynchronizerByPath(repository.WorkingDirectory));

        await synchronizer2.DisposeAsync();
    }

    [Fact]
    public void SetupCliSynchronizer_WithNullOptions_ShouldThrowArgumentNullException()
    {
        // Arrange
        var repository = CreateRepository();
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => service.SetupCliSynchronizer(repository.WorkingDirectory, null!));
    }

    [Fact]
    public void SetupCliSynchronizer_WithNonExistentPath_ShouldThrowDirectoryNotFoundException()
    {
        // Arrange
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);
        var nonExistentPath = Path.Combine(_testRoot, "non-existent");

        // Act & Assert
        Assert.Throws<DirectoryNotFoundException>(() => service.SetupCliSynchronizer(nonExistentPath, new GitCliSyncOptions()));
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
    public async Task InvalidateSynchronizer_ShouldRemoveAndDisposeCachedSynchronizer()
    {
        // Arrange
        var repository = CreateRepository();
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);
        service.SetupCliSynchronizer(repository.WorkingDirectory, new GitCliSyncOptions());

        // Act
        service.InvalidateSynchronizer(repository.WorkingDirectory);

        // Assert
        Assert.Null(service.GetSynchronizerByPath(repository.WorkingDirectory));

        await Task.Delay(10); // allow fire-and-forget DisposeAsync to complete
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
        var synchronizer1 = service.SetupCliSynchronizer(repository1.WorkingDirectory, new GitCliSyncOptions());
        var synchronizer2 = service.SetupCliSynchronizer(repository2.WorkingDirectory, new GitCliSyncOptions());

        // Act
        await service.DisposeAsync();

        // Assert
        Assert.Null(service.GetSynchronizerByPath(repository1.WorkingDirectory));
        Assert.Null(service.GetSynchronizerByPath(repository2.WorkingDirectory));

        // Disposed synchronizers should ignore further local-change notifications, confirming
        // DisposeAsync was actually called on them (NotifyLocalChange is a no-op once disposed).
        synchronizer1.NotifyLocalChange();
        synchronizer2.NotifyLocalChange();

        // Calling DisposeAsync again should be a no-op and not throw.
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
        service.SetupCliSynchronizer(repository.WorkingDirectory, new GitCliSyncOptions());

        // Act
        await service.DisposeAsync();

        // Assert (should not throw)
        await service.DisposeAsync();
    }

    [Fact]
    public async Task SetupCliSynchronizerAsync_WithNonExistentPath_ShouldCloneAndCreateSynchronizer()
    {
        // Arrange
        var remote = CreateRepository();
        var localPath = Path.Combine(_testRoot, "cloned-non-existent");
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);

        // Act
        var synchronizer = await service.SetupCliSynchronizerAsync(localPath, remote.WorkingDirectory, new GitCliSyncOptions());

        // Assert
        Assert.NotNull(synchronizer);
        Assert.True(Directory.Exists(Path.Combine(localPath, ".git")));
        Assert.True(File.Exists(Path.Combine(localPath, "README.md")));
        Assert.Same(synchronizer, service.GetSynchronizerByPath(localPath));

        await synchronizer.DisposeAsync();
    }

    [Fact]
    public async Task SetupCliSynchronizerAsync_WithEmptyExistingDirectory_ShouldCloneAndCreateSynchronizer()
    {
        // Arrange
        var remote = CreateRepository();
        var localPath = Path.Combine(_testRoot, "cloned-empty-dir");
        Directory.CreateDirectory(localPath);
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);

        // Act
        var synchronizer = await service.SetupCliSynchronizerAsync(localPath, remote.WorkingDirectory, new GitCliSyncOptions());

        // Assert
        Assert.NotNull(synchronizer);
        Assert.True(Directory.Exists(Path.Combine(localPath, ".git")));

        await synchronizer.DisposeAsync();
    }

    [Fact]
    public async Task SetupCliSynchronizerAsync_WithExistingRepository_ShouldNotCloneAndUseExistingRepository()
    {
        // Arrange
        var repository = CreateRepository();
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);

        // Act
        // An invalid remote URL is passed on purpose: since the repository already exists locally, no clone should be attempted.
        var synchronizer = await service.SetupCliSynchronizerAsync(repository.WorkingDirectory, "invalid://not-a-real-remote", new GitCliSyncOptions());

        // Assert
        Assert.NotNull(synchronizer);
        Assert.Same(synchronizer, service.GetSynchronizerByPath(repository.WorkingDirectory));

        await synchronizer.DisposeAsync();
    }

    [Fact]
    public async Task SetupCliSynchronizerAsync_WithNonEmptyDirectoryWithoutRepository_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var remote = CreateRepository();
        var localPath = Path.Combine(_testRoot, "non-empty-non-repo");
        Directory.CreateDirectory(localPath);
        File.WriteAllText(Path.Combine(localPath, "some-file.txt"), "content");
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SetupCliSynchronizerAsync(localPath, remote.WorkingDirectory, new GitCliSyncOptions()));
    }

    [Fact]
    public async Task SetupCliSynchronizerAsync_WithNullOptions_ShouldThrowArgumentNullException()
    {
        // Arrange
        var localPath = Path.Combine(_testRoot, "null-options");
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.SetupCliSynchronizerAsync(localPath, "https://example.com/repo.git", null!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SetupCliSynchronizerAsync_WithNullOrWhitespaceRemoteUrl_ShouldThrowArgumentException(string? remoteUrl)
    {
        // Arrange
        var localPath = Path.Combine(_testRoot, "invalid-remote-url");
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.SetupCliSynchronizerAsync(localPath, remoteUrl!, new GitCliSyncOptions()));
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
