using System.Text;
using Pmad.Git.HttpServer.Protocol;

namespace Pmad.Git.HttpServer.Test.Protocol;

public sealed class PktLineProtocolTest
{
    [Fact]
    public async Task PktLineReader_CanReadSimplePacket()
    {
        // 0010 = 16 bytes total (4 header + 12 payload)
        var data = "0010hello world\n";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));
        var reader = new PktLineReader(stream);

        var packet = await reader.ReadAsync(CancellationToken.None);

        Assert.NotNull(packet);
        Assert.False(packet.Value.IsFlush);
        Assert.False(packet.Value.IsDelimiter);
        Assert.Equal("hello world\n", packet.Value.AsString());
    }

    [Fact]
    public async Task PktLineReader_CanReadFlush()
    {
        var data = "0000";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));
        var reader = new PktLineReader(stream);

        var packet = await reader.ReadAsync(CancellationToken.None);

        Assert.NotNull(packet);
        Assert.True(packet.Value.IsFlush);
    }

    [Fact]
    public async Task PktLineReader_CanReadDelimiter()
    {
        var data = "0001";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));
        var reader = new PktLineReader(stream);

        var packet = await reader.ReadAsync(CancellationToken.None);

        Assert.NotNull(packet);
        Assert.True(packet.Value.IsDelimiter);
    }

    [Fact]
    public async Task PktLineReader_CanReadMultiplePackets()
    {
        var data = "0006a\n0006b\n0000";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));
        var reader = new PktLineReader(stream);

        var packet1 = await reader.ReadAsync(CancellationToken.None);
        Assert.NotNull(packet1);
        Assert.Equal("a\n", packet1.Value.AsString());

        var packet2 = await reader.ReadAsync(CancellationToken.None);
        Assert.NotNull(packet2);
        Assert.Equal("b\n", packet2.Value.AsString());

        var flush = await reader.ReadAsync(CancellationToken.None);
        Assert.NotNull(flush);
        Assert.True(flush.Value.IsFlush);
    }

    [Fact]
    public async Task PktLineWriter_CanWriteSimplePacket()
    {
        var stream = new MemoryStream();
        
        await PktLineWriter.WriteStringAsync(stream, "test\n", CancellationToken.None);

        var result = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Equal("0009test\n", result);
    }

    [Fact]
    public async Task PktLineWriter_CanWriteFlush()
    {
        var stream = new MemoryStream();
        
        await PktLineWriter.WriteFlushAsync(stream, CancellationToken.None);

        var result = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Equal("0000", result);
    }

    [Fact]
    public async Task RoundTrip_WriteThenRead()
    {
        var stream = new MemoryStream();
        var writer = PktLineWriter.WriteStringAsync(stream, "want 1234567890abcdef1234567890abcdef12345678\n", CancellationToken.None);
        await writer;
        await PktLineWriter.WriteFlushAsync(stream, CancellationToken.None);

        stream.Position = 0;
        var reader = new PktLineReader(stream);

        var packet = await reader.ReadAsync(CancellationToken.None);
        Assert.NotNull(packet);
        Assert.Equal("want 1234567890abcdef1234567890abcdef12345678\n", packet.Value.AsString());

        var flush = await reader.ReadAsync(CancellationToken.None);
        Assert.NotNull(flush);
        Assert.True(flush.Value.IsFlush);
    }
}
