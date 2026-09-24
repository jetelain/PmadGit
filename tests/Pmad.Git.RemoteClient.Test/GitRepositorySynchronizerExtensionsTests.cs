using Pmad.Git.LocalRepositories;
using Pmad.Git.RemoteClient;

namespace Pmad.Git.RemoteClient.Test;

/// <summary>
/// Unit tests for <see cref="GitRepositorySynchronizerExtensions.CreateSynchronizer"/>.
/// </summary>
public sealed class GitRepositorySynchronizerExtensionsTests : IDisposable
{
    private readonly string _repoPath;
    private readonly GitRepository _repository;

    public GitRepositorySynchronizerExtensionsTests()
    {
        _repoPath = Path.Combine(Path.GetTempPath(), $"remote-sync-ext-test-{Guid.NewGuid():N}");
        _repository = GitRepository.Init(_repoPath);
    }

    public void Dispose()
    {
        TestHelper.TryDeleteDirectory(_repoPath);
    }

    [Fact]
    public void CreateSynchronizer_NullRepository_ThrowsArgumentNullException()
    {
        var options = new GitRemoteClientSyncOptions("http://localhost/test.git");
        Assert.Throws<ArgumentNullException>(() => ((IGitRepository)null!).CreateSynchronizer(options));
    }

    [Fact]
    public void CreateSynchronizer_NullOptions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _repository.CreateSynchronizer(null!));
    }

    [Fact]
    public async Task CreateSynchronizer_Starts_By_Default()
    {
        var options = new GitRemoteClientSyncOptions("http://localhost/test.git");

        await using var synchronizer = _repository.CreateSynchronizer(options);

        Assert.Equal(GitSyncState.Idle, synchronizer.State);

        // Starting again through Start() must be a no-op (idempotent), proving Start() was
        // already called by CreateSynchronizer.
        synchronizer.Start();
    }

    [Fact]
    public async Task CreateSynchronizer_With_Start_False_Does_Not_Start_Periodic_Loop()
    {
        var options = new GitRemoteClientSyncOptions("http://localhost/test.git");

        await using var synchronizer = _repository.CreateSynchronizer(options, start: false);

        Assert.Equal(GitSyncState.Idle, synchronizer.State);
    }

    [Fact]
    public async Task CreateSynchronizer_WithCredentials_ConfiguresClientOptionsCredentials()
    {
        var credentials = GitHttpCredentials.Basic("user", "pass");
        var options = new GitRemoteClientSyncOptions("http://localhost/test.git", credentials);

        await using var synchronizer = _repository.CreateSynchronizer(options, start: false);

        Assert.Equal(GitSyncState.Idle, synchronizer.State);
    }

    [Fact]
    public async Task CreateSynchronizer_DoesNotMutateCallerClientOptions()
    {
        var callerClientOptions = new GitRemoteClientOptions();
        var credentials = GitHttpCredentials.Basic("user", "pass");
        var options = new GitRemoteClientSyncOptions("http://localhost/test.git", credentials)
        {
            ClientOptions = callerClientOptions
        };

        await using var synchronizer = _repository.CreateSynchronizer(options, start: false);

        // The caller's instance must not be modified as a side-effect
        Assert.Null(callerClientOptions.Credentials);
    }

    [Fact]
    public void GitRemoteClientSyncOptions_Constructors_And_Properties()
    {
        var defaultOptions = new GitRemoteClientSyncOptions();
        Assert.Null(defaultOptions.Url);
        Assert.Null(defaultOptions.RemoteUrl);
        Assert.Null(defaultOptions.Credentials);
        Assert.Null(defaultOptions.ClientOptions);

        var credentials = GitHttpCredentials.Basic("user", "token");
        var customOptions = new GitRemoteClientSyncOptions("https://example.com/repo.git", credentials)
        {
            Remote = "upstream",
            Branch = "develop",
            PushDebounceDelay = TimeSpan.FromSeconds(10),
            PullInterval = TimeSpan.FromMinutes(2)
        };

        Assert.Equal("https://example.com/repo.git", customOptions.Url);
        Assert.Equal("https://example.com/repo.git", customOptions.RemoteUrl);
        Assert.Equal("upstream", customOptions.Remote);
        Assert.Equal("develop", customOptions.Branch);
        Assert.Equal(TimeSpan.FromSeconds(10), customOptions.PushDebounceDelay);
        Assert.Equal(TimeSpan.FromMinutes(2), customOptions.PullInterval);
        Assert.Same(credentials, customOptions.Credentials);

        customOptions.RemoteUrl = "https://example.com/other.git";
        Assert.Equal("https://example.com/other.git", customOptions.Url);
        Assert.Equal("https://example.com/other.git", customOptions.RemoteUrl);

        var copied = new GitRemoteClientSyncOptions(customOptions);
        Assert.Equal("https://example.com/other.git", copied.Url);
        Assert.Equal("upstream", copied.Remote);
        Assert.Equal("develop", copied.Branch);
        Assert.Equal(TimeSpan.FromSeconds(10), copied.PushDebounceDelay);
        Assert.Equal(TimeSpan.FromMinutes(2), copied.PullInterval);
        Assert.Same(credentials, copied.Credentials);
    }

    [Fact]
    public void GitRemoteClientSyncOptions_NullOptionsInConstructor_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new GitRemoteClientSyncOptions(null!));
    }
}
