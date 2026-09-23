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

    [Fact]
    public async Task DiscoverReferencesAsync_ServerReturnsErrPacket_ThrowsGitRemoteExceptionWithServerMessage()
    {
        var handler = new MockHttpMessageHandler
        {
            Handler = async _ =>
            {
                var body = new MemoryStream();
                await PktLineWriter.WriteStringAsync(body, "ERR Repository not found\n", CancellationToken.None);
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

        var ex = await Assert.ThrowsAsync<GitRemoteException>(async () =>
        {
            await connection.DiscoverReferencesAsync(new Uri("http://localhost/test.git"), "git-upload-pack");
        });

        Assert.Contains("Repository not found", ex.Message);
    }

    [Fact]
    public async Task UploadPackAsync_ServerReturnsErrPacket_ThrowsGitRemoteExceptionWithServerMessage()
    {
        var handler = new MockHttpMessageHandler
        {
            Handler = async _ =>
            {
                var body = new MemoryStream();
                await PktLineWriter.WriteStringAsync(body, "ERR upload-pack: not our ref 1111111111111111111111111111111111111111\n", CancellationToken.None);
                body.Seek(0, SeekOrigin.Begin);
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(body)
                };
                response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-git-upload-pack-result");
                return response;
            }
        };

        var httpClient = new HttpClient(handler);
        using var connection = new GitHttpConnection(new GitRemoteClientOptions { HttpClient = httpClient });

        var ad = new GitRemoteAdvertisement(
            new Dictionary<string, GitHash> { ["refs/heads/main"] = new("1111111111111111111111111111111111111111") },
            new HashSet<string> { "side-band-64k" },
            new Dictionary<string, string>(),
            "refs/heads/main",
            new("1111111111111111111111111111111111111111"),
            GitObjectFormat.Sha1,
            "test");

        var ex = await Assert.ThrowsAsync<GitRemoteException>(async () =>
        {
            await connection.UploadPackAsync(
                new Uri("http://localhost/test.git"),
                new[] { new GitHash("1111111111111111111111111111111111111111") },
                Array.Empty<GitHash>(),
                ad);
        });

        Assert.Contains("not our ref", ex.Message);
    }

    [Fact]
    public async Task UploadPackAsync_WithMultiAckIntermediatePackets_ConsumesIntermediateAcksAndReadsPackStream()
    {
        var handler = new MockHttpMessageHandler
        {
            Handler = async _ =>
            {
                var body = new MemoryStream();
                // Intermediate ACKs
                await PktLineWriter.WriteStringAsync(body, "ACK 1111111111111111111111111111111111111111 continue\n", CancellationToken.None);
                await PktLineWriter.WriteStringAsync(body, "ACK 2222222222222222222222222222222222222222 common\n", CancellationToken.None);
                // Terminal ACK
                await PktLineWriter.WriteStringAsync(body, "ACK 3333333333333333333333333333333333333333\n", CancellationToken.None);

                // Sideband 1: pack payload
                var packPayload = new byte[] { 1, 80, 65, 67, 75 }; // band 1 + "PACK"
                await PktLineWriter.WriteAsync(body, packPayload, CancellationToken.None);
                await PktLineWriter.WriteFlushAsync(body, CancellationToken.None);

                body.Seek(0, SeekOrigin.Begin);
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(body)
                };
                response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-git-upload-pack-result");
                return response;
            }
        };

        var httpClient = new HttpClient(handler);
        using var connection = new GitHttpConnection(new GitRemoteClientOptions { HttpClient = httpClient });

        var ad = new GitRemoteAdvertisement(
            new Dictionary<string, GitHash> { ["refs/heads/main"] = new("3333333333333333333333333333333333333333") },
            new HashSet<string> { "multi_ack", "side-band-64k" },
            new Dictionary<string, string>(),
            "refs/heads/main",
            new("3333333333333333333333333333333333333333"),
            GitObjectFormat.Sha1,
            "test");

        await using var uploadPackResponse = await connection.UploadPackAsync(
            new Uri("http://localhost/test.git"),
            new[] { new GitHash("3333333333333333333333333333333333333333") },
            new[] { new GitHash("1111111111111111111111111111111111111111") },
            ad);

        var buffer = new byte[4];
        var read = await uploadPackResponse.PackStream.ReadAsync(buffer, CancellationToken.None);
        Assert.Equal(4, read);
        Assert.Equal("PACK"u8.ToArray(), buffer);
    }

    [Fact]
    public async Task ReceivePackAsync_WithoutReportStatus_ThrowsGitRemoteException()
    {
        var httpClient = new HttpClient(new MockHttpMessageHandler());
        using var connection = new GitHttpConnection(new GitRemoteClientOptions { HttpClient = httpClient });

        var adWithoutReportStatus = new GitRemoteAdvertisement(
            new Dictionary<string, GitHash>(),
            new HashSet<string> { "delete-refs" }, // No report-status
            new Dictionary<string, string>(),
            null,
            null,
            GitObjectFormat.Sha1,
            "test");

        var cmd = new GitRefUpdateCommand(null, new GitHash("1111111111111111111111111111111111111111"), "refs/heads/main");

        var ex = await Assert.ThrowsAsync<GitRemoteException>(async () =>
        {
            await connection.ReceivePackAsync(
                new Uri("http://localhost/test.git"),
                new[] { cmd },
                null,
                adWithoutReportStatus,
                20);
        });

        Assert.Contains("report-status", ex.Message);
    }

    [Fact]
    public async Task DiscoverReferencesAsync_StalledResponseBody_ThrowsTimeoutException()
    {
        var handler = new MockHttpMessageHandler
        {
            Handler = _ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new StalledStream())
                };
                response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-git-upload-pack-advertisement");
                return Task.FromResult(response);
            }
        };

        var httpClient = new HttpClient(handler);
        var options = new GitRemoteClientOptions
        {
            HttpClient = httpClient,
            Timeout = TimeSpan.FromMilliseconds(150)
        };
        using var connection = new GitHttpConnection(options);

        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await connection.DiscoverReferencesAsync(new Uri("http://localhost/test.git"), "git-upload-pack");
        });
    }

    [Fact]
    public async Task UploadPackAsync_StalledPackStream_ThrowsTimeoutException()
    {
        var handler = new MockHttpMessageHandler
        {
            Handler = async _ =>
            {
                var body = new CombinedPrefixAndStalledStream("NAK\n");
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(body)
                };
                response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-git-upload-pack-result");
                return response;
            }
        };

        var httpClient = new HttpClient(handler);
        var options = new GitRemoteClientOptions
        {
            HttpClient = httpClient,
            Timeout = TimeSpan.FromMilliseconds(150)
        };
        using var connection = new GitHttpConnection(options);

        var ad = new GitRemoteAdvertisement(
            new Dictionary<string, GitHash> { ["refs/heads/main"] = new("1111111111111111111111111111111111111111") },
            new HashSet<string>(),
            new Dictionary<string, string>(),
            "refs/heads/main",
            new("1111111111111111111111111111111111111111"),
            GitObjectFormat.Sha1,
            "test");

        await using var uploadPackResponse = await connection.UploadPackAsync(
            new Uri("http://localhost/test.git"),
            new[] { new GitHash("1111111111111111111111111111111111111111") },
            Array.Empty<GitHash>(),
            ad);

        var buffer = new byte[10];
        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await uploadPackResponse.PackStream.ReadAsync(buffer, CancellationToken.None);
        });
    }

    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class CombinedPrefixAndStalledStream : Stream
    {
        private readonly byte[] _prefix;
        private int _prefixOffset;

        public CombinedPrefixAndStalledStream(string pktLineText)
        {
            var ms = new MemoryStream();
            PktLineWriter.WriteStringAsync(ms, pktLineText, CancellationToken.None).GetAwaiter().GetResult();
            _prefix = ms.ToArray();
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _prefixOffset; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_prefixOffset < _prefix.Length)
            {
                var toCopy = Math.Min(buffer.Length, _prefix.Length - _prefixOffset);
                _prefix.AsMemory(_prefixOffset, toCopy).CopyTo(buffer);
                _prefixOffset += toCopy;
                return toCopy;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
