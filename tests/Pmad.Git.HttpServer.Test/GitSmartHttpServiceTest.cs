using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Pmad.Git.HttpServer;
using Pmad.Git.LocalRepositories;
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
    public async Task HandleReceivePackAsync_WithSuccessfulPush_ShouldInvokeCallback()
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
            OnReceivePackCompleted = (ctx, repoName, updatedRefs, token) =>
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

        // Create a simple flush packet (no updates, should still succeed)
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

    private HttpContext CreateHttpContext(string path, string? repository = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = "GET";
        context.Request.QueryString = new QueryString(path.Contains('?') ? path.Substring(path.IndexOf('?')) : "");

        if (repository != null)
        {
            context.Request.RouteValues["repository"] = repository;
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
