using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Pmad.Git.HttpServer;
using Pmad.Git.LocalRepositories;
using Pmad.Git.Protocol;
using Pmad.Git.Protocol.Pack;
using System.Diagnostics;
using System.Text;

namespace Pmad.Git.HttpServer.Test;

public sealed class GitSmartHttpServiceTest : IDisposable
{
    private readonly string _serverRepoRoot;
    private readonly string _testRepoPath;

    public GitSmartHttpServiceTest()
    {
        _serverRepoRoot = Path.Combine(Path.GetTempPath(), "PmadGitSmartHttpServiceTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_serverRepoRoot);
        _testRepoPath = Path.Combine(_serverRepoRoot, "test-repo.git");
        CreateBareRepository(_testRepoPath);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullOptions_ShouldThrowArgumentNullException()
    {
        var repositoryService = new GitRepositoryService();
        Assert.Throws<ArgumentNullException>(() => new GitSmartHttpService(null!, repositoryService));
    }

    [Fact]
    public void Constructor_WithNullRepositoryService_ShouldThrowArgumentNullException()
    {
        var options = Options.Create(new GitSmartHttpOptions { RepositoryRoot = _serverRepoRoot });
        Assert.Throws<ArgumentNullException>(() => new GitSmartHttpService(options, null!));
    }

    [Fact]
    public void Constructor_WithEmptyRepositoryRoot_ShouldThrowArgumentException()
    {
        var options = Options.Create(new GitSmartHttpOptions { RepositoryRoot = "" });
        var repositoryService = new GitRepositoryService();
        Assert.Throws<ArgumentException>(() => new GitSmartHttpService(options, repositoryService));
    }

    [Fact]
    public void Constructor_WithWhitespaceRepositoryRoot_ShouldThrowArgumentException()
    {
        var options = Options.Create(new GitSmartHttpOptions { RepositoryRoot = "   " });
        var repositoryService = new GitRepositoryService();
        Assert.Throws<ArgumentException>(() => new GitSmartHttpService(options, repositoryService));
    }

    [Fact]
    public void Constructor_WithValidOptions_ShouldNotThrow()
    {
        var options = Options.Create(new GitSmartHttpOptions { RepositoryRoot = _serverRepoRoot });
        var repositoryService = new GitRepositoryService();
        var service = new GitSmartHttpService(options, repositoryService);
        Assert.NotNull(service);
    }

    #endregion

    #region HandleInfoRefsAsync Tests

    [Fact]
    public async Task HandleInfoRefsAsync_WithMissingServiceParameter_ShouldReturn400()
    {
        // Arrange
        var service = CreateService();
        var context = CreateHttpContext("/test-repo.git/info/refs", repository: "test-repo");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleInfoRefsAsync_WithUnsupportedService_ShouldReturn400()
    {
        // Arrange
        var service = CreateService();
        var context = CreateHttpContext("/test-repo.git/info/refs?service=git-unsupported", repository: "test-repo");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleInfoRefsAsync_WithDisabledUploadPack_ShouldReturn403()
    {
        // Arrange
        var options = Options.Create(new GitSmartHttpOptions
        {
            RepositoryRoot = _serverRepoRoot,
            EnableUploadPack = false
        });
        var repositoryService = new GitRepositoryService();
        var service = new GitSmartHttpService(options, repositoryService);
        var context = CreateHttpContext("/test-repo.git/info/refs?service=git-upload-pack", repository: "test-repo");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleInfoRefsAsync_WithDisabledReceivePack_ShouldReturn403()
    {
        // Arrange
        var options = Options.Create(new GitSmartHttpOptions
        {
            RepositoryRoot = _serverRepoRoot,
            EnableReceivePack = false
        });
        var repositoryService = new GitRepositoryService();
        var service = new GitSmartHttpService(options, repositoryService);
        var context = CreateHttpContext("/test-repo.git/info/refs?service=git-receive-pack", repository: "test-repo");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleInfoRefsAsync_WithMissingRepository_ShouldReturn404()
    {
        // Arrange
        var service = CreateService();
        var context = CreateHttpContext("/missing-repo.git/info/refs?service=git-upload-pack", repository: "missing-repo");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleInfoRefsAsync_WithInvalidRepositoryName_ShouldReturn400()
    {
        // Arrange
        var service = CreateService();
        var context = CreateHttpContext("/invalid/../repo.git/info/refs?service=git-upload-pack", repository: "invalid/../repo");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleInfoRefsAsync_WithValidRequest_ShouldReturn200()
    {
        // Arrange
        var service = CreateService();
        var context = CreateHttpContext("/test-repo.git/info/refs?service=git-upload-pack", repository: "test-repo");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Contains("application/x-git-upload-pack-advertisement", context.Response.ContentType);
    }

    [Fact]
    public async Task HandleInfoRefsAsync_WithAuthorizationDenied_ShouldReturn403()
    {
        // Arrange
        var options = Options.Create(new GitSmartHttpOptions
        {
            RepositoryRoot = _serverRepoRoot,
            AuthorizeAsync = (ctx, repo, op, token) => ValueTask.FromResult(false)
        });
        var repositoryService = new GitRepositoryService();
        var service = new GitSmartHttpService(options, repositoryService);
        var context = CreateHttpContext("/test-repo.git/info/refs?service=git-upload-pack", repository: "test-repo");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleInfoRefsAsync_WithEmptyRepositoryName_ShouldReturn404()
    {
        // Arrange
        var service = CreateService();
        var context = CreateHttpContext("/info/refs?service=git-upload-pack", repository: "");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleInfoRefsAsync_ShouldNotRaiseChanged()
    {
        // Arrange: HandleInfoRefsAsync only refreshes caches to advertise the latest refs;
        // this is a read-only refresh and must not be mistaken for a real repository change
        // (which would otherwise cause a synchronizer to schedule a spurious push).
        var options = Options.Create(new GitSmartHttpOptions
        {
            RepositoryRoot = _serverRepoRoot,
            EnableUploadPack = true,
            EnableReceivePack = true,
            AuthorizeAsync = (_, _, _, _) => ValueTask.FromResult(true)
        });
        var repositoryService = new GitRepositoryService();
        var service = new GitSmartHttpService(options, repositoryService);
        var repository = repositoryService.GetRepositoryByPath(_testRepoPath);

        var changedRaised = false;
        repository.Changed += (_, _) => changedRaised = true;

        var context = CreateHttpContext("/test-repo.git/info/refs?service=git-upload-pack", repository: "test-repo");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.False(changedRaised);
    }

    #endregion

    #region HandleUploadPackAsync Tests

    [Fact]
    public async Task HandleUploadPackAsync_WithDisabledService_ShouldReturn403()
    {
        // Arrange
        var options = Options.Create(new GitSmartHttpOptions
        {
            RepositoryRoot = _serverRepoRoot,
            EnableUploadPack = false
        });
        var repositoryService = new GitRepositoryService();
        var service = new GitSmartHttpService(options, repositoryService);
        var context = CreateHttpContext("/test-repo.git/git-upload-pack", repository: "test-repo");

        // Act
        await service.HandleUploadPackAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleUploadPackAsync_WithMissingRepository_ShouldReturn404()
    {
        // Arrange
        var service = CreateService();
        var context = CreateHttpContext("/missing-repo.git/git-upload-pack", repository: "missing-repo");

        // Act
        await service.HandleUploadPackAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleUploadPackAsync_WithNoWants_ShouldReturn400()
    {
        // Arrange
        var service = CreateService();
        var context = CreateHttpContext("/test-repo.git/git-upload-pack", repository: "test-repo");
        context.Request.Body = new MemoryStream(); // Empty body

        // Act
        await service.HandleUploadPackAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    #endregion

    #region HandleReceivePackAsync Tests

    [Fact]
    public async Task HandleReceivePackAsync_WithDisabledService_ShouldReturn403()
    {
        // Arrange
        var options = Options.Create(new GitSmartHttpOptions
        {
            RepositoryRoot = _serverRepoRoot,
            EnableReceivePack = false
        });
        var repositoryService = new GitRepositoryService();
        var service = new GitSmartHttpService(options, repositoryService);
        var context = CreateHttpContext("/test-repo.git/git-receive-pack", repository: "test-repo");

        // Act
        await service.HandleReceivePackAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleReceivePackAsync_WithMissingRepository_ShouldReturn404()
    {
        // Arrange
        var options = Options.Create(new GitSmartHttpOptions
        {
            RepositoryRoot = _serverRepoRoot,
            EnableReceivePack = true,
            AuthorizeAsync = (_, _, _, _) => ValueTask.FromResult(true) // Allow all operations for this test
        });
        var repositoryService = new GitRepositoryService();
        var service = new GitSmartHttpService(options, repositoryService);
        var context = CreateHttpContext("/missing-repo.git/git-receive-pack", repository: "missing-repo");

        // Act
        await service.HandleReceivePackAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    #endregion

    #region Repository Name Normalization Tests

    [Fact]
    public async Task HandleInfoRefsAsync_WithDotGitSuffix_ShouldNormalize()
    {
        // Arrange
        var service = CreateService();
        var context = CreateHttpContext("/test-repo.git/info/refs?service=git-upload-pack", repository: "test-repo.git");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleInfoRefsAsync_WithSlashesInName_ShouldNormalize()
    {
        // Arrange: Create a nested repository
        var nestedPath = Path.Combine(_serverRepoRoot, "group", "nested-repo.git");
        CreateBareRepository(nestedPath);

        var service = CreateService();
        var context = CreateHttpContext("/group/nested-repo.git/info/refs?service=git-upload-pack", repository: "group/nested-repo");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleInfoRefsAsync_WithDoubleDotAttack_ShouldReturn400()
    {
        // Arrange
        var service = CreateService();
        var context = CreateHttpContext("/../../etc/passwd/info/refs?service=git-upload-pack", repository: "../../etc/passwd");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleInfoRefsAsync_WithPathTraversalAttempt_ShouldReturn400()
    {
        // Arrange
        var service = CreateService();
        var context = CreateHttpContext("/test/../../../etc/hosts.git/info/refs?service=git-upload-pack", repository: "test/../../../etc/hosts");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    #endregion

    #region Custom Repository Normalizer Tests

    [Fact]
    public async Task HandleInfoRefsAsync_WithCustomNormalizer_ShouldUseIt()
    {
        // Arrange
        var normalizedName = "normalized-repo";
        var actualRepoPath = Path.Combine(_serverRepoRoot, normalizedName + ".git");
        CreateBareRepository(actualRepoPath);

        var options = Options.Create(new GitSmartHttpOptions
        {
            RepositoryRoot = _serverRepoRoot,
            RepositoryNameNormalizer = name => normalizedName
        });
        var repositoryService = new GitRepositoryService();
        var service = new GitSmartHttpService(options, repositoryService);
        var context = CreateHttpContext("/any-name.git/info/refs?service=git-upload-pack", repository: "any-name");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    #endregion

    #region Authorization Tests

    [Fact]
    public async Task HandleInfoRefsAsync_WithAsyncAuthorization_ShouldAwait()
    {
        // Arrange
        var authCalled = false;
        var options = Options.Create(new GitSmartHttpOptions
        {
            RepositoryRoot = _serverRepoRoot,
            AuthorizeAsync = async (ctx, repo, op, token) =>
            {
                await Task.Delay(10, token);
                authCalled = true;
                return true;
            }
        });
        var repositoryService = new GitRepositoryService();
        var service = new GitSmartHttpService(options, repositoryService);
        var context = CreateHttpContext("/test-repo.git/info/refs?service=git-upload-pack", repository: "test-repo");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.True(authCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleInfoRefsAsync_WithAuthorizationException_ShouldReturn403()
    {
        // Arrange
        var options = Options.Create(new GitSmartHttpOptions
        {
            RepositoryRoot = _serverRepoRoot,
            AuthorizeAsync = (ctx, repo, op, token) => throw new UnauthorizedAccessException()
        });
        var repositoryService = new GitRepositoryService();
        var service = new GitSmartHttpService(options, repositoryService);
        var context = CreateHttpContext("/test-repo.git/info/refs?service=git-upload-pack", repository: "test-repo");

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            async () => await service.HandleInfoRefsAsync(context));
    }

    [Fact]
    public async Task HandleInfoRefsAsync_WithReadOperation_ShouldPassReadToAuthorization()
    {
        // Arrange
        GitOperation? capturedOperation = null;
        var options = Options.Create(new GitSmartHttpOptions
        {
            RepositoryRoot = _serverRepoRoot,
            AuthorizeAsync = (ctx, repo, op, token) =>
            {
                capturedOperation = op;
                return ValueTask.FromResult(true);
            }
        });
        var repositoryService = new GitRepositoryService();
        var service = new GitSmartHttpService(options, repositoryService);
        var context = CreateHttpContext("/test-repo.git/info/refs?service=git-upload-pack", repository: "test-repo");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(GitOperation.Read, capturedOperation);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleInfoRefsAsync_WithWriteOperation_ShouldPassWriteToAuthorization()
    {
        // Arrange
        GitOperation? capturedOperation = null;
        var options = Options.Create(new GitSmartHttpOptions
        {
            RepositoryRoot = _serverRepoRoot,
            EnableReceivePack = true,
            AuthorizeAsync = (ctx, repo, op, token) =>
            {
                capturedOperation = op;
                return ValueTask.FromResult(true);
            }
        });
        var repositoryService = new GitRepositoryService();
        var service = new GitSmartHttpService(options, repositoryService);
        var context = CreateHttpContext("/test-repo.git/info/refs?service=git-receive-pack", repository: "test-repo");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(GitOperation.Write, capturedOperation);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleUploadPackAsync_ShouldPassReadToAuthorization()
    {
        // Arrange
        GitOperation? capturedOperation = null;
        var options = Options.Create(new GitSmartHttpOptions
        {
            RepositoryRoot = _serverRepoRoot,
            EnableUploadPack = true,
            AuthorizeAsync = (ctx, repo, op, token) =>
            {
                capturedOperation = op;
                return ValueTask.FromResult(true);
            }
        });
        var repositoryService = new GitRepositoryService();
        var service = new GitSmartHttpService(options, repositoryService);
        var context = CreateHttpContext("/test-repo.git/git-upload-pack", repository: "test-repo");
        context.Request.Body = new MemoryStream();

        // Act
        await service.HandleUploadPackAsync(context);

        // Assert
        Assert.Equal(GitOperation.Read, capturedOperation);
    }

    [Fact]
    public async Task HandleReceivePackAsync_ShouldPassWriteToAuthorization()
    {
        // Arrange
        GitOperation? capturedOperation = null;
        var options = Options.Create(new GitSmartHttpOptions
        {
            RepositoryRoot = _serverRepoRoot,
            EnableReceivePack = true,
            AuthorizeAsync = (ctx, repo, op, token) =>
            {
                capturedOperation = op;
                return ValueTask.FromResult(true);
            }
        });
        var repositoryService = new GitRepositoryService();
        var service = new GitSmartHttpService(options, repositoryService);
        var context = CreateHttpContext("/test-repo.git/git-receive-pack", repository: "test-repo");
        var flushPacket = new byte[] { 0x30, 0x30, 0x30, 0x30 };
        context.Request.Body = new MemoryStream(flushPacket);

        // Act
        await service.HandleReceivePackAsync(context);

        // Assert
        Assert.Equal(GitOperation.Write, capturedOperation);
    }

    [Fact]
    public async Task AuthorizeAsync_CanDenyWriteWhileAllowingRead()
    {
        // Arrange
        var options = Options.Create(new GitSmartHttpOptions
        {
            RepositoryRoot = _serverRepoRoot,
            EnableReceivePack = true,
            AuthorizeAsync = (ctx, repo, op, token) =>
            {
                // Allow read but deny write
                return ValueTask.FromResult(op == GitOperation.Read);
            }
        });
        var repositoryService = new GitRepositoryService();
        var service = new GitSmartHttpService(options, repositoryService);

        // Act & Assert - Read should succeed
        var readContext = CreateHttpContext("/test-repo.git/info/refs?service=git-upload-pack", repository: "test-repo");
        await service.HandleInfoRefsAsync(readContext);
        Assert.Equal(StatusCodes.Status200OK, readContext.Response.StatusCode);

        // Act & Assert - Write should be denied
        var writeContext = CreateHttpContext("/test-repo.git/info/refs?service=git-receive-pack", repository: "test-repo");
        await service.HandleInfoRefsAsync(writeContext);
        Assert.Equal(StatusCodes.Status403Forbidden, writeContext.Response.StatusCode);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public async Task HandleInfoRefsAsync_WithBackslashesInName_ShouldNormalize()
    {
        // Arrange
        var service = CreateService();
        var context = CreateHttpContext("/test-repo.git/info/refs?service=git-upload-pack", repository: "test\\repo");

        // Note: This should fail because backslashes get normalized to forward slashes
        // and the directory doesn't exist

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleInfoRefsAsync_WithEmptyQueryString_ShouldReturn400()
    {
        // Arrange
        var service = CreateService();
        var context = CreateHttpContext("/test-repo.git/info/refs", repository: "test-repo");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleInfoRefsAsync_WithCancellationToken_ShouldRespectCancellation()
    {
        // Arrange
        var service = CreateService();
        var context = CreateHttpContext("/test-repo.git/info/refs?service=git-upload-pack", repository: "test-repo");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await service.HandleInfoRefsAsync(context, cts.Token));
    }

    [Fact]
    public async Task HandleInfoRefsAsync_WithRepoWithoutDotGit_ShouldFindWithDotGit()
    {
        // Arrange
        var service = CreateService();
        var context = CreateHttpContext("/test-repo/info/refs?service=git-upload-pack", repository: "test-repo");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleInfoRefsAsync_WithNullRepositoryRouteValue_ShouldReturn404()
    {
        // Arrange
        var service = CreateService();
        var context = CreateHttpContext("/info/refs?service=git-upload-pack");
        context.Request.RouteValues["repository"] = null!;

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    #endregion

    #region OnReceivePackCompleted Callback Tests

    [Fact]
    public async Task HandleReceivePackAsync_WithNoUpdates_ShouldNotInvokeCallback()
    {
        // Arrange
        var callbackInvoked = false;
        string? capturedRepositoryName = null;
        IReadOnlyList<string>? capturedUpdatedRefs = null;

        var options = Options.Create(new GitSmartHttpOptions
        {
            RepositoryRoot = _serverRepoRoot,
            EnableReceivePack = true,
            AuthorizeAsync = (_, _, _, _) => ValueTask.FromResult(true), // Allow all operations for this test
            OnReceivePackCompleted = (ctx, repoName, updatedRefs) =>
            {
                callbackInvoked = true;
                capturedRepositoryName = repoName;
                capturedUpdatedRefs = updatedRefs;
                return ValueTask.CompletedTask;
            }
        });
        var repositoryService = new GitRepositoryService();
        var service = new GitSmartHttpService(options, repositoryService);
        var context = CreateHttpContext("/test-repo.git/git-receive-pack", repository: "test-repo");

        // Create a flush packet with no updates
        var flushPacket = new byte[] { 0x30, 0x30, 0x30, 0x30 }; // "0000"
        context.Request.Body = new MemoryStream(flushPacket);

        // Act
        await service.HandleReceivePackAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        // No updates means callback should not be invoked
        Assert.False(callbackInvoked);
    }

    [Fact]
    public async Task HandleReceivePackAsync_WithoutCallback_ShouldSucceed()
    {
        // Arrange
        var options = Options.Create(new GitSmartHttpOptions
        {
            RepositoryRoot = _serverRepoRoot,
            EnableReceivePack = true,
            AuthorizeAsync = (_, _, _, _) => ValueTask.FromResult(true), // Allow all operations for this test
            OnReceivePackCompleted = null // No callback
        });
        var repositoryService = new GitRepositoryService();
        var service = new GitSmartHttpService(options, repositoryService);
        var context = CreateHttpContext("/test-repo.git/git-receive-pack", repository: "test-repo");

        var flushPacket = new byte[] { 0x30, 0x30, 0x30, 0x30 };
        context.Request.Body = new MemoryStream(flushPacket);

        // Act
        await service.HandleReceivePackAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleReceivePackAsync_WithNoUpdates_ShouldNotRaiseChanged()
    {
        // Arrange: a receive-pack request with no ref updates (flush only) should not raise
        // Changed, since no actual repository modification occurred.
        var options = Options.Create(new GitSmartHttpOptions
        {
            RepositoryRoot = _serverRepoRoot,
            EnableReceivePack = true,
            AuthorizeAsync = (_, _, _, _) => ValueTask.FromResult(true)
        });
        var repositoryService = new GitRepositoryService();
        var service = new GitSmartHttpService(options, repositoryService);
        var repository = repositoryService.GetRepositoryByPath(_testRepoPath);

        var changedRaised = false;
        repository.Changed += (_, _) => changedRaised = true;

        var context = CreateHttpContext("/test-repo.git/git-receive-pack", repository: "test-repo");
        var flushPacket = new byte[] { 0x30, 0x30, 0x30, 0x30 }; // "0000"
        context.Request.Body = new MemoryStream(flushPacket);

        // Act
        await service.HandleReceivePackAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.False(changedRaised);
    }

    // Note: Actual callback invocation with reference updates (success, partial success, exceptions, timing)
    // is tested in GitSmartHttpEndToEndTest with real git push operations.

    #endregion

    #region Upload-Pack Incremental Fetch (Have Negotiation) Tests

    [Fact]
    public async Task HandleUploadPackAsync_WithHaves_NegotiatesAndSendsOnlyIncrementalObjects()
    {
        // Arrange
        // Add two commits to the test repository: commit 1 and commit 2
        var tempWorkDir = Path.Combine(Path.GetTempPath(), "temp-work-negotiation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempWorkDir);
        string commit1, commit2;
        try
        {
            RunGitInDirectory(tempWorkDir, "init --quiet --initial-branch=main");
            RunGitInDirectory(tempWorkDir, "config user.name \"Test\"");
            RunGitInDirectory(tempWorkDir, "config user.email test@test.com");

            File.WriteAllText(Path.Combine(tempWorkDir, "file1.txt"), "content 1");
            RunGitInDirectory(tempWorkDir, "add file1.txt");
            RunGitInDirectory(tempWorkDir, "commit -m \"Commit 1\" --quiet");
            commit1 = TestHelper.RunGit(tempWorkDir, "rev-parse HEAD").Trim();

            File.WriteAllText(Path.Combine(tempWorkDir, "file2.txt"), "content 2");
            RunGitInDirectory(tempWorkDir, "add file2.txt");
            RunGitInDirectory(tempWorkDir, "commit -m \"Commit 2\" --quiet");
            commit2 = TestHelper.RunGit(tempWorkDir, "rev-parse HEAD").Trim();

            RunGitInDirectory(tempWorkDir, $"remote add origin \"{_testRepoPath}\"");
            RunGitInDirectory(tempWorkDir, "push -u origin main --quiet");
        }
        finally
        {
            TestHelper.TryDeleteDirectory(tempWorkDir);
        }

        var service = CreateService();
        var context = CreateHttpContext("/test-repo.git/git-upload-pack", repository: "test-repo");

        // Client wants commit 2, has commit 1, with multi_ack_detailed
        var requestStream = new MemoryStream();
        await PktLineWriter.WriteStringAsync(requestStream, $"want {commit2} multi_ack_detailed\n", CancellationToken.None);
        await PktLineWriter.WriteFlushAsync(requestStream, CancellationToken.None);
        await PktLineWriter.WriteStringAsync(requestStream, $"have {commit1}\n", CancellationToken.None);
        await PktLineWriter.WriteStringAsync(requestStream, "done\n", CancellationToken.None);
        requestStream.Position = 0;
        context.Request.Body = requestStream;

        // Act
        await service.HandleUploadPackAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        context.Response.Body.Position = 0;

        // Verify response contains ACK for commit1, not NAK
        var reader = new PktLineReader(context.Response.Body);
        var ackCommonPacket = await reader.ReadAsync(CancellationToken.None);
        Assert.NotNull(ackCommonPacket);
        Assert.Equal($"ACK {commit1} common\n", ackCommonPacket.Value.AsString());

        var terminalAckPacket = await reader.ReadAsync(CancellationToken.None);
        Assert.NotNull(terminalAckPacket);
        Assert.Equal($"ACK {commit1}\n", terminalAckPacket.Value.AsString());

        // Verify the packfile immediately follows the ACKs and has count = 3 (commit2, tree2, file2 blob)
        // PACK header: "PACK" (4 bytes) + version (4 bytes, uint32 BE) + object count (4 bytes, uint32 BE)
        var packHeader = new byte[12];
        var bytesRead = await context.Response.Body.ReadAsync(packHeader, 0, 12);
        Assert.Equal(12, bytesRead);
        Assert.Equal("PACK", Encoding.ASCII.GetString(packHeader, 0, 4));
        var objectCount = (packHeader[8] << 24) | (packHeader[9] << 16) | (packHeader[10] << 8) | packHeader[11];
        Assert.Equal(3, objectCount); // Only 3 incremental objects, NOT full history (which would be 6)
    }

    [Fact]
    public async Task HandleUploadPackAsync_WithMultiAck_RespondsWithAckContinue()
    {
        var tempWorkDir = Path.Combine(Path.GetTempPath(), "temp-work-multiack", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempWorkDir);
        string commit1, commit2;
        try
        {
            RunGitInDirectory(tempWorkDir, "init --quiet --initial-branch=main");
            RunGitInDirectory(tempWorkDir, "config user.name \"Test\"");
            RunGitInDirectory(tempWorkDir, "config user.email test@test.com");

            File.WriteAllText(Path.Combine(tempWorkDir, "file1.txt"), "content 1");
            RunGitInDirectory(tempWorkDir, "add file1.txt");
            RunGitInDirectory(tempWorkDir, "commit -m \"Commit 1\" --quiet");
            commit1 = TestHelper.RunGit(tempWorkDir, "rev-parse HEAD").Trim();

            File.WriteAllText(Path.Combine(tempWorkDir, "file2.txt"), "content 2");
            RunGitInDirectory(tempWorkDir, "add file2.txt");
            RunGitInDirectory(tempWorkDir, "commit -m \"Commit 2\" --quiet");
            commit2 = TestHelper.RunGit(tempWorkDir, "rev-parse HEAD").Trim();

            RunGitInDirectory(tempWorkDir, $"remote add origin \"{_testRepoPath}\"");
            RunGitInDirectory(tempWorkDir, "push -u origin main --quiet");
        }
        finally
        {
            TestHelper.TryDeleteDirectory(tempWorkDir);
        }

        var service = CreateService();
        var context = CreateHttpContext("/test-repo.git/git-upload-pack", repository: "test-repo");

        var requestStream = new MemoryStream();
        await PktLineWriter.WriteStringAsync(requestStream, $"want {commit2} multi_ack\n", CancellationToken.None);
        await PktLineWriter.WriteFlushAsync(requestStream, CancellationToken.None);
        await PktLineWriter.WriteStringAsync(requestStream, $"have {commit1}\n", CancellationToken.None);
        await PktLineWriter.WriteStringAsync(requestStream, "done\n", CancellationToken.None);
        requestStream.Position = 0;
        context.Request.Body = requestStream;

        // Act
        await service.HandleUploadPackAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        context.Response.Body.Position = 0;

        var reader = new PktLineReader(context.Response.Body);
        var ackContinuePacket = await reader.ReadAsync(CancellationToken.None);
        Assert.NotNull(ackContinuePacket);
        Assert.Equal($"ACK {commit1} continue\n", ackContinuePacket.Value.AsString());

        var terminalAckPacket = await reader.ReadAsync(CancellationToken.None);
        Assert.NotNull(terminalAckPacket);
        Assert.Equal($"ACK {commit1}\n", terminalAckPacket.Value.AsString());
    }

    [Fact]
    public async Task HandleUploadPackAsync_WithoutMultiAck_RespondsWithSingleAck()
    {
        var tempWorkDir = Path.Combine(Path.GetTempPath(), "temp-work-singleack", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempWorkDir);
        string commit1, commit2;
        try
        {
            RunGitInDirectory(tempWorkDir, "init --quiet --initial-branch=main");
            RunGitInDirectory(tempWorkDir, "config user.name \"Test\"");
            RunGitInDirectory(tempWorkDir, "config user.email test@test.com");

            File.WriteAllText(Path.Combine(tempWorkDir, "file1.txt"), "content 1");
            RunGitInDirectory(tempWorkDir, "add file1.txt");
            RunGitInDirectory(tempWorkDir, "commit -m \"Commit 1\" --quiet");
            commit1 = TestHelper.RunGit(tempWorkDir, "rev-parse HEAD").Trim();

            File.WriteAllText(Path.Combine(tempWorkDir, "file2.txt"), "content 2");
            RunGitInDirectory(tempWorkDir, "add file2.txt");
            RunGitInDirectory(tempWorkDir, "commit -m \"Commit 2\" --quiet");
            commit2 = TestHelper.RunGit(tempWorkDir, "rev-parse HEAD").Trim();

            RunGitInDirectory(tempWorkDir, $"remote add origin \"{_testRepoPath}\"");
            RunGitInDirectory(tempWorkDir, "push -u origin main --quiet");
        }
        finally
        {
            TestHelper.TryDeleteDirectory(tempWorkDir);
        }

        var service = CreateService();
        var context = CreateHttpContext("/test-repo.git/git-upload-pack", repository: "test-repo");

        var requestStream = new MemoryStream();
        await PktLineWriter.WriteStringAsync(requestStream, $"want {commit2}\n", CancellationToken.None);
        await PktLineWriter.WriteFlushAsync(requestStream, CancellationToken.None);
        await PktLineWriter.WriteStringAsync(requestStream, $"have {commit1}\n", CancellationToken.None);
        await PktLineWriter.WriteStringAsync(requestStream, "done\n", CancellationToken.None);
        requestStream.Position = 0;
        context.Request.Body = requestStream;

        // Act
        await service.HandleUploadPackAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        context.Response.Body.Position = 0;

        var reader = new PktLineReader(context.Response.Body);
        var ackPacket = await reader.ReadAsync(CancellationToken.None);
        Assert.NotNull(ackPacket);
        Assert.Equal($"ACK {commit1}\n", ackPacket.Value.AsString());
    }

    [Fact]
    public async Task HandleUploadPackAsync_WithUnknownHaves_RespondsWithNak()
    {
        var tempWorkDir = Path.Combine(Path.GetTempPath(), "temp-work-nak", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempWorkDir);
        string commit1;
        try
        {
            RunGitInDirectory(tempWorkDir, "init --quiet --initial-branch=main");
            RunGitInDirectory(tempWorkDir, "config user.name \"Test\"");
            RunGitInDirectory(tempWorkDir, "config user.email test@test.com");

            File.WriteAllText(Path.Combine(tempWorkDir, "file1.txt"), "content 1");
            RunGitInDirectory(tempWorkDir, "add file1.txt");
            RunGitInDirectory(tempWorkDir, "commit -m \"Commit 1\" --quiet");
            commit1 = TestHelper.RunGit(tempWorkDir, "rev-parse HEAD").Trim();

            RunGitInDirectory(tempWorkDir, $"remote add origin \"{_testRepoPath}\"");
            RunGitInDirectory(tempWorkDir, "push -u origin main --quiet");
        }
        finally
        {
            TestHelper.TryDeleteDirectory(tempWorkDir);
        }

        var service = CreateService();
        var context = CreateHttpContext("/test-repo.git/git-upload-pack", repository: "test-repo");

        var unknownHave = new string('1', 40);
        var requestStream = new MemoryStream();
        await PktLineWriter.WriteStringAsync(requestStream, $"want {commit1} multi_ack_detailed\n", CancellationToken.None);
        await PktLineWriter.WriteFlushAsync(requestStream, CancellationToken.None);
        await PktLineWriter.WriteStringAsync(requestStream, $"have {unknownHave}\n", CancellationToken.None);
        await PktLineWriter.WriteStringAsync(requestStream, "done\n", CancellationToken.None);
        requestStream.Position = 0;
        context.Request.Body = requestStream;

        // Act
        await service.HandleUploadPackAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        context.Response.Body.Position = 0;

        var reader = new PktLineReader(context.Response.Body);
        var nakPacket = await reader.ReadAsync(CancellationToken.None);
        Assert.NotNull(nakPacket);
        Assert.Equal("NAK\n", nakPacket.Value.AsString());
    }

    [Fact]
    public async Task HandleUploadPackAsync_WithDelimiterPacket_ParsesSuccessfully()
    {
        var tempWorkDir = Path.Combine(Path.GetTempPath(), "temp-work-delim", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempWorkDir);
        string commit1;
        try
        {
            RunGitInDirectory(tempWorkDir, "init --quiet --initial-branch=main");
            RunGitInDirectory(tempWorkDir, "config user.name \"Test\"");
            RunGitInDirectory(tempWorkDir, "config user.email test@test.com");

            File.WriteAllText(Path.Combine(tempWorkDir, "file1.txt"), "content 1");
            RunGitInDirectory(tempWorkDir, "add file1.txt");
            RunGitInDirectory(tempWorkDir, "commit -m \"Commit 1\" --quiet");
            commit1 = TestHelper.RunGit(tempWorkDir, "rev-parse HEAD").Trim();

            RunGitInDirectory(tempWorkDir, $"remote add origin \"{_testRepoPath}\"");
            RunGitInDirectory(tempWorkDir, "push -u origin main --quiet");
        }
        finally
        {
            TestHelper.TryDeleteDirectory(tempWorkDir);
        }

        var service = CreateService();
        var context = CreateHttpContext("/test-repo.git/git-upload-pack", repository: "test-repo");

        var requestStream = new MemoryStream();
        await PktLineWriter.WriteStringAsync(requestStream, $"want {commit1} multi_ack_detailed\n", CancellationToken.None);
        await PktLineWriter.WriteDelimiterAsync(requestStream, CancellationToken.None);
        await PktLineWriter.WriteStringAsync(requestStream, "done\n", CancellationToken.None);
        requestStream.Position = 0;
        context.Request.Body = requestStream;

        // Act
        await service.HandleUploadPackAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleReceivePackAsync_WithInvalidRefPrefix_ThrowsInvalidOperationException()
    {
        var repositoryService = new GitRepositoryService();
        var repo = repositoryService.GetRepositoryByPath(_testRepoPath);
        var service = CreateService();
        var context = CreateHttpContext("/test-repo.git/git-receive-pack", repository: "test-repo");

        var zeroHash = new string('0', 40);
        var newHash = new string('1', 40);
        var stream = new MemoryStream();
        await PktLineWriter.WriteStringAsync(stream, $"{zeroHash} {newHash} invalid-ref\0report-status\n", CancellationToken.None);
        await PktLineWriter.WriteFlushAsync(stream, CancellationToken.None);
        await new GitPackBuilder().WriteAsync(repo, Array.Empty<GitHash>(), stream, CancellationToken.None);
        stream.Position = 0;
        context.Request.Body = stream;

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.HandleReceivePackAsync(context));
        Assert.Contains("References must reside under refs/", ex.Message);
    }

    [Fact]
    public async Task HandleReceivePackAsync_WhenRefAlreadyExistsOnCreation_ReturnsNgStatus()
    {
        var repositoryService = new GitRepositoryService();
        var repo = repositoryService.GetRepositoryByPath(_testRepoPath);
        var commitData = Encoding.UTF8.GetBytes("tree 4b825dc642cb6eb9a060e54bf8d69288fbee4904\nauthor Test <t@t.com> 0 +0000\ncommitter Test <t@t.com> 0 +0000\n\nInitial\n");
        var commitHash = await repo.ObjectStore.WriteObjectAsync(GitObjectType.Commit, commitData, CancellationToken.None);
        await repo.ReferenceStore.CreateReferenceAsync("refs/heads/main", commitHash, overwrite: true, CancellationToken.None);

        var service = CreateService();
        var context = CreateHttpContext("/test-repo.git/git-receive-pack", repository: "test-repo");

        var zeroHash = new string('0', 40);
        var newHash = new string('1', 40);
        var stream = new MemoryStream();
        await PktLineWriter.WriteStringAsync(stream, $"{zeroHash} {newHash} refs/heads/main\0report-status\n", CancellationToken.None);
        await PktLineWriter.WriteFlushAsync(stream, CancellationToken.None);
        await new GitPackBuilder().WriteAsync(repo, Array.Empty<GitHash>(), stream, CancellationToken.None);
        stream.Position = 0;
        context.Request.Body = stream;

        // Act
        await service.HandleReceivePackAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        context.Response.Body.Position = 0;

        var reader = new PktLineReader(context.Response.Body);
        var unpackPacket = await reader.ReadAsync(CancellationToken.None);
        Assert.NotNull(unpackPacket);
        Assert.Equal("unpack ok\n", unpackPacket.Value.AsString());

        var statusPacket = await reader.ReadAsync(CancellationToken.None);
        Assert.NotNull(statusPacket);
        Assert.Equal("ng refs/heads/main reference exists\n", statusPacket.Value.AsString());
    }

    [Fact]
    public async Task HandleReceivePackAsync_WhenOldValueMismatches_ReturnsNgNonFastForward()
    {
        var repositoryService = new GitRepositoryService();
        var repo = repositoryService.GetRepositoryByPath(_testRepoPath);
        var commitData = Encoding.UTF8.GetBytes("tree 4b825dc642cb6eb9a060e54bf8d69288fbee4904\nauthor Test <t@t.com> 0 +0000\ncommitter Test <t@t.com> 0 +0000\n\nInitial\n");
        var commitHash = await repo.ObjectStore.WriteObjectAsync(GitObjectType.Commit, commitData, CancellationToken.None);
        await repo.ReferenceStore.CreateReferenceAsync("refs/heads/main", commitHash, overwrite: true, CancellationToken.None);

        var service = CreateService();
        var context = CreateHttpContext("/test-repo.git/git-receive-pack", repository: "test-repo");

        var wrongOldHash = new string('2', 40);
        var newHash = new string('3', 40);
        var stream = new MemoryStream();
        await PktLineWriter.WriteStringAsync(stream, $"{wrongOldHash} {newHash} refs/heads/main\0report-status\n", CancellationToken.None);
        await PktLineWriter.WriteFlushAsync(stream, CancellationToken.None);
        await new GitPackBuilder().WriteAsync(repo, Array.Empty<GitHash>(), stream, CancellationToken.None);
        stream.Position = 0;
        context.Request.Body = stream;

        // Act
        await service.HandleReceivePackAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        context.Response.Body.Position = 0;

        var reader = new PktLineReader(context.Response.Body);
        var unpackPacket = await reader.ReadAsync(CancellationToken.None);
        Assert.NotNull(unpackPacket);
        Assert.Equal("unpack ok\n", unpackPacket.Value.AsString());

        var statusPacket = await reader.ReadAsync(CancellationToken.None);
        Assert.NotNull(statusPacket);
        Assert.Equal("ng refs/heads/main non-fast-forward\n", statusPacket.Value.AsString());
    }

    [Fact]
    public async Task HandleReceivePackAsync_WithCorruptPackfile_ReturnsUnpackError()
    {
        var service = CreateService();
        var context = CreateHttpContext("/test-repo.git/git-receive-pack", repository: "test-repo");

        var zeroHash = new string('0', 40);
        var newHash = new string('1', 40);
        var stream = new MemoryStream();
        await PktLineWriter.WriteStringAsync(stream, $"{zeroHash} {newHash} refs/heads/feature\0report-status\n", CancellationToken.None);
        await PktLineWriter.WriteFlushAsync(stream, CancellationToken.None);
        stream.Write(new byte[] { 0xFF, 0xFE, 0xFD, 0xFC, 0xFB, 0xFA, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 });
        stream.Position = 0;
        context.Request.Body = stream;

        // Act
        await service.HandleReceivePackAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        context.Response.Body.Position = 0;

        var reader = new PktLineReader(context.Response.Body);
        var unpackPacket = await reader.ReadAsync(CancellationToken.None);
        Assert.NotNull(unpackPacket);
        Assert.StartsWith("unpack error", unpackPacket.Value.AsString());
    }

    #endregion

    #region Path Traversal and Device Name Tests

    [Fact]
    public void ResolveRepositoryPath_WithSiblingDirectory_ThrowsDirectoryNotFoundException()
    {
        // Sibling directory named e.g. <_serverRepoRoot>-secret
        var siblingDir = _serverRepoRoot.TrimEnd(Path.DirectorySeparatorChar) + "-secret";
        Directory.CreateDirectory(siblingDir);
        try
        {
            var service = CreateService();
            // Attempt to resolve sibling directory
            var siblingName = "../" + Path.GetFileName(siblingDir);
            Assert.Throws<DirectoryNotFoundException>(() => service.ResolveRepositoryPath(siblingName));
        }
        finally
        {
            TestHelper.TryDeleteDirectory(siblingDir);
        }
    }

    [Fact]
    public async Task HandleInfoRefsAsync_WithPathTraversalSibling_Returns400()
    {
        var service = CreateService();
        var siblingRepo = _serverRepoRoot.TrimEnd(Path.DirectorySeparatorChar) + "-secret";
        var context = CreateHttpContext("/" + Path.GetFileName(siblingRepo) + ".git/info/refs?service=git-upload-pack", repository: "../" + Path.GetFileName(siblingRepo));

        await service.HandleInfoRefsAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("AUX")]
    [InlineData("NUL")]
    [InlineData("PRN")]
    [InlineData("COM1")]
    [InlineData("LPT1")]
    public async Task HandleInfoRefsAsync_WithWindowsDeviceName_Returns400(string deviceName)
    {
        var service = CreateService();
        var context = CreateHttpContext($"/{deviceName}.git/info/refs?service=git-upload-pack", repository: deviceName);

        await service.HandleInfoRefsAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    #endregion

    #region Content-Type Enforcement Tests

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("text/plain")]
    [InlineData("application/json")]
    [InlineData("application/x-git-receive-pack-request")] // Wrong service
    public async Task HandleUploadPackAsync_WithInvalidContentType_Returns415(string? contentType)
    {
        var service = CreateService();
        var context = CreateHttpContext("/test-repo.git/git-upload-pack", repository: "test-repo", contentType: contentType);

        await service.HandleUploadPackAsync(context);

        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleUploadPackAsync_WithContentTypeParameters_SucceedsValidation()
    {
        var service = CreateService();
        var context = CreateHttpContext("/test-repo.git/git-upload-pack", repository: "test-repo", contentType: "application/x-git-upload-pack-request; charset=utf-8");
        context.Request.Body = new MemoryStream(); // Empty body will fail at wants parsing (400), showing Content-Type passed

        await service.HandleUploadPackAsync(context);

        // Passed content-type check, failed at wants check (400) rather than 415
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("text/plain")]
    [InlineData("application/json")]
    [InlineData("application/x-git-upload-pack-request")] // Wrong service
    public async Task HandleReceivePackAsync_WithInvalidContentType_Returns415(string? contentType)
    {
        var service = CreateService();
        var context = CreateHttpContext("/test-repo.git/git-receive-pack", repository: "test-repo", contentType: contentType);

        await service.HandleReceivePackAsync(context);

        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleReceivePackAsync_WithContentTypeParameters_SucceedsValidation()
    {
        var service = CreateService();
        var context = CreateHttpContext("/test-repo.git/git-receive-pack", repository: "test-repo", contentType: "application/x-git-receive-pack-request; charset=utf-8");
        context.Request.Body = new MemoryStream(new byte[] { 0x30, 0x30, 0x30, 0x30 }); // Flush packet

        await service.HandleReceivePackAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    #endregion

    #region Report-Status and Cancellation Tests

    [Fact]
    public async Task HandleReceivePackAsync_WithoutReportStatus_DoesNotWriteStatusReport()
    {
        var service = CreateService();
        var context = CreateHttpContext("/test-repo.git/git-receive-pack", repository: "test-repo");
        // Flush packet without report-status capability
        context.Request.Body = new MemoryStream(new byte[] { 0x30, 0x30, 0x30, 0x30 });

        await service.HandleReceivePackAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length); // Nothing written when report-status is not negotiated
    }

    [Fact]
    public async Task HandleReceivePackAsync_WhenClientCancels_PropagatesOperationCanceledException()
    {
        var service = CreateService();
        var context = CreateHttpContext("/test-repo.git/git-receive-pack", repository: "test-repo");

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancelled token

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.HandleReceivePackAsync(context, cts.Token));
        Assert.Equal(0, context.Response.Body.Length);
    }

    [Fact]
    public async Task HandleReceivePackAsync_WithOnReceivePackCompleted_ReceivesDetachedContext()
    {
        HttpContext? capturedContext = null;
        string? capturedRepo = null;
        IReadOnlyList<string>? capturedRefs = null;
        var tcs = new TaskCompletionSource<bool>();

        var options = Options.Create(new GitSmartHttpOptions
        {
            RepositoryRoot = _serverRepoRoot,
            EnableReceivePack = true,
            AuthorizeAsync = (_, _, _, _) => ValueTask.FromResult(true),
            OnReceivePackCompleted = (ctx, repo, refs) =>
            {
                capturedContext = ctx;
                capturedRepo = repo;
                capturedRefs = refs;
                tcs.SetResult(true);
                return ValueTask.CompletedTask;
            }
        });
        var repositoryService = new GitRepositoryService();
        var service = new GitSmartHttpService(options, repositoryService);

        var context = CreateHttpContext("/test-repo.git/git-receive-pack", repository: "test-repo");
        context.Request.Headers["X-Custom-Test"] = "CustomValue";

        // Create a commit and reference first, then delete it via receive-pack (requires no pack data)
        var repo = repositoryService.GetRepositoryByPath(_testRepoPath);
        var zeroHash = new string('0', repo.HashLengthBytes * 2);
        var commitData = Encoding.UTF8.GetBytes("tree 4b825dc642cb6eb9a060e54bf8d69288fbee4904\nauthor Test <t@t.com> 0 +0000\ncommitter Test <t@t.com> 0 +0000\n\nInitial\n");
        var commitHash = await repo.ObjectStore.WriteObjectAsync(GitObjectType.Commit, commitData, CancellationToken.None);
        await repo.ReferenceStore.CreateReferenceAsync("refs/heads/main", commitHash, overwrite: true, CancellationToken.None);

        var stream = new MemoryStream();
        await PktLineWriter.WriteStringAsync(stream, $"{commitHash.Value} {zeroHash} refs/heads/main\0report-status delete-refs\n", CancellationToken.None);
        await PktLineWriter.WriteFlushAsync(stream, CancellationToken.None);
        stream.Position = 0;
        context.Request.Body = stream;

        await service.HandleReceivePackAsync(context);

        // Simulate context recycling immediately after HandleReceivePackAsync completes
        context.Request.Headers.Clear();
        context.User = null!;

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(capturedContext);
        Assert.NotSame(context, capturedContext);
        Assert.Equal("CustomValue", capturedContext.Request.Headers["X-Custom-Test"]);
        Assert.Equal("test-repo", capturedRepo);
        Assert.NotNull(capturedRefs);
        Assert.Single(capturedRefs);
        Assert.Equal("refs/heads/main", capturedRefs[0]);
    }

    #endregion

    #region Helper Methods

    private GitSmartHttpService CreateService()
    {
        var options = Options.Create(new GitSmartHttpOptions
        {
            RepositoryRoot = _serverRepoRoot,
            EnableUploadPack = true,
            EnableReceivePack = true,
            AuthorizeAsync = (_, _, _, _) => ValueTask.FromResult(true) // Allow all operations for test helper
        });
        var repositoryService = new GitRepositoryService();
        return new GitSmartHttpService(options, repositoryService);
    }

    private HttpContext CreateHttpContext(string path, string? repository = null, string? contentType = "__DEFAULT__")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        var isUploadPack = path.Contains("upload-pack");
        var isReceivePack = path.Contains("receive-pack");
        context.Request.Method = isUploadPack || isReceivePack ? "POST" : "GET";
        context.Request.QueryString = new QueryString(path.Contains('?') ? path.Substring(path.IndexOf('?')) : "");

        if (repository != null)
        {
            context.Request.RouteValues["repository"] = repository;
        }

        if (contentType == "__DEFAULT__")
        {
            if (isUploadPack)
            {
                context.Request.ContentType = "application/x-git-upload-pack-request";
            }
            else if (isReceivePack)
            {
                context.Request.ContentType = "application/x-git-receive-pack-request";
            }
        }
        else
        {
            context.Request.ContentType = contentType;
        }

        context.Response.Body = new MemoryStream();
        return context;
    }

    private void CreateBareRepository(string path)
    {
        Directory.CreateDirectory(path);
        RunGitInDirectory(path, "init --bare --quiet --initial-branch=main");
    }

    private void RunGitInDirectory(string workingDirectory, string arguments)
    {
        var startInfo = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start git process");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {arguments} failed with exit code {process.ExitCode}:{Environment.NewLine}{error}");
        }
    }

    public void Dispose()
    {
        TestHelper.TryDeleteDirectory(_serverRepoRoot);
    }

    #endregion
}
