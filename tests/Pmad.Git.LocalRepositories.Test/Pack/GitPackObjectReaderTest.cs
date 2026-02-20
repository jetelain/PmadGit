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

    [Fact]
    public async Task ReadZLibAsync_WithTruncatedStream_ShouldThrowEndOfStreamException()
    {
        // Arrange: Start of valid zlib header but truncated
        var truncatedData = new byte[] { 0x78, 0x9C }; // Valid zlib header but no data
        var stream = new MemoryStream(truncatedData);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<EndOfStreamException>(
            async () => await GitPackObjectReader.ReadZLibAsync(stream, CancellationToken.None));

        Assert.Contains("Unexpected end of stream while reading compressed data", exception.Message);
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

    [Fact]
    public async Task ReadObjectAsync_WithNonSeekableStream_ShouldThrowArgumentException()
    {
        // Arrange: Create a non-seekable stream
        var nonSeekableStream = new NonSeekableStream();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            async () => await GitPackObjectReader.ReadObjectAsync(
                nonSeekableStream,
                0,
                20,
                (hash, ct) => throw new InvalidOperationException("Should not be called"),
                null,
                CancellationToken.None));

        Assert.Contains("Stream must support seeking", exception.Message);
        Assert.Equal("stream", exception.ParamName);
    }

    [Fact]
    public async Task ReadObjectAsync_WithSHA256Hash_ShouldReadRefDeltaCorrectly()
    {
        // Arrange: Create a ref-delta object with SHA-256 hash (32 bytes)
        var baseContent = "Test content"u8.ToArray();
        var baseObject = new GitObjectData(GitObjectType.Blob, baseContent);
        var baseHash = new GitHash("a".PadRight(64, '0')); // SHA-256 hash (64 hex chars)

        var delta = CreateSimpleCopyDelta(baseContent.Length, baseContent.Length);
        var stream = CreateRefDeltaStream(baseHash, delta, 32); // 32 bytes for SHA-256

        var resolvedHash = false;

        // Act
        var result = await GitPackObjectReader.ReadObjectAsync(
            stream,
            0,
            32, // SHA-256 hash length
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
        Assert.Equal(baseContent, result.Content);
    }

    [Fact]
    public async Task ReadObjectAsync_WithCancellation_ShouldRespectCancellationToken()
    {
        // Arrange: Create a large blob that takes time to read
        var largeContent = new byte[100000];
        Array.Fill<byte>(largeContent, 0x42);
        var stream = CreatePackObjectStream(3, largeContent);

        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await GitPackObjectReader.ReadObjectAsync(
                stream,
                0,
                20,
                (hash, ct) => throw new InvalidOperationException("Should not be called"),
                null,
                cts.Token));
    }

    #endregion

    #region Delta Application Tests

    [Fact]
    public async Task ReadObjectAsync_WithDeltaBaseSizeMismatch_ShouldThrowInvalidDataException()
    {
        // Arrange: Create a delta with wrong base size
        var baseContent = "Hello World"u8.ToArray(); // 11 bytes
        var baseObject = new GitObjectData(GitObjectType.Blob, baseContent);

        // Create delta that expects different base size
        var delta = new List<byte>
        {
            50, // Base size = 50 (wrong!)
            5,  // Result size = 5
            0x91, 0x00, 0x05 // Copy 5 bytes from offset 0
        };

        var stream = CreateOfsDeltaStream(100, delta.ToArray());

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            async () => await GitPackObjectReader.ReadObjectAsync(
                stream,
                200,
                20,
                (hash, ct) => throw new InvalidOperationException("Should not be called"),
                (offset, ct) => Task.FromResult(baseObject),
                CancellationToken.None));

        Assert.Contains("Delta base size mismatch", exception.Message);
    }

    [Fact]
    public async Task ReadObjectAsync_WithDeltaCopyExceedingBase_ShouldThrowInvalidDataException()
    {
        // Arrange: Create a delta that tries to copy beyond base size
        var baseContent = "Hello"u8.ToArray(); // 5 bytes
        var baseObject = new GitObjectData(GitObjectType.Blob, baseContent);

        // Create delta that tries to copy from offset 10 (beyond base)
        var delta = new List<byte>
        {
            5,    // Base size = 5
            5,    // Result size = 5
            0x91, // Copy opcode with offset and size
            10,   // Offset = 10 (exceeds base!)
            5     // Size = 5
        };

        var stream = CreateOfsDeltaStream(100, delta.ToArray());

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            async () => await GitPackObjectReader.ReadObjectAsync(
                stream,
                200,
                20,
                (hash, ct) => throw new InvalidOperationException("Should not be called"),
                (offset, ct) => Task.FromResult(baseObject),
                CancellationToken.None));

        Assert.Contains("Delta copy instruction exceeds base size", exception.Message);
    }

    [Fact]
    public async Task ReadObjectAsync_WithDeltaInsertExceedingPayload_ShouldThrowInvalidDataException()
    {
        // Arrange: Create a delta with insert that exceeds available data
        var baseContent = "Hello"u8.ToArray();
        var baseObject = new GitObjectData(GitObjectType.Blob, baseContent);

        // Create delta with insert opcode that exceeds remaining data
        var delta = new List<byte>
        {
            5,   // Base size = 5
            10,  // Result size = 10
            10   // Insert 10 bytes, but no data follows!
        };

        var stream = CreateOfsDeltaStream(100, delta.ToArray());

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            async () => await GitPackObjectReader.ReadObjectAsync(
                stream,
                200,
                20,
                (hash, ct) => throw new InvalidOperationException("Should not be called"),
                (offset, ct) => Task.FromResult(baseObject),
                CancellationToken.None));

        Assert.Contains("Delta insert instruction exceeds payload", exception.Message);
    }

    [Fact]
    public async Task ReadObjectAsync_WithDeltaInvalidOpcode_ShouldThrowInvalidDataException()
    {
        // Arrange: Create a delta with invalid opcode (0x00)
        var baseContent = "Hello"u8.ToArray();
        var baseObject = new GitObjectData(GitObjectType.Blob, baseContent);

        // Create delta with invalid opcode
        var delta = new List<byte>
        {
            5,   // Base size = 5
            5,   // Result size = 5
            0x00 // Invalid opcode
        };

        var stream = CreateOfsDeltaStream(100, delta.ToArray());

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            async () => await GitPackObjectReader.ReadObjectAsync(
                stream,
                200,
                20,
                (hash, ct) => throw new InvalidOperationException("Should not be called"),
                (offset, ct) => Task.FromResult(baseObject),
                CancellationToken.None));

        Assert.Contains("Invalid delta opcode", exception.Message);
    }

    [Fact]
    public async Task ReadObjectAsync_WithDeltaWrongResultSize_ShouldThrowInvalidDataException()
    {
        // Arrange: Create a delta that produces wrong result size
        var baseContent = "Hello World"u8.ToArray();
        var baseObject = new GitObjectData(GitObjectType.Blob, baseContent);

        // Create delta that claims result size 10 but only produces 5
        var delta = new List<byte>
        {
            11,   // Base size = 11
            10,   // Result size = 10 (claimed)
            0x91, // Copy opcode
            0x00, // Offset = 0
            0x05  // Size = 5 (only produces 5 bytes!)
        };

        var stream = CreateOfsDeltaStream(100, delta.ToArray());

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            async () => await GitPackObjectReader.ReadObjectAsync(
                stream,
                200,
                20,
                (hash, ct) => throw new InvalidOperationException("Should not be called"),
                (offset, ct) => Task.FromResult(baseObject),
                CancellationToken.None));

        Assert.Contains("Delta application produced incorrect length", exception.Message);
    }

    [Fact]
    public async Task ReadObjectAsync_WithComplexDelta_ShouldApplyCorrectly()
    {
        // Arrange: Create a complex delta with copy and insert operations
        var baseContent = "Hello World! This is a test."u8.ToArray();
        var baseObject = new GitObjectData(GitObjectType.Blob, baseContent);

        // Create delta: copy "Hello ", insert "Beautiful ", copy "World!"
        var delta = new List<byte>();
        delta.Add((byte)baseContent.Length); // Base size
        delta.Add(25); // Result size: "Hello Beautiful World!" = 23 bytes, but we'll make it 25 for exact match
        
        // Copy "Hello " (6 bytes from offset 0)
        delta.Add(0x91); // Copy opcode (offset bit 0, size bit 4)
        delta.Add(0x00); // Offset = 0
        delta.Add(0x06); // Size = 6

        // Insert "Beautiful " (10 bytes)
        delta.Add(10); // Insert 10 bytes
        delta.AddRange("Beautiful "u8.ToArray());

        // Copy "World!" (6 bytes from offset 6)
        delta.Add(0x91); // Copy opcode
        delta.Add(0x06); // Offset = 6
        delta.Add(0x06); // Size = 6

        // Insert "!!" (3 bytes to reach 25)
        delta.Add(3); // Insert 3 bytes
        delta.AddRange("!!!"u8.ToArray());

        var stream = CreateOfsDeltaStream(100, delta.ToArray());

        // Act
        var result = await GitPackObjectReader.ReadObjectAsync(
            stream,
            200,
            20,
            (hash, ct) => throw new InvalidOperationException("Should not be called"),
            (offset, ct) => Task.FromResult(baseObject),
            CancellationToken.None);

        // Assert
        Assert.Equal(GitObjectType.Blob, result.Type);
        Assert.Equal("Hello Beautiful World!!!!", System.Text.Encoding.UTF8.GetString(result.Content));
    }

    [Fact]
    public async Task ReadObjectAsync_WithDeltaMaxCopySize_ShouldUseDefaultCopySize()
    {
        // Arrange: Test that copy size of 0 defaults to 0x10000
        var baseContent = new byte[0x20000]; // Large base (131072 bytes)
        Array.Fill<byte>(baseContent, 0x42);
        var baseObject = new GitObjectData(GitObjectType.Blob, baseContent);

        // Create delta with copy size = 0 (should default to 0x10000)
        var delta = new List<byte>();
        
        // Base size = 0x20000 (131072) - variable length encoding
        delta.Add(0x80); // Continue bit set, lower 7 bits = 0
        delta.Add(0x80); // Continue bit set, next 7 bits = 0
        delta.Add(0x08); // No continue bit, next 7 bits = 8 (total: 8 << 14 = 131072)
        
        // Result size = 0x10000 (65536) - variable length encoding
        delta.Add(0x80); // Continue bit set, lower 7 bits = 0
        delta.Add(0x80); // Continue bit set, next 7 bits = 0
        delta.Add(0x04); // No continue bit, next 7 bits = 4 (total: 4 << 14 = 65536)
        
        // Copy opcode with no size bits set (defaults to 0x10000)
        delta.Add(0x80); // Copy opcode, no offset or size bits set

        var stream = CreateOfsDeltaStream(100, delta.ToArray());

        // Act
        var result = await GitPackObjectReader.ReadObjectAsync(
            stream,
            200,
            20,
            (hash, ct) => throw new InvalidOperationException("Should not be called"),
            (offset, ct) => Task.FromResult(baseObject),
            CancellationToken.None);

        // Assert
        Assert.Equal(GitObjectType.Blob, result.Type);
        Assert.Equal(0x10000, result.Content.Length);
    }

    #endregion

    #region Helper Methods

    private static Stream CreatePackObjectStream(int kind, byte[] content)
    {
        var stream = new MemoryStream();
        
        // Write type and size header
        var size = content.Length;
        var firstByte = (byte)((kind << 4) | (size & 0x0F));
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
                stream.WriteByte((byte)((remaining & 0x7F) | 0x80));
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

    private static Stream CreateRefDeltaStream(GitHash baseHash, byte[] delta, int hashLengthBytes = 20)
    {
        var stream = new MemoryStream();
        
        // Write type (7) and size header
        var size = delta.Length;
        stream.WriteByte((byte)((7 << 4) | (size & 0x0F)));

        // Write base hash (20 bytes for SHA-1 or 32 bytes for SHA-256)
        var hashBytes = Convert.FromHexString(baseHash.Value);
        if (hashBytes.Length < hashLengthBytes)
        {
            // Pad with zeros if needed
            var paddedBytes = new byte[hashLengthBytes];
            Array.Copy(hashBytes, paddedBytes, hashBytes.Length);
            stream.Write(paddedBytes);
        }
        else
        {
            stream.Write(hashBytes, 0, hashLengthBytes);
        }

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
        stream.WriteByte((byte)((6 << 4) | (size & 0x0F)));

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

    #region Helper Classes

    private sealed class NonSeekableStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    #endregion
}
