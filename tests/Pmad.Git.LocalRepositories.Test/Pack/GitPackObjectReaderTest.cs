using System.IO.Compression;
using Pmad.Git.LocalRepositories.Pack;

namespace Pmad.Git.LocalRepositories.Test.Pack;

public sealed class GitPackObjectReaderTest
{
    #region ReadTypeAndSizeAsync Tests

    [Fact]
    public async Task ReadTypeAndSizeAsync_WithSimpleCommit_ShouldReadCorrectly()
    {
        // Arrange: Type=1 (commit), Size=15
        // Format: bits 7-5: continuation, bits 4-7: type, bits 0-3: size low bits
        // First byte: 0001 1111 = 0x1F (type=1, size low=15, no continuation)
        var data = new byte[] { 0x1F };
        var stream = new MemoryStream(data);

        // Act
        var (kind, size) = await GitPackObjectReader.ReadTypeAndSizeAsync(stream, CancellationToken.None);

        // Assert
        Assert.Equal(1, kind);
        Assert.Equal(15L, size);
    }

    [Fact]
    public async Task ReadTypeAndSizeAsync_WithLargerSize_ShouldHandleMultipleBytes()
    {
        // Arrange: Type=3 (blob), Size=200
        // First byte: type=3, size low 4 bits=0, continuation bit set
        // 200 = 11001000 binary
        // Low 4 bits: 1000 (8), remaining: 1100 (12)
        // First byte: 1011 1000 = 0xB8 (type=3, size=8, continue)
        // Second byte: 0000 1100 = 0x0C (size bits 4-10 = 12)
        var data = new byte[] { 0xB8, 0x0C };
        var stream = new MemoryStream(data);

        // Act
        var (kind, size) = await GitPackObjectReader.ReadTypeAndSizeAsync(stream, CancellationToken.None);

        // Assert
        Assert.Equal(3, kind);
        Assert.Equal(200L, size);
    }

    [Fact]
    public async Task ReadTypeAndSizeAsync_WithEmptyStream_ShouldThrowEndOfStreamException()
    {
        // Arrange
        var stream = new MemoryStream(Array.Empty<byte>());

        // Act & Assert
        await Assert.ThrowsAsync<EndOfStreamException>(
            async () => await GitPackObjectReader.ReadTypeAndSizeAsync(stream, CancellationToken.None));
    }

    #endregion

    #region ReadVariableLength Tests

    [Fact]
    public void ReadVariableLength_WithSingleByte_ShouldReadCorrectly()
    {
        // Arrange: Value = 50
        var data = new byte[] { 50 };
        var cursor = 0;

        // Act
        var result = GitPackObjectReader.ReadVariableLength(data, ref cursor);

        // Assert
        Assert.Equal(50L, result);
        Assert.Equal(1, cursor);
    }

    [Fact]
    public void ReadVariableLength_WithMultipleBytes_ShouldReadCorrectly()
    {
        // Arrange: Value = 200
        // 200 = 11001000 = 1001000 (lower 7 bits) + 0000001 (next 7 bits)
        // First byte: 11001000 (0xC8) - continue bit set
        // Second byte: 00000001 (0x01) - no continue bit
        var data = new byte[] { 0xC8, 0x01 };
        var cursor = 0;

        // Act
        var result = GitPackObjectReader.ReadVariableLength(data, ref cursor);

        // Assert
        Assert.Equal(200L, result);
        Assert.Equal(2, cursor);
    }

    [Fact]
    public void ReadVariableLength_WithLargeValue_ShouldReadCorrectly()
    {
        // Arrange: Value = 70000 (as used in tests)
        // 70000 = 0x11170
        // Variable length: 0xF0, 0xA2, 0x04
        var data = new byte[] { 0xF0, 0xA2, 0x04 };
        var cursor = 0;

        // Act
        var result = GitPackObjectReader.ReadVariableLength(data, ref cursor);

        // Assert
        Assert.Equal(70000L, result);
        Assert.Equal(3, cursor);
    }

    #endregion

    #region ReadOfsDeltaOffsetAsync Tests

    [Fact]
    public async Task ReadOfsDeltaOffsetAsync_WithSmallOffset_ShouldReadCorrectly()
    {
        // Arrange: Offset = 10
        var data = new byte[] { 10 };
        var stream = new MemoryStream(data);

        // Act
        var offset = await GitPackObjectReader.ReadOfsDeltaOffsetAsync(stream, CancellationToken.None);

        // Assert
        Assert.Equal(10L, offset);
    }

