using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pmad.Git.HttpServer;

namespace Pmad.Git.HttpServer.Test;

public sealed class GitSmartHttpOptionsCopyToTest
{
    [Fact]
    public void CopyTo_CopiesRepositoryRoot()
    {
        var source = new GitSmartHttpOptions { RepositoryRoot = "/repos/myrepo" };
        var target = new GitSmartHttpOptions();

        source.CopyTo(target);

        Assert.Equal("/repos/myrepo", target.RepositoryRoot);
    }

    [Fact]
    public void CopyTo_CopiesEnableUploadPack_WhenFalse()
    {
        var source = new GitSmartHttpOptions { EnableUploadPack = false };
        var target = new GitSmartHttpOptions();

        source.CopyTo(target);

        Assert.False(target.EnableUploadPack);
    }

    [Fact]
    public void CopyTo_CopiesEnableReceivePack_WhenTrue()
    {
        var source = new GitSmartHttpOptions { EnableReceivePack = true };
        var target = new GitSmartHttpOptions();

        source.CopyTo(target);

        Assert.True(target.EnableReceivePack);
    }

    [Fact]
    public void CopyTo_CopiesAgent()
    {
        var source = new GitSmartHttpOptions { Agent = "MyServer/2.0" };
        var target = new GitSmartHttpOptions();

        source.CopyTo(target);

        Assert.Equal("MyServer/2.0", target.Agent);
    }

    [Fact]
    public void CopyTo_CopiesAuthorizeAsync()
    {
        Func<HttpContext, string, GitOperation, CancellationToken, ValueTask<bool>> callback =
            (_, _, _, _) => ValueTask.FromResult(true);
        var source = new GitSmartHttpOptions { AuthorizeAsync = callback };
        var target = new GitSmartHttpOptions();

        source.CopyTo(target);

        Assert.Same(callback, target.AuthorizeAsync);
    }

    [Fact]
    public void CopyTo_CopiesNullAuthorizeAsync()
    {
        var source = new GitSmartHttpOptions { AuthorizeAsync = null };
        var target = new GitSmartHttpOptions();

        source.CopyTo(target);

        Assert.Null(target.AuthorizeAsync);
    }

    [Fact]
    public void CopyTo_CopiesRepositoryNameNormalizer()
    {
        Func<string, string> normalizer = name => name.ToUpperInvariant();
        var source = new GitSmartHttpOptions { RepositoryNameNormalizer = normalizer };
        var target = new GitSmartHttpOptions();

        source.CopyTo(target);

        Assert.Same(normalizer, target.RepositoryNameNormalizer);
    }

    [Fact]
    public void CopyTo_CopiesRepositoryNameValidator()
    {
        Func<string, bool> validator = _ => true;
        var source = new GitSmartHttpOptions { RepositoryNameValidator = validator };
        var target = new GitSmartHttpOptions();

        source.CopyTo(target);

        Assert.Same(validator, target.RepositoryNameValidator);
    }

    [Fact]
    public void CopyTo_CopiesRepositoryResolver()
    {
        Func<HttpContext, string?> resolver = _ => "custom-repo";
        var source = new GitSmartHttpOptions { RepositoryResolver = resolver };
        var target = new GitSmartHttpOptions();

        source.CopyTo(target);

        Assert.Same(resolver, target.RepositoryResolver);
    }

    [Fact]
    public void CopyTo_CopiesOnReceivePackCompleted()
    {
        Func<HttpContext, string, IReadOnlyList<string>, ValueTask> callback =
            (_, _, _) => ValueTask.CompletedTask;
        var source = new GitSmartHttpOptions { OnReceivePackCompleted = callback };
        var target = new GitSmartHttpOptions();

        source.CopyTo(target);

        Assert.Same(callback, target.OnReceivePackCompleted);
    }

    [Fact]
    public void CopyTo_CopiesAllPropertiesAtOnce()
    {
        Func<HttpContext, string, GitOperation, CancellationToken, ValueTask<bool>> authorize =
            (_, _, _, _) => ValueTask.FromResult(true);
        Func<string, string> normalizer = name => name;
        Func<string, bool> validator = _ => true;
        Func<HttpContext, string?> resolver = _ => "repo";
        Func<HttpContext, string, IReadOnlyList<string>, ValueTask> onCompleted =
            (_, _, _) => ValueTask.CompletedTask;

        var source = new GitSmartHttpOptions
        {
            RepositoryRoot = "/repos",
            EnableUploadPack = false,
            EnableReceivePack = true,
            Agent = "TestAgent/1.0",
            AuthorizeAsync = authorize,
            RepositoryNameNormalizer = normalizer,
            RepositoryNameValidator = validator,
            RepositoryResolver = resolver,
            OnReceivePackCompleted = onCompleted
        };
        var target = new GitSmartHttpOptions();

        source.CopyTo(target);

        Assert.Equal("/repos", target.RepositoryRoot);
        Assert.False(target.EnableUploadPack);
        Assert.True(target.EnableReceivePack);
        Assert.Equal("TestAgent/1.0", target.Agent);
        Assert.Same(authorize, target.AuthorizeAsync);
        Assert.Same(normalizer, target.RepositoryNameNormalizer);
        Assert.Same(validator, target.RepositoryNameValidator);
        Assert.Same(resolver, target.RepositoryResolver);
        Assert.Same(onCompleted, target.OnReceivePackCompleted);
    }

    [Fact]
    public void AddGitSmartHttp_WithOptionsOverload_CopiesAllPropertiesToRegisteredOptions()
    {
        Func<HttpContext, string, GitOperation, CancellationToken, ValueTask<bool>> authorize =
            (_, _, _, _) => ValueTask.FromResult(true);
        Func<string, string> normalizer = name => name;
        Func<string, bool> validator = _ => true;
        Func<HttpContext, string?> resolver = _ => "repo";
        Func<HttpContext, string, IReadOnlyList<string>, ValueTask> onCompleted =
            (_, _, _) => ValueTask.CompletedTask;

        var source = new GitSmartHttpOptions
        {
            RepositoryRoot = Path.GetTempPath(),
            EnableUploadPack = false,
            EnableReceivePack = true,
            Agent = "TestAgent/1.0",
            AuthorizeAsync = authorize,
            RepositoryNameNormalizer = normalizer,
            RepositoryNameValidator = validator,
            RepositoryResolver = resolver,
            OnReceivePackCompleted = onCompleted
        };

        var services = new ServiceCollection();
        services.AddGitSmartHttp(source);
        var provider = services.BuildServiceProvider();

        var registered = provider.GetRequiredService<IOptions<GitSmartHttpOptions>>().Value;

        Assert.Equal(source.RepositoryRoot, registered.RepositoryRoot);
        Assert.Equal(source.EnableUploadPack, registered.EnableUploadPack);
        Assert.Equal(source.EnableReceivePack, registered.EnableReceivePack);
        Assert.Equal(source.Agent, registered.Agent);
        Assert.Same(source.AuthorizeAsync, registered.AuthorizeAsync);
        Assert.Same(source.RepositoryNameNormalizer, registered.RepositoryNameNormalizer);
        Assert.Same(source.RepositoryNameValidator, registered.RepositoryNameValidator);
        Assert.Same(source.RepositoryResolver, registered.RepositoryResolver);
        Assert.Same(source.OnReceivePackCompleted, registered.OnReceivePackCompleted);
    }
}
