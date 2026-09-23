using System.Text;
using Pmad.Git.Protocol;
using Pmad.Git.RemoteClient;

namespace Pmad.Git.RemoteClient.Test;

public sealed class GitSidebandStreamTest
{
    [Fact]
    public async Task ReadAsync_Band1Data_StreamsPackPayload()
    {
        using var memory = new MemoryStream();
        var expectedData = Encoding.UTF8.GetBytes("PACK-PAYLOAD-TEST-DATA-12345");

        // Write Band 1 packet: 0x01 + data
        var packetPayload = new byte[expectedData.Length + 1];
        packetPayload[0] = 1; // Band 1
        expectedData.CopyTo(packetPayload, 1);
        await PktLineWriter.WriteAsync(memory, packetPayload, CancellationToken.None);

        // Write Flush
        await PktLineWriter.WriteFlushAsync(memory, CancellationToken.None);

        memory.Seek(0, SeekOrigin.Begin);

        using var sideband = new GitSidebandStream(memory);
        using var result = new MemoryStream();
        await sideband.CopyToAsync(result);

        Assert.Equal(expectedData, result.ToArray());
    }

    [Fact]
    public async Task ReadAsync_Band2Progress_InvokesCallback()
    {
        using var memory = new MemoryStream();
        var progressMessages = new List<string>();

        // Band 2: Progress message
        var progress1 = Encoding.UTF8.GetBytes("Counting objects: 50%\n");
        var p1Payload = new byte[progress1.Length + 1];
        p1Payload[0] = 2; // Band 2
        progress1.CopyTo(p1Payload, 1);
        await PktLineWriter.WriteAsync(memory, p1Payload, CancellationToken.None);

        // Band 1: Data
        var data = Encoding.UTF8.GetBytes("DATA");
        var dPayload = new byte[data.Length + 1];
        dPayload[0] = 1;
        data.CopyTo(dPayload, 1);
        await PktLineWriter.WriteAsync(memory, dPayload, CancellationToken.None);

        // Band 2: Progress message 2
        var progress2 = Encoding.UTF8.GetBytes("Counting objects: 100%\n");
        var p2Payload = new byte[progress2.Length + 1];
        p2Payload[0] = 2;
        progress2.CopyTo(p2Payload, 1);
        await PktLineWriter.WriteAsync(memory, p2Payload, CancellationToken.None);

        // Flush
        await PktLineWriter.WriteFlushAsync(memory, CancellationToken.None);

        memory.Seek(0, SeekOrigin.Begin);

        using var sideband = new GitSidebandStream(memory, onProgress: msg => progressMessages.Add(msg));
        using var result = new MemoryStream();
        await sideband.CopyToAsync(result);

        Assert.Equal(data, result.ToArray());
        Assert.Equal(2, progressMessages.Count);
        Assert.Equal("Counting objects: 50%\n", progressMessages[0]);
        Assert.Equal("Counting objects: 100%\n", progressMessages[1]);
    }

    [Fact]
    public async Task ReadAsync_Band3Error_ThrowsGitRemoteException()
    {
        using var memory = new MemoryStream();

        // Band 3: Error message
        var errorMsg = Encoding.UTF8.GetBytes("remote: repository not found");
        var errPayload = new byte[errorMsg.Length + 1];
        errPayload[0] = 3; // Band 3
        errorMsg.CopyTo(errPayload, 1);
        await PktLineWriter.WriteAsync(memory, errPayload, CancellationToken.None);

        memory.Seek(0, SeekOrigin.Begin);

        using var sideband = new GitSidebandStream(memory);
        var buffer = new byte[64];

        var ex = await Assert.ThrowsAsync<GitRemoteException>(async () =>
        {
            await sideband.ReadAsync(buffer);
        });

        Assert.Contains("repository not found", ex.Message);
    }

    [Fact]
    public async Task ReadAsync_InterleavedDataAndProgress_HandlesCorrectly()
    {
        using var memory = new MemoryStream();
        var progress = new List<string>();

        // Band 2: start
        var p1 = new byte[] { 2, (byte)'A', (byte)'\n' };
        await PktLineWriter.WriteAsync(memory, p1, CancellationToken.None);

        // Band 1: part 1
        var d1 = new byte[] { 1, 10, 20, 30 };
        await PktLineWriter.WriteAsync(memory, d1, CancellationToken.None);

        // Band 2: middle
        var p2 = new byte[] { 2, (byte)'B', (byte)'\n' };
        await PktLineWriter.WriteAsync(memory, p2, CancellationToken.None);

        // Band 1: part 2
        var d2 = new byte[] { 1, 40, 50 };
        await PktLineWriter.WriteAsync(memory, d2, CancellationToken.None);

        // Flush
        await PktLineWriter.WriteFlushAsync(memory, CancellationToken.None);

        memory.Seek(0, SeekOrigin.Begin);

        using var sideband = new GitSidebandStream(memory, onProgress: msg => progress.Add(msg));
        using var result = new MemoryStream();
        await sideband.CopyToAsync(result);

        Assert.Equal(new byte[] { 10, 20, 30, 40, 50 }, result.ToArray());
        Assert.Equal(new[] { "A\n", "B\n" }, progress);
    }

    [Fact]
    public async Task ReadAsync_SmallBuffer_HandlesPartialReads()
    {
        using var memory = new MemoryStream();
        var originalData = new byte[30];
        for (var i = 0; i < originalData.Length; i++)
        {
            originalData[i] = (byte)(i + 1);
        }

        var payload = new byte[originalData.Length + 1];
        payload[0] = 1;
        originalData.CopyTo(payload, 1);
        await PktLineWriter.WriteAsync(memory, payload, CancellationToken.None);
        await PktLineWriter.WriteFlushAsync(memory, CancellationToken.None);

        memory.Seek(0, SeekOrigin.Begin);

        using var sideband = new GitSidebandStream(memory);
        var smallBuffer = new byte[7];
        var totalRead = new List<byte>();

        while (true)
        {
            var read = await sideband.ReadAsync(smallBuffer);
            if (read == 0)
            {
                break;
            }
            totalRead.AddRange(smallBuffer.Take(read));
        }

        Assert.Equal(originalData, totalRead.ToArray());
    }

    [Fact]
    public async Task ReadAsync_InvalidChannel_ThrowsInvalidDataException()
    {
        using var memory = new MemoryStream();
        var payload = new byte[] { 9, 1, 2, 3 }; // Channel 9 is invalid
        await PktLineWriter.WriteAsync(memory, payload, CancellationToken.None);

        memory.Seek(0, SeekOrigin.Begin);

        using var sideband = new GitSidebandStream(memory);
        var buffer = new byte[10];

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await sideband.ReadAsync(buffer);
        });
    }
}