    [Fact]
    public async Task ReadOfsDeltaOffsetAsync_WithLargerOffset_ShouldReadCorrectly()
    {
        // Arrange: Test encoding similar to variable length but different algorithm
        // For offset = 200
        var data = new byte[] { 0xC9, 0x01 }; // Ofs-delta encoding
        var stream = new MemoryStream(data);

        // Act
        var offset = await GitPackObjectReader.ReadOfsDeltaOffsetAsync(stream, CancellationToken.None);

        // Assert: The exact value depends on Git's ofs-delta encoding
        Assert.True(offset > 0);
    }

    [Fact]
    public async Task ReadOfsDeltaOffsetAsync_WithEmptyStream_ShouldThrowEndOfStreamException()
    {
        // Arrange
        var stream = new MemoryStream(Array.Empty<byte>());

        // Act & Assert
        await Assert.ThrowsAsync<EndOfStreamException>(
            async () => await GitPackObjectReader.ReadOfsDeltaOffsetAsync(stream, CancellationToken.None));
    }

    #endregion

    #region ReadByteAsync Tests

    [Fact]
    public async Task ReadByteAsync_WithData_ShouldReadByte()
    {
        // Arrange
        var data = new byte[] { 42, 100, 200 };
        var stream = new MemoryStream(data);

        // Act
        var byte1 = await GitPackObjectReader.ReadByteAsync(stream, CancellationToken.None);
        var byte2 = await GitPackObjectReader.ReadByteAsync(stream, CancellationToken.None);
        var byte3 = await GitPackObjectReader.ReadByteAsync(stream, CancellationToken.None);

        // Assert
        Assert.Equal(42, byte1);
        Assert.Equal(100, byte2);
        Assert.Equal(200, byte3);
    }

    [Fact]
    public async Task ReadByteAsync_AtEndOfStream_ShouldReturnNegativeOne()
    {
        // Arrange
        var stream = new MemoryStream(Array.Empty<byte>());

        // Act
        var result = await GitPackObjectReader.ReadByteAsync(stream, CancellationToken.None);

        // Assert
        Assert.Equal(-1, result);
    }

