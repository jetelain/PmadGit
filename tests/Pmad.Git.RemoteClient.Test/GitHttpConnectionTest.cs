using System.Net;
using System.Text;
using Pmad.Git.LocalRepositories;
using Pmad.Git.Protocol;
using Pmad.Git.RemoteClient;

namespace Pmad.Git.RemoteClient.Test;

public sealed class GitHttpConnectionTest
{
    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, Task<HttpResponseMessage>> Handler { get; set; } = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Handler(request);
    }

    [Fact]
    public async Task DiscoverReferencesAsync_ValidResponse_ParsesReferencesAndCapabilities()
    {
        var handler = new MockHttpMessageHandler
        {
            Handler = async req =>
            {
                Assert.Equal(HttpMethod.Get, req.Method);
                Assert.Contains("/info/refs?service=git-upload-pack", req.RequestUri!.ToString());

                var body = new MemoryStream();
                await PktLineWriter.WriteStringAsync(body, "# service=git-upload-pack\n", CancellationToken.None);
                await PktLineWriter.WriteFlushAsync(body, CancellationToken.None);
                await PktLineWriter.WriteStringAsync(body, "1111111111111111111111111111111111111111 HEAD\0symref=HEAD:refs/heads/main agent=git/2.40.0 multi_ack side-band-64k\n", CancellationToken.None);
                await PktLineWriter.WriteStringAsync(body, "1111111111111111111111111111111111111111 refs/heads/main\n", CancellationToken.None);
                await PktLineWriter.WriteStringAsync(body, "2222222222222222222222222222222222222222 refs/heads/feature\n", CancellationToken.None);
                await PktLineWriter.WriteFlushAsync(body, CancellationToken.None);

                body.Seek(0, SeekOrigin.Begin);
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(body)
                };
                response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-git-upload-pack-advertisement");
                return response;
            }
        };

        var httpClient = new HttpClient(handler);
        using var connection = new GitHttpConnection(new GitRemoteClientOptions { HttpClient = httpClient });

        var ad = await connection.DiscoverReferencesAsync(new Uri("http://localhost/test.git"), "git-upload-pack");

        Assert.Equal("refs/heads/main", ad.HeadSymrefTarget);
        Assert.Equal(new GitHash("1111111111111111111111111111111111111111"), ad.HeadHash);
        Assert.True(ad.Capabilities.Contains("multi_ack"));
        Assert.True(ad.Capabilities.Contains("side-band-64k"));
        Assert.Equal("git/2.40.0", ad.Agent);
        Assert.Equal(3, ad.References.Count);
        Assert.Equal(new GitHash("1111111111111111111111111111111111111111"), ad.References["refs/heads/main"]);
        Assert.Equal(new GitHash("2222222222222222222222222222222222222222"), ad.References["refs/heads/feature"]);
    }

    [Fact]
    public async Task DiscoverReferencesAsync_InvalidContentType_ThrowsGitRemoteException()
    {
        var handler = new MockHttpMessageHandler
        {
            Handler = _ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("invalid")
                };
                response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/html");
                return Task.FromResult(response);
            }
        };

        var httpClient = new HttpClient(handler);
        using var connection = new GitHttpConnection(new GitRemoteClientOptions { HttpClient = httpClient });

        await Assert.ThrowsAsync<GitRemoteException>(async () =>
        {
            await connection.DiscoverReferencesAsync(new Uri("http://localhost/test.git"), "git-upload-pack");
        });
    }

    [Fact]
    public async Task DiscoverReferencesAsync_InvalidHeader_ThrowsGitRemoteException()
    {
        var handler = new MockHttpMessageHandler
        {
            Handler = async _ =>
            {
                var body = new MemoryStream();
                await PktLineWriter.WriteStringAsync(body, "bad header\n", CancellationToken.None);
                await PktLineWriter.WriteFlushAsync(body, CancellationToken.None);
                body.Seek(0, SeekOrigin.Begin);

                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(body)
                };
                response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-git-upload-pack-advertisement");
                return response;
            }
        };

        var httpClient = new HttpClient(handler);
        using var connection = new GitHttpConnection(new GitRemoteClientOptions { HttpClient = httpClient });

        await Assert.ThrowsAsync<GitRemoteException>(async () =>
        {
            await connection.DiscoverReferencesAsync(new Uri("http://localhost/test.git"), "git-upload-pack");
        });
    }

    [Fact]
    public async Task DiscoverReferencesAsync_Http401_ThrowsGitAuthenticationException()
    {
        var handler = new MockHttpMessageHandler
        {
            Handler = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized))
        };

        var httpClient = new HttpClient(handler);
        using var connection = new GitHttpConnection(new GitRemoteClientOptions { HttpClient = httpClient });

        var ex = await Assert.ThrowsAsync<GitAuthenticationException>(async () =>
        {
            await connection.DiscoverReferencesAsync(new Uri("http://localhost/test.git"), "git-upload-pack");
        });

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public async Task DiscoverReferencesAsync_Http403_ThrowsGitAccessDeniedException()
    {
        var handler = new MockHttpMessageHandler
        {
            Handler = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden))
        };

        var httpClient = new HttpClient(handler);
        using var connection = new GitHttpConnection(new GitRemoteClientOptions { HttpClient = httpClient });

        var ex = await Assert.ThrowsAsync<GitAccessDeniedException>(async () =>
        {
            await connection.DiscoverReferencesAsync(new Uri("http://localhost/test.git"), "git-upload-pack");
        });

        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
    }
}
