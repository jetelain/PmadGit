using System.Text;
using Pmad.Git.Protocol;

namespace Pmad.Git.Protocol.Test.Protocol;

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
    public async Task PktLineReader_CanReadResponseEnd()
    {
        var data = "0002";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));
        var reader = new PktLineReader(stream);

        var packet = await reader.ReadAsync(CancellationToken.None);

        Assert.NotNull(packet);
        Assert.False(packet.Value.IsFlush);
        Assert.False(packet.Value.IsDelimiter);
        Assert.True(packet.Value.IsResponseEnd);
    }

    [Fact]
    public async Task PktLineReader_ControlFramesAreDifferentiated()
    {
        var data = "000000010002";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));
        var reader = new PktLineReader(stream);

        var flush = await reader.ReadAsync(CancellationToken.None);
        Assert.NotNull(flush);
        Assert.True(flush.Value.IsFlush);
        Assert.False(flush.Value.IsDelimiter);
        Assert.False(flush.Value.IsResponseEnd);

        var delim = await reader.ReadAsync(CancellationToken.None);
        Assert.NotNull(delim);
        Assert.False(delim.Value.IsFlush);
        Assert.True(delim.Value.IsDelimiter);
        Assert.False(delim.Value.IsResponseEnd);

        var respEnd = await reader.ReadAsync(CancellationToken.None);
        Assert.NotNull(respEnd);
        Assert.False(respEnd.Value.IsFlush);
        Assert.False(respEnd.Value.IsDelimiter);
        Assert.True(respEnd.Value.IsResponseEnd);
    }

    [Fact]
    public async Task PktLineReader_RejectsLength3()
    {
        var data = "0003";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));
        var reader = new PktLineReader(stream);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => reader.ReadAsync(CancellationToken.None));
        Assert.Contains("invalid", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PktLineReader_RejectsLengthExceeding65520()
    {
        // 0xffff = 65535 > 65520
        var data = "ffff" + new string('a', 10);
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));
        var reader = new PktLineReader(stream);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => reader.ReadAsync(CancellationToken.None));
        Assert.Contains("exceeds maximum allowed packet length", ex.Message);
    }

    [Fact]
    public async Task PktLineWriter_CanWriteDelimiter()
    {
        var stream = new MemoryStream();
        await PktLineWriter.WriteDelimiterAsync(stream, CancellationToken.None);

        var result = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Equal("0001", result);
    }

    [Fact]
    public async Task PktLineWriter_CanWriteResponseEnd()
    {
        var stream = new MemoryStream();
        await PktLineWriter.WriteResponseEndAsync(stream, CancellationToken.None);

        var result = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Equal("0002", result);
    }

    [Fact]
    public async Task PktLineWriter_MaxPayloadSucceeds()
    {
        var stream = new MemoryStream();
        var payload = new byte[PktLineWriter.MaxPayloadLength];
        Array.Fill<byte>(payload, (byte)'x');

        await PktLineWriter.WriteAsync(stream, payload, CancellationToken.None);

        var written = stream.ToArray();
        Assert.Equal(65520, written.Length);
        Assert.Equal("fff0", Encoding.ASCII.GetString(written, 0, 4));
    }

    [Fact]
    public async Task PktLineWriter_ExceedingMaxPayloadThrowsArgumentOutOfRangeException()
    {
        var stream = new MemoryStream();
        var payload = new byte[PktLineWriter.MaxPayloadLength + 1];

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => PktLineWriter.WriteAsync(stream, payload, CancellationToken.None));

        Assert.Equal("payload", ex.ParamName);
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