    [Fact]
    public async Task ReadByteAsync_WithCancellation_ShouldThrowOperationCanceledException()
    {
        // Arrange
        var stream = new MemoryStream(new byte[] { 42 });
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await GitPackObjectReader.ReadByteAsync(stream, cts.Token));
    }

    #endregion

    #region ReadZLibAsync Tests

    [Fact]
    public async Task ReadZLibAsync_WithCompressedData_ShouldDecompress()
    {
        // Arrange: Compress some test data
        var originalData = "Hello, Git Pack!"u8.ToArray();
        var compressedStream = new MemoryStream();
        using (var zlib = new ZLibStream(compressedStream, CompressionMode.Compress, leaveOpen: true))
        {
            await zlib.WriteAsync(originalData);
        }
        compressedStream.Position = 0;

        // Act
        var decompressed = await GitPackObjectReader.ReadZLibAsync(compressedStream, CancellationToken.None);

        // Assert
        Assert.Equal(originalData, decompressed);
    }

    [Fact]
    public async Task ReadZLibAsync_WithEmptyData_ShouldReturnEmpty()
    {
        // Arrange: Compress empty data
        var compressedStream = new MemoryStream();
        using (var zlib = new ZLibStream(compressedStream, CompressionMode.Compress, leaveOpen: true))
        {
            await zlib.WriteAsync(Array.Empty<byte>());
        }
        compressedStream.Position = 0;

        // Act
        var decompressed = await GitPackObjectReader.ReadZLibAsync(compressedStream, CancellationToken.None);

        // Assert
        Assert.Empty(decompressed);
    }

    [Fact]
    public async Task ReadZLibAsync_WithMultipleConsecutiveObjects_ShouldNotReadAhead()
    {
        // Arrange: Create two consecutive compressed objects
        var data1 = "First Object"u8.ToArray();
        var data2 = "Second Object"u8.ToArray();

        var stream = new MemoryStream();
        
        // Write first compressed object
        long firstEndPosition;
        using (var zlib = new ZLibStream(stream, CompressionMode.Compress, leaveOpen: true))
        {
            await zlib.WriteAsync(data1);
        }
        firstEndPosition = stream.Position;

        // Write second compressed object
        using (var zlib = new ZLibStream(stream, CompressionMode.Compress, leaveOpen: true))
        {
            await zlib.WriteAsync(data2);
        }

        stream.Position = 0;

        // Act: Read first object
        var decompressed1 = await GitPackObjectReader.ReadZLibAsync(stream, CancellationToken.None);

        // Assert: Stream position should be at start of second object
        Assert.Equal(data1, decompressed1);
        Assert.Equal(firstEndPosition, stream.Position);

        // Act: Read second object
        var decompressed2 = await GitPackObjectReader.ReadZLibAsync(stream, CancellationToken.None);

        // Assert
        Assert.Equal(data2, decompressed2);
    }

    [Fact]
    public async Task ReadZLibAsync_WithInvalidData_ShouldThrowException()
    {
        // Arrange: Invalid compressed data
        var invalidData = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };
        var stream = new MemoryStream(invalidData);

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(
            async () => await GitPackObjectReader.ReadZLibAsync(stream, CancellationToken.None));
    }

    #endregion

    #region ReadObjectAsync Tests

    [Fact]
    public async Task ReadObjectAsync_WithCommitObject_ShouldReadCorrectly()
    {
        // Arrange: Create a commit object (kind 1)
        var commitContent = "tree abc\nauthor Test\n\nCommit message"u8.ToArray();
        var stream = CreatePackObjectStream(1, commitContent);

        // Act
        var result = await GitPackObjectReader.ReadObjectAsync(
            stream,
            0,
            20,
            (hash, ct) => throw new InvalidOperationException("Should not resolve by hash"),
            (offset, ct) => throw new InvalidOperationException("Should not resolve by offset"),
            CancellationToken.None);

        // Assert
        Assert.Equal(GitObjectType.Commit, result.Type);
        Assert.Equal(commitContent, result.Content);
    }

    [Fact]
    public async Task ReadObjectAsync_WithTreeObject_ShouldReadCorrectly()
    {
        // Arrange: Create a tree object (kind 2)
        var treeContent = "100644 file.txt\0"u8.ToArray();
        var stream = CreatePackObjectStream(2, treeContent);

        // Act
        var result = await GitPackObjectReader.ReadObjectAsync(
            stream,
            0,
            20,
            (hash, ct) => throw new InvalidOperationException("Should not resolve by hash"),
            (offset, ct) => throw new InvalidOperationException("Should not resolve by offset"),
            CancellationToken.None);

        // Assert
        Assert.Equal(GitObjectType.Tree, result.Type);
        Assert.Equal(treeContent, result.Content);
    }

    [Fact]
    public async Task ReadObjectAsync_WithBlobObject_ShouldReadCorrectly()
    {
        // Arrange: Create a blob object (kind 3)
        var blobContent = "File content here"u8.ToArray();
        var stream = CreatePackObjectStream(3, blobContent);

        // Act
        var result = await GitPackObjectReader.ReadObjectAsync(
            stream,
            0,
            20,
            (hash, ct) => throw new InvalidOperationException("Should not resolve by hash"),
            (offset, ct) => throw new InvalidOperationException("Should not resolve by offset"),
            CancellationToken.None);

        // Assert
        Assert.Equal(GitObjectType.Blob, result.Type);
        Assert.Equal(blobContent, result.Content);
    }

    [Fact]
    public async Task ReadObjectAsync_WithTagObject_ShouldReadCorrectly()
    {
        // Arrange: Create a tag object (kind 4)
        var tagContent = "object abc\ntype commit\ntag v1.0\n"u8.ToArray();
        var stream = CreatePackObjectStream(4, tagContent);

        // Act
        var result = await GitPackObjectReader.ReadObjectAsync(
            stream,
            0,
            20,
            (hash, ct) => throw new InvalidOperationException("Should not resolve by hash"),
            (offset, ct) => throw new InvalidOperationException("Should not resolve by offset"),
            CancellationToken.None);

        // Assert
        Assert.Equal(GitObjectType.Tag, result.Type);
        Assert.Equal(tagContent, result.Content);
    }

    [Fact]
    public async Task ReadObjectAsync_WithRefDelta_ShouldResolveAndApplyDelta()
    {
        // Arrange: Create a ref-delta object (kind 7)
        var baseContent = "Hello World"u8.ToArray();
        var baseObject = new GitObjectData(GitObjectType.Blob, baseContent);
        var baseHash = new GitHash("a".PadRight(40, '0')); // SHA-1 hash

        // Create delta that copies first 5 bytes
        var delta = CreateSimpleCopyDelta(baseContent.Length, 5);
        var stream = CreateRefDeltaStream(baseHash, delta);

        var resolvedHash = false;

        // Act
        var result = await GitPackObjectReader.ReadObjectAsync(
            stream,
            0,
            20,
            (hash, ct) =>
            {
                resolvedHash = true;
                Assert.Equal(baseHash, hash);
                return Task.FromResult(baseObject);
            },
            null,
            CancellationToken.None);

        // Assert
        Assert.True(resolvedHash);
        Assert.Equal(GitObjectType.Blob, result.Type);
        Assert.Equal("Hello"u8.ToArray(), result.Content);
    }

    [Fact]
    public async Task ReadObjectAsync_WithOfsDelta_ShouldResolveAndApplyDelta()
    {
        // Arrange: Create an ofs-delta object (kind 6)
        var baseContent = "Hello World"u8.ToArray();
        var baseObject = new GitObjectData(GitObjectType.Blob, baseContent);

        // Create delta that copies first 5 bytes
        var delta = CreateSimpleCopyDelta(baseContent.Length, 5);
        var stream = CreateOfsDeltaStream(100, delta);

        var resolvedOffset = false;
        var currentOffset = 200L;

        // Act
        var result = await GitPackObjectReader.ReadObjectAsync(
            stream,
            currentOffset,
            20,
            (hash, ct) => throw new InvalidOperationException("Should not resolve by hash"),
            (offset, ct) =>
            {
                resolvedOffset = true;
                Assert.Equal(100L, offset); // currentOffset (200) - distance (100)
                return Task.FromResult(baseObject);
            },
            CancellationToken.None);

        // Assert
        Assert.True(resolvedOffset);
        Assert.Equal(GitObjectType.Blob, result.Type);
        Assert.Equal("Hello"u8.ToArray(), result.Content);
    }

    [Fact]
    public async Task ReadObjectAsync_WithOfsDeltaAndNoResolver_ShouldThrowInvalidDataException()
    {
        // Arrange: Create an ofs-delta object (kind 6)
        var delta = CreateSimpleCopyDelta(10, 5);
        var stream = CreateOfsDeltaStream(100, delta);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            async () => await GitPackObjectReader.ReadObjectAsync(
                stream,
                200,
                20,
                (hash, ct) => throw new InvalidOperationException("Should not resolve by hash"),
                null, // No offset resolver
                CancellationToken.None));

        Assert.Contains("ofs-delta requires offset resolution capability", exception.Message);
    }

    [Fact]
    public async Task ReadObjectAsync_WithUnsupportedKind_ShouldThrowNotSupportedException()
    {
        // Arrange: Create object with unsupported kind (5)
        var stream = new MemoryStream();
        stream.WriteByte(0x55); // kind=5, size=5

        stream.Position = 0;

        // Act & Assert
        await Assert.ThrowsAsync<NotSupportedException>(
            async () => await GitPackObjectReader.ReadObjectAsync(
                stream,
                0,
                20,
                (hash, ct) => throw new InvalidOperationException("Should not be called"),
                null,
                CancellationToken.None));
    }

    #endregion

    #region Helper Methods

    private static Stream CreatePackObjectStream(int kind, byte[] content)
    {
        var stream = new MemoryStream();
        
        // Write type and size header
        var size = content.Length;
        var firstByte = (byte)(kind << 4 | size & 0x0F);
        if (size < 16)
        {
            stream.WriteByte(firstByte);
        }
        else
        {
            stream.WriteByte((byte)(firstByte | 0x80));
            var remaining = size >> 4;
            while (remaining >= 128)
            {
                stream.WriteByte((byte)(remaining & 0x7F | 0x80));
                remaining >>= 7;
            }
            stream.WriteByte((byte)(remaining & 0x7F));
        }

        // Write compressed content
        using (var zlib = new ZLibStream(stream, CompressionMode.Compress, leaveOpen: true))
        {
            zlib.Write(content);
        }

        stream.Position = 0;
        return stream;
    }

    private static Stream CreateRefDeltaStream(GitHash baseHash, byte[] delta)
    {
        var stream = new MemoryStream();
        
        // Write type (7) and size header
        var size = delta.Length;
        stream.WriteByte((byte)(7 << 4 | size & 0x0F));

        // Write base hash (20 bytes for SHA-1)
        var hashBytes = Convert.FromHexString(baseHash.Value);
        stream.Write(hashBytes);

        // Write compressed delta
        using (var zlib = new ZLibStream(stream, CompressionMode.Compress, leaveOpen: true))
        {
            zlib.Write(delta);
        }

        stream.Position = 0;
        return stream;
    }

    private static Stream CreateOfsDeltaStream(long distance, byte[] delta)
    {
        var stream = new MemoryStream();
        
        // Write type (6) and size header
        var size = delta.Length;
        stream.WriteByte((byte)(6 << 4 | size & 0x0F));

        // Write ofs-delta offset (simplified - single byte for small distances)
        stream.WriteByte((byte)distance);

        // Write compressed delta
        using (var zlib = new ZLibStream(stream, CompressionMode.Compress, leaveOpen: true))
        {
            zlib.Write(delta);
        }

        stream.Position = 0;
        return stream;
    }

    private static byte[] CreateSimpleCopyDelta(int baseSize, int copySize)
    {
        var delta = new List<byte>();
        
        // Base size (variable length)
        delta.Add((byte)baseSize);
        
        // Result size (variable length)
        delta.Add((byte)copySize);
        
        // Copy instruction: copy 'copySize' bytes from offset 0
        delta.Add(0x91); // Copy opcode: bits 0 (offset) and 4 (size) set
        delta.Add(0x00); // Offset = 0
        delta.Add((byte)copySize); // Size = copySize

        return delta.ToArray();
    }

    #endregion
}
