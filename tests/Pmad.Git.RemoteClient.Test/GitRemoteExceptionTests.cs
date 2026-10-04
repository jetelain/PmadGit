using System.Net;
using System.IO;
using System.Net.Http;

namespace Pmad.Git.RemoteClient.Test;

public class GitRemoteExceptionTests
{
    [Fact]
    public void GitRemoteException_DefaultConstructor()
    {
        var ex = new GitRemoteException();
        Assert.NotNull(ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void GitRemoteException_MessageConstructor()
    {
        var ex = new GitRemoteException("test error");
        Assert.Equal("test error", ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void GitRemoteException_InnerExceptionConstructor()
    {
        var inner = new InvalidOperationException("inner error");
        var ex = new GitRemoteException("outer error", inner);
        Assert.Equal("outer error", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void GitAuthenticationException_Properties()
    {
        var exDefault = new GitAuthenticationException("Auth failed");
        Assert.Equal("Auth failed", exDefault.Message);
        Assert.Equal(HttpStatusCode.Unauthorized, exDefault.StatusCode);

        var exCustom = new GitAuthenticationException("Custom auth failed", HttpStatusCode.ProxyAuthenticationRequired);
        Assert.Equal("Custom auth failed", exCustom.Message);
        Assert.Equal(HttpStatusCode.ProxyAuthenticationRequired, exCustom.StatusCode);
    }

    [Fact]
    public void GitAccessDeniedException_Properties()
    {
        var exDefault = new GitAccessDeniedException("Access denied");
        Assert.Equal("Access denied", exDefault.Message);
        Assert.Equal(HttpStatusCode.Forbidden, exDefault.StatusCode);

        var exCustom = new GitAccessDeniedException("Forbidden custom", HttpStatusCode.Unauthorized);
        Assert.Equal("Forbidden custom", exCustom.Message);
        Assert.Equal(HttpStatusCode.Unauthorized, exCustom.StatusCode);
    }

    [Fact]
    public void GitRepositoryNotFoundException_Properties()
    {
        var exDefault = new GitRepositoryNotFoundException("Repo not found");
        Assert.Equal("Repo not found", exDefault.Message);
        Assert.Equal(HttpStatusCode.NotFound, exDefault.StatusCode);

        var exCustom = new GitRepositoryNotFoundException("Custom not found", HttpStatusCode.Gone);
        Assert.Equal("Custom not found", exCustom.Message);
        Assert.Equal(HttpStatusCode.Gone, exCustom.StatusCode);
    }

    [Fact]
    public void GitUploadPackResponse_Dispose_DisposesResources()
    {
        var packStream = new MemoryStream();
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var bodyStream = new MemoryStream();

        var uploadPackResponse = new GitUploadPackResponse(packStream, response, bodyStream);
        Assert.Same(packStream, uploadPackResponse.PackStream);

        uploadPackResponse.Dispose();

        Assert.Throws<ObjectDisposedException>(() => packStream.ReadByte());
        Assert.Throws<ObjectDisposedException>(() => bodyStream.ReadByte());
    }

    [Fact]
    public async Task GitUploadPackResponse_DisposeAsync_DisposesResources()
    {
        var packStream = new MemoryStream();
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var bodyStream = new MemoryStream();

        var uploadPackResponse = new GitUploadPackResponse(packStream, response, bodyStream);
        Assert.Same(packStream, uploadPackResponse.PackStream);

        await uploadPackResponse.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => packStream.ReadByte());
        Assert.Throws<ObjectDisposedException>(() => bodyStream.ReadByte());
    }
}
