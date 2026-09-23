using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pmad.Git.HttpServer;
using Pmad.Git.LocalRepositories;
using Pmad.Git.RemoteClient;

namespace Pmad.Git.RemoteClient.Test;

public sealed class GitAuthenticationTest : IDisposable
{
    private readonly string _serverRepoRoot;
    private readonly string _clientWorkingDir;
    private IHost? _host;
    private TestServer? _testServer;

    public GitAuthenticationTest()
    {
        _serverRepoRoot = Path.Combine(Path.GetTempPath(), "PmadGitAuthTestServer", Guid.NewGuid().ToString("N"));
        _clientWorkingDir = Path.Combine(Path.GetTempPath(), "PmadGitAuthTestClient", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_serverRepoRoot);
        Directory.CreateDirectory(_clientWorkingDir);
    }

    private async Task StartServerAsync(Func<string?, bool> validateAuth)
        => await StartServerAsync(ctx => validateAuth(ctx.Request.Headers.Authorization.ToString())).ConfigureAwait(false);

    private async Task StartServerAsync(Func<HttpContext, bool> validateRequest)
    {
        var builder = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddGitSmartHttp(options =>
                        {
                            options.RepositoryRoot = _serverRepoRoot;
                            options.EnableUploadPack = true;
                            options.EnableReceivePack = true;
                        });
                    })
                    .Configure(app =>
                    {
                        app.Use(async (context, next) =>
                        {
                            if (!validateRequest(context))
                            {
                                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                                context.Response.Headers.WWWAuthenticate = "Basic realm=\"Git\"";
                                return;
                            }

                            await next().ConfigureAwait(false);
                        });

                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGitSmartHttp("/{repository}.git");
                        });
                    });
            });

        _host = await builder.StartAsync();
        _testServer = _host.GetTestServer();
    }

    private void CreateServerRepository(string name)
    {
        var barePath = Path.Combine(_serverRepoRoot, $"{name}.git");
        using var repo = GitRepositoryWithIndexAndWorkspace.Init(barePath, initialBranch: "main");
        File.WriteAllText(Path.Combine(barePath, "test.txt"), "hello");
        repo.StageAsync("test.txt").GetAwaiter().GetResult();
        repo.CommitAsync("Initial commit").GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Request_WithoutCredentials_WhenAuthRequired_ThrowsGitAuthenticationException()
    {
        CreateServerRepository("auth-repo");
        await StartServerAsync(auth => !string.IsNullOrEmpty(auth));

        var httpClient = _testServer!.CreateClient();
        var options = new GitRemoteClientOptions { HttpClient = httpClient };
        var clientDir = Path.Combine(_clientWorkingDir, "no-creds");

        var ex = await Assert.ThrowsAsync<GitAuthenticationException>(async () =>
        {
            await GitRemoteClientRepository.CloneAsync("http://localhost/auth-repo.git", clientDir, options);
        });

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public async Task Request_WithInvalidCredentials_ThrowsGitAuthenticationException()
    {
        CreateServerRepository("auth-repo");
        var expectedBasic = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("validuser:secret123"));
        await StartServerAsync(auth => auth == expectedBasic);

        var httpClient = _testServer!.CreateClient();
        var options = new GitRemoteClientOptions
        {
            HttpClient = httpClient,
            Credentials = GitHttpCredentials.Basic("validuser", "wrongpassword")
        };
        var clientDir = Path.Combine(_clientWorkingDir, "wrong-creds");

        var ex = await Assert.ThrowsAsync<GitAuthenticationException>(async () =>
        {
            await GitRemoteClientRepository.CloneAsync("http://localhost/auth-repo.git", clientDir, options);
        });

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public async Task Request_WithValidBasicCredentials_Succeeds()
    {
        CreateServerRepository("auth-repo");
        var expectedBasic = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("validuser:secret123"));
        await StartServerAsync(auth => auth == expectedBasic);

        var httpClient = _testServer!.CreateClient();
        var options = new GitRemoteClientOptions
        {
            HttpClient = httpClient,
            Credentials = GitHttpCredentials.Basic("validuser", "secret123")
        };
        var clientDir = Path.Combine(_clientWorkingDir, "valid-basic");

        using var repo = await GitRemoteClientRepository.CloneAsync("http://localhost/auth-repo.git", clientDir, options);
        Assert.NotNull(repo);
        Assert.True(File.Exists(Path.Combine(clientDir, "test.txt")));
        Assert.Equal("hello", File.ReadAllText(Path.Combine(clientDir, "test.txt")));
    }

    [Fact]
    public async Task Request_WithBearerToken_Succeeds()
    {
        CreateServerRepository("auth-repo");
        var expectedBearer = "Bearer token-abc-123";
        await StartServerAsync(auth => auth == expectedBearer);

        var httpClient = _testServer!.CreateClient();
        var options = new GitRemoteClientOptions
        {
            HttpClient = httpClient,
            Credentials = GitHttpCredentials.Bearer("token-abc-123")
        };
        var clientDir = Path.Combine(_clientWorkingDir, "valid-bearer");

        using var repo = await GitRemoteClientRepository.CloneAsync("http://localhost/auth-repo.git", clientDir, options);
        Assert.NotNull(repo);
        Assert.True(File.Exists(Path.Combine(clientDir, "test.txt")));
    }

    [Fact]
    public async Task Request_WithPersonalAccessToken_Succeeds()
    {
        CreateServerRepository("auth-repo");
        var expectedPatAuth = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("token:pat-xyz-789"));
        await StartServerAsync(auth => auth == expectedPatAuth);

        var httpClient = _testServer!.CreateClient();
        var options = new GitRemoteClientOptions
        {
            HttpClient = httpClient,
            Credentials = GitHttpCredentials.PersonalAccessToken("pat-xyz-789")
        };
        var clientDir = Path.Combine(_clientWorkingDir, "valid-pat");

        using var repo = await GitRemoteClientRepository.CloneAsync("http://localhost/auth-repo.git", clientDir, options);
        Assert.NotNull(repo);
        Assert.True(File.Exists(Path.Combine(clientDir, "test.txt")));
    }

    [Fact]
    public async Task Request_WithCustomHeader_Succeeds()
    {
        CreateServerRepository("auth-custom-repo");
        const string customHeaderName = "X-Git-Custom-Token";
        const string customHeaderValue = "secret-token-value-12345";
        await StartServerAsync(ctx =>
            ctx.Request.Headers.TryGetValue(customHeaderName, out var val) && val.ToString() == customHeaderValue);

        var httpClient = _testServer!.CreateClient();
        var options = new GitRemoteClientOptions
        {
            HttpClient = httpClient,
            Credentials = GitHttpCredentials.Custom(customHeaderName, customHeaderValue)
        };
        var clientDir = Path.Combine(_clientWorkingDir, "valid-custom");

        using var repo = await GitRemoteClientRepository.CloneAsync("http://localhost/auth-custom-repo.git", clientDir, options);
        Assert.NotNull(repo);
        Assert.True(File.Exists(Path.Combine(clientDir, "test.txt")));
    }

    [Fact]
    public async Task Request_WithInvalidCustomHeader_ThrowsGitAuthenticationException()
    {
        CreateServerRepository("auth-custom-repo");
        const string customHeaderName = "X-Git-Custom-Token";
        await StartServerAsync(ctx =>
            ctx.Request.Headers.TryGetValue(customHeaderName, out var val) && val.ToString() == "expected-secret");

        var httpClient = _testServer!.CreateClient();
        var options = new GitRemoteClientOptions
        {
            HttpClient = httpClient,
            Credentials = GitHttpCredentials.Custom(customHeaderName, "wrong-secret")
        };
        var clientDir = Path.Combine(_clientWorkingDir, "invalid-custom");

        var ex = await Assert.ThrowsAsync<GitAuthenticationException>(async () =>
        {
            await GitRemoteClientRepository.CloneAsync("http://localhost/auth-custom-repo.git", clientDir, options);
        });

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    public void Dispose()
    {
        _host?.Dispose();
        _testServer?.Dispose();
        TestHelper.TryDeleteDirectory(_serverRepoRoot);
        TestHelper.TryDeleteDirectory(_clientWorkingDir);
    }
}
