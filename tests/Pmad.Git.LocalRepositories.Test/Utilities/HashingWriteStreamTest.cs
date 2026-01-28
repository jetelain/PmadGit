using System.Security.Cryptography;
using Pmad.Git.LocalRepositories.Utilities;

namespace Pmad.Git.LocalRepositories.Test.Utilities;

public sealed class HashingWriteStreamTest
{
    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenStreamIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new HashingWriteStream(null!, HashAlgorithmName.SHA256));
    }

    [Fact]
    public void CanRead_ReturnsFalse()
    {
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingWriteStream(innerStream, HashAlgorithmName.SHA256);

        Assert.False(hashingStream.CanRead);
    }

    [Fact]
    public void CanSeek_ReturnsFalse()
    {
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingWriteStream(innerStream, HashAlgorithmName.SHA256);

        Assert.False(hashingStream.CanSeek);
    }

    [Fact]
    public void CanWrite_ReturnsTrue_WhenInnerStreamCanWrite()
    {
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingWriteStream(innerStream, HashAlgorithmName.SHA256);

        Assert.True(hashingStream.CanWrite);
    }

    [Fact]
    public void Length_ThrowsNotSupportedException()
    {
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingWriteStream(innerStream, HashAlgorithmName.SHA256);

        Assert.Throws<NotSupportedException>(() => hashingStream.Length);
    }

    [Fact]
    public void Position_Get_ThrowsNotSupportedException()
    {
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingWriteStream(innerStream, HashAlgorithmName.SHA256);

        Assert.Throws<NotSupportedException>(() => hashingStream.Position);
    }

    [Fact]
    public void Position_Set_ThrowsNotSupportedException()
    {
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingWriteStream(innerStream, HashAlgorithmName.SHA256);

        Assert.Throws<NotSupportedException>(() => hashingStream.Position = 0);
    }

    [Fact]
    public void Read_ThrowsNotSupportedException()
    {
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingWriteStream(innerStream, HashAlgorithmName.SHA256);

        Assert.Throws<NotSupportedException>(() => hashingStream.Read(new byte[10], 0, 10));
    }

    [Fact]
    public void Seek_ThrowsNotSupportedException()
    {
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingWriteStream(innerStream, HashAlgorithmName.SHA256);

        Assert.Throws<NotSupportedException>(() => hashingStream.Seek(0, SeekOrigin.Begin));
    }

    [Fact]
    public void SetLength_ThrowsNotSupportedException()
    {
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingWriteStream(innerStream, HashAlgorithmName.SHA256);

        Assert.Throws<NotSupportedException>(() => hashingStream.SetLength(0));
    }

    [Fact]
    public void Write_WritesDataAndUpdatesHash()
    {
        var testData = "Hello, World!"u8.ToArray();
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingWriteStream(innerStream, HashAlgorithmName.SHA256);

        hashingStream.Write(testData, 0, testData.Length);

        Assert.Equal(testData, innerStream.ToArray());

        var hash = hashingStream.CompleteHash();
        Assert.NotNull(hash);
        Assert.Equal(32, hash.Length); // SHA256 produces 32 bytes
    }

    [Fact]
    public void Write_WithOffset_WritesCorrectData()
    {
        var testData = "XXXHELLO"u8.ToArray();
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingWriteStream(innerStream, HashAlgorithmName.SHA256);

        hashingStream.Write(testData, 3, 5);

        Assert.Equal("HELLO"u8.ToArray(), innerStream.ToArray());
    }

    [Fact]
    public void Write_MultipleWrites_UpdatesHashCorrectly()
    {
        var testData1 = "Hello, "u8.ToArray();
        var testData2 = "World!"u8.ToArray();
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingWriteStream(innerStream, HashAlgorithmName.SHA256);

        hashingStream.Write(testData1, 0, testData1.Length);
        hashingStream.Write(testData2, 0, testData2.Length);

        var hash = hashingStream.CompleteHash();

        // Verify hash matches combined data
        using var sha256 = SHA256.Create();
        var combinedData = testData1.Concat(testData2).ToArray();
        var expectedHash = sha256.ComputeHash(combinedData);

        Assert.Equal(expectedHash, hash);
    }

    [Fact]
    public async Task WriteAsync_WritesDataAndUpdatesHash()
    {
        var testData = "Hello, World!"u8.ToArray();
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingWriteStream(innerStream, HashAlgorithmName.SHA256);

        await hashingStream.WriteAsync(testData.AsMemory(), CancellationToken.None);

        Assert.Equal(testData, innerStream.ToArray());

        var hash = hashingStream.CompleteHash();
        Assert.NotNull(hash);
        Assert.Equal(32, hash.Length);
    }

    [Fact]
    public async Task WriteAsync_WithOffset_WritesCorrectData()
    {
        var testData = "XXXHELLO"u8.ToArray();
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingWriteStream(innerStream, HashAlgorithmName.SHA256);

        await hashingStream.WriteAsync(testData, 3, 5, CancellationToken.None);

        Assert.Equal("HELLO"u8.ToArray(), innerStream.ToArray());
    }

    [Fact]
    public void CompleteHash_ComputesCorrectSHA256Hash()
    {
        var testData = "The quick brown fox jumps over the lazy dog"u8.ToArray();
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingWriteStream(innerStream, HashAlgorithmName.SHA256);

        hashingStream.Write(testData, 0, testData.Length);

        var hash = hashingStream.CompleteHash();

        // Verify against expected SHA256 hash
        using var sha256 = SHA256.Create();
        var expectedHash = sha256.ComputeHash(testData);

        Assert.Equal(expectedHash, hash);
    }

    [Fact]
    public void CompleteHash_ComputesCorrectSHA1Hash()
    {
        var testData = "The quick brown fox jumps over the lazy dog"u8.ToArray();
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingWriteStream(innerStream, HashAlgorithmName.SHA1);

        hashingStream.Write(testData, 0, testData.Length);

        var hash = hashingStream.CompleteHash();

        // Verify against expected SHA1 hash
        using var sha1 = SHA1.Create();
        var expectedHash = sha1.ComputeHash(testData);

        Assert.Equal(expectedHash, hash);
    }

    [Fact]
    public void CompleteHash_ThrowsInvalidOperationException_WhenCalledTwice()
    {
        var testData = "Hello"u8.ToArray();
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingWriteStream(innerStream, HashAlgorithmName.SHA256);

        hashingStream.Write(testData, 0, testData.Length);

        hashingStream.CompleteHash();

        Assert.Throws<InvalidOperationException>(() => hashingStream.CompleteHash());
    }

    [Fact]
    public void Flush_FlushesInnerStream()
    {
        var innerStream = new FlushTrackingStream();
        using var hashingStream = new HashingWriteStream(innerStream, HashAlgorithmName.SHA256);

        hashingStream.Flush();

        Assert.True(innerStream.FlushCalled);
    }

    [Fact]
    public async Task FlushAsync_FlushesInnerStream()
    {
        var innerStream = new FlushTrackingStream();
        using var hashingStream = new HashingWriteStream(innerStream, HashAlgorithmName.SHA256);

        await hashingStream.FlushAsync(CancellationToken.None);

        Assert.True(innerStream.FlushAsyncCalled);
    }

    [Fact]
    public void Dispose_DisposesInnerStream_WhenLeaveOpenIsFalse()
    {
        var innerStream = new MemoryStream();
        var hashingStream = new HashingWriteStream(innerStream, HashAlgorithmName.SHA256, leaveOpen: false);

        hashingStream.Dispose();

        Assert.Throws<ObjectDisposedException>(() => innerStream.WriteByte(1));
    }

    [Fact]
    public void Dispose_DoesNotDisposeInnerStream_WhenLeaveOpenIsTrue()
    {
        var innerStream = new MemoryStream();
        var hashingStream = new HashingWriteStream(innerStream, HashAlgorithmName.SHA256, leaveOpen: true);

        hashingStream.Dispose();

        // Should still be able to write to inner stream
        innerStream.WriteByte(1);
        Assert.Equal(1, innerStream.Length);
    }

    [Fact]
    public void CompleteHash_WithEmptyStream_ReturnsHashOfEmptyData()
    {
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingWriteStream(innerStream, HashAlgorithmName.SHA256);

        var hash = hashingStream.CompleteHash();

        // Verify hash of empty data
        using var sha256 = SHA256.Create();
        var expectedHash = sha256.ComputeHash(Array.Empty<byte>());

        Assert.Equal(expectedHash, hash);
    }

    [Fact]
    public void BytesWritten_InitiallyZero()
    {
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingWriteStream(innerStream, HashAlgorithmName.SHA256);

        Assert.Equal(0, hashingStream.BytesWritten);
    }

    [Fact]
    public void BytesWritten_UpdatesAfterSingleWrite()
    {
        var testData = "Hello, World!"u8.ToArray();
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingWriteStream(innerStream, HashAlgorithmName.SHA256);

        hashingStream.Write(testData, 0, testData.Length);

        Assert.Equal(testData.Length, hashingStream.BytesWritten);
        Assert.Equal(13, hashingStream.BytesWritten);
    }

    [Fact]
    public void BytesWritten_UpdatesAfterMultipleWrites()
    {
        var testData1 = "Hello, "u8.ToArray();
        var testData2 = "World!"u8.ToArray();
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingWriteStream(innerStream, HashAlgorithmName.SHA256);

        hashingStream.Write(testData1, 0, testData1.Length);
        Assert.Equal(7, hashingStream.BytesWritten);

        hashingStream.Write(testData2, 0, testData2.Length);
        Assert.Equal(13, hashingStream.BytesWritten);
    }

    [Fact]
    public void BytesWritten_UpdatesCorrectlyWithOffset()
    {
        var testData = "XXXHELLO"u8.ToArray();
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingWriteStream(innerStream, HashAlgorithmName.SHA256);

        hashingStream.Write(testData, 3, 5);

        Assert.Equal(5, hashingStream.BytesWritten);
    }

    [Fact]
    public async Task BytesWritten_UpdatesAfterWriteAsync()
    {
        var testData = "Hello, World!"u8.ToArray();
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingWriteStream(innerStream, HashAlgorithmName.SHA256);

        await hashingStream.WriteAsync(testData.AsMemory(), CancellationToken.None);

        Assert.Equal(testData.Length, hashingStream.BytesWritten);
        Assert.Equal(13, hashingStream.BytesWritten);
    }

    [Fact]
    public async Task BytesWritten_UpdatesAfterMultipleWriteAsync()
    {
        var testData1 = "Hello, "u8.ToArray();
        var testData2 = "World!"u8.ToArray();
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingWriteStream(innerStream, HashAlgorithmName.SHA256);

        await hashingStream.WriteAsync(testData1.AsMemory(), CancellationToken.None);
        Assert.Equal(7, hashingStream.BytesWritten);

        await hashingStream.WriteAsync(testData2.AsMemory(), CancellationToken.None);
        Assert.Equal(13, hashingStream.BytesWritten);
    }

    [Fact]
    public async Task BytesWritten_UpdatesWithMixedWriteAndWriteAsync()
    {
        var testData1 = "Hello, "u8.ToArray();
        var testData2 = "World!"u8.ToArray();
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingWriteStream(innerStream, HashAlgorithmName.SHA256);

        hashingStream.Write(testData1, 0, testData1.Length);
        Assert.Equal(7, hashingStream.BytesWritten);

        await hashingStream.WriteAsync(testData2.AsMemory(), CancellationToken.None);
        Assert.Equal(13, hashingStream.BytesWritten);
    }

    [Fact]
    public void BytesWritten_RemainsZeroForEmptyWrites()
    {
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingWriteStream(innerStream, HashAlgorithmName.SHA256);

        hashingStream.Write(Array.Empty<byte>(), 0, 0);

        Assert.Equal(0, hashingStream.BytesWritten);
    }

    [Fact]
    public async Task BytesWritten_RemainsZeroForEmptyWriteAsync()
    {
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingWriteStream(innerStream, HashAlgorithmName.SHA256);

        await hashingStream.WriteAsync(Memory<byte>.Empty, CancellationToken.None);

        Assert.Equal(0, hashingStream.BytesWritten);
    }

    [Fact]
    public void BytesWritten_AccumulatesCorrectlyForLargeData()
    {
        var testData = new byte[10000];
        Random.Shared.NextBytes(testData);
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingWriteStream(innerStream, HashAlgorithmName.SHA256);

        // Write in chunks
        hashingStream.Write(testData, 0, 3000);
        Assert.Equal(3000, hashingStream.BytesWritten);

        hashingStream.Write(testData, 3000, 4000);
        Assert.Equal(7000, hashingStream.BytesWritten);

        hashingStream.Write(testData, 7000, 3000);
        Assert.Equal(10000, hashingStream.BytesWritten);
    }

    [Fact]
    public async Task RoundTrip_WriteReadVerifyHash()
    {
        var testData = "The quick brown fox jumps over the lazy dog"u8.ToArray();
        using var storage = new MemoryStream();
        
        // Write data with hash
        byte[] writeHash;
        using (var writeStream = new HashingWriteStream(storage, HashAlgorithmName.SHA256, leaveOpen: true))
        {
            await writeStream.WriteAsync(testData.AsMemory(), CancellationToken.None);
            writeHash = writeStream.CompleteHash();
        }

        // Read data back with hash
        storage.Position = 0;
        byte[] readHash;
        var buffer = new byte[testData.Length];
        using (var readStream = new HashingReadStream(storage, HashAlgorithmName.SHA256, leaveOpen: true))
        {
            var bytesRead = await readStream.ReadAsync(buffer.AsMemory(), CancellationToken.None);
            Assert.Equal(testData.Length, bytesRead);
            readHash = readStream.CompleteHash();
        }

        // Verify hashes match
        Assert.Equal(writeHash, readHash);
        Assert.Equal(testData, buffer);
    }

    private sealed class FlushTrackingStream : MemoryStream
    {
        public bool FlushCalled { get; private set; }
        public bool FlushAsyncCalled { get; private set; }

        public override void Flush()
        {
            FlushCalled = true;
            base.Flush();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            FlushAsyncCalled = true;
            return base.FlushAsync(cancellationToken);
        }
    }
}
