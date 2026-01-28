using System.Security.Cryptography;
using Pmad.Git.HttpServer.Utilities;

namespace Pmad.Git.HttpServer.Test.Utilities;

public sealed class HashingReadStreamTest
{
    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenStreamIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new HashingReadStream(null!, HashAlgorithmName.SHA256));
    }

    [Fact]
    public void CanRead_ReturnsTrue_WhenInnerStreamCanRead()
    {
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingReadStream(innerStream, HashAlgorithmName.SHA256);

        Assert.True(hashingStream.CanRead);
    }

    [Fact]
    public void CanSeek_ReturnsFalse()
    {
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingReadStream(innerStream, HashAlgorithmName.SHA256);

        Assert.False(hashingStream.CanSeek);
    }

    [Fact]
    public void CanWrite_ReturnsFalse()
    {
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingReadStream(innerStream, HashAlgorithmName.SHA256);

        Assert.False(hashingStream.CanWrite);
    }

    [Fact]
    public void Length_ThrowsNotSupportedException()
    {
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingReadStream(innerStream, HashAlgorithmName.SHA256);

        Assert.Throws<NotSupportedException>(() => hashingStream.Length);
    }

    [Fact]
    public void Position_Get_ThrowsNotSupportedException()
    {
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingReadStream(innerStream, HashAlgorithmName.SHA256);

        Assert.Throws<NotSupportedException>(() => hashingStream.Position);
    }

    [Fact]
    public void Position_Set_ThrowsNotSupportedException()
    {
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingReadStream(innerStream, HashAlgorithmName.SHA256);

        Assert.Throws<NotSupportedException>(() => hashingStream.Position = 0);
    }

    [Fact]
    public void Flush_ThrowsNotSupportedException()
    {
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingReadStream(innerStream, HashAlgorithmName.SHA256);

        Assert.Throws<NotSupportedException>(() => hashingStream.Flush());
    }

    [Fact]
    public void Seek_ThrowsNotSupportedException()
    {
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingReadStream(innerStream, HashAlgorithmName.SHA256);

        Assert.Throws<NotSupportedException>(() => hashingStream.Seek(0, SeekOrigin.Begin));
    }

    [Fact]
    public void SetLength_ThrowsNotSupportedException()
    {
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingReadStream(innerStream, HashAlgorithmName.SHA256);

        Assert.Throws<NotSupportedException>(() => hashingStream.SetLength(0));
    }

    [Fact]
    public void Write_ThrowsNotSupportedException()
    {
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingReadStream(innerStream, HashAlgorithmName.SHA256);

        Assert.Throws<NotSupportedException>(() => hashingStream.Write(new byte[10], 0, 10));
    }

    [Fact]
    public void Read_UpdatesHashAndBytesRead()
    {
        var testData = "Hello, World!"u8.ToArray();
        using var innerStream = new MemoryStream(testData);
        using var hashingStream = new HashingReadStream(innerStream, HashAlgorithmName.SHA256);

        var buffer = new byte[testData.Length];
        var bytesRead = hashingStream.Read(buffer, 0, buffer.Length);

        Assert.Equal(testData.Length, bytesRead);
        Assert.Equal(testData.Length, hashingStream.BytesRead);
        Assert.Equal(testData, buffer);

        var hash = hashingStream.CompleteHash();
        Assert.NotNull(hash);
        Assert.Equal(32, hash.Length); // SHA256 produces 32 bytes
    }

    [Fact]
    public void Read_PartialReads_UpdatesHashCorrectly()
    {
        var testData = "Hello, World!"u8.ToArray();
        using var innerStream = new MemoryStream(testData);
        using var hashingStream = new HashingReadStream(innerStream, HashAlgorithmName.SHA256);

        var buffer = new byte[5];
        var firstRead = hashingStream.Read(buffer, 0, 5);
        Assert.Equal(5, firstRead);
        Assert.Equal(5, hashingStream.BytesRead);

        var secondRead = hashingStream.Read(buffer, 0, 5);
        Assert.Equal(5, secondRead);
        Assert.Equal(10, hashingStream.BytesRead);

        var hash = hashingStream.CompleteHash();

        // Verify hash matches expected value
        using var expectedHashStream = new MemoryStream(testData[..10]);
        using var sha256 = SHA256.Create();
        var expectedHash = sha256.ComputeHash(expectedHashStream);
        
        Assert.Equal(expectedHash, hash);
    }

    [Fact]
    public async Task ReadAsync_UpdatesHashAndBytesRead()
    {
        var testData = "Hello, World!"u8.ToArray();
        using var innerStream = new MemoryStream(testData);
        using var hashingStream = new HashingReadStream(innerStream, HashAlgorithmName.SHA256);

        var buffer = new byte[testData.Length];
        var bytesRead = await hashingStream.ReadAsync(buffer.AsMemory(), CancellationToken.None);

        Assert.Equal(testData.Length, bytesRead);
        Assert.Equal(testData.Length, hashingStream.BytesRead);
        Assert.Equal(testData, buffer);

        var hash = hashingStream.CompleteHash();
        Assert.NotNull(hash);
        Assert.Equal(32, hash.Length);
    }

    [Fact]
    public async Task ReadAsync_WithOffset_UpdatesHashCorrectly()
    {
        var testData = "Hello, World!"u8.ToArray();
        using var innerStream = new MemoryStream(testData);
        using var hashingStream = new HashingReadStream(innerStream, HashAlgorithmName.SHA256);

        var buffer = new byte[20];
        var bytesRead = await hashingStream.ReadAsync(buffer, 5, testData.Length, CancellationToken.None);

        Assert.Equal(testData.Length, bytesRead);
        Assert.Equal(testData.Length, hashingStream.BytesRead);
        Assert.Equal(testData, buffer[5..(5 + testData.Length)]);
    }

    [Fact]
    public void CompleteHash_ComputesCorrectSHA256Hash()
    {
        var testData = "The quick brown fox jumps over the lazy dog"u8.ToArray();
        using var innerStream = new MemoryStream(testData);
        using var hashingStream = new HashingReadStream(innerStream, HashAlgorithmName.SHA256);

        var buffer = new byte[testData.Length];
        hashingStream.Read(buffer, 0, buffer.Length);

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
        using var innerStream = new MemoryStream(testData);
        using var hashingStream = new HashingReadStream(innerStream, HashAlgorithmName.SHA1);

        var buffer = new byte[testData.Length];
        hashingStream.Read(buffer, 0, buffer.Length);

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
        using var innerStream = new MemoryStream(testData);
        using var hashingStream = new HashingReadStream(innerStream, HashAlgorithmName.SHA256);

        var buffer = new byte[testData.Length];
        hashingStream.Read(buffer, 0, buffer.Length);

        hashingStream.CompleteHash();

        Assert.Throws<InvalidOperationException>(() => hashingStream.CompleteHash());
    }

    [Fact]
    public void Read_AfterCompleteHash_DoesNotUpdateHash()
    {
        var testData = "Hello, World!"u8.ToArray();
        using var innerStream = new MemoryStream(testData);
        using var hashingStream = new HashingReadStream(innerStream, HashAlgorithmName.SHA256);

        var buffer = new byte[5];
        hashingStream.Read(buffer, 0, 5);

        var hash = hashingStream.CompleteHash();

        // Read more data after completing hash
        hashingStream.Read(buffer, 0, 5);

        // Verify hash only includes first 5 bytes
        using var sha256 = SHA256.Create();
        var expectedHash = sha256.ComputeHash(testData[..5]);

        Assert.Equal(expectedHash, hash);
    }

    [Fact]
    public void BytesRead_InitiallyZero()
    {
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingReadStream(innerStream, HashAlgorithmName.SHA256);

        Assert.Equal(0, hashingStream.BytesRead);
    }

    [Fact]
    public void BytesRead_TracksAllReads()
    {
        var testData = new byte[100];
        using var innerStream = new MemoryStream(testData);
        using var hashingStream = new HashingReadStream(innerStream, HashAlgorithmName.SHA256);

        var buffer = new byte[30];
        hashingStream.Read(buffer, 0, 30);
        Assert.Equal(30, hashingStream.BytesRead);

        hashingStream.Read(buffer, 0, 20);
        Assert.Equal(50, hashingStream.BytesRead);

        hashingStream.Read(buffer, 0, 30);
        Assert.Equal(80, hashingStream.BytesRead);
    }

    [Fact]
    public void Dispose_DisposesInnerStream_WhenLeaveOpenIsFalse()
    {
        var innerStream = new MemoryStream();
        var hashingStream = new HashingReadStream(innerStream, HashAlgorithmName.SHA256, leaveOpen: false);

        hashingStream.Dispose();

        Assert.Throws<ObjectDisposedException>(() => innerStream.ReadByte());
    }

    [Fact]
    public void Dispose_DoesNotDisposeInnerStream_WhenLeaveOpenIsTrue()
    {
        var innerStream = new MemoryStream([1, 2, 3]);
        var hashingStream = new HashingReadStream(innerStream, HashAlgorithmName.SHA256, leaveOpen: true);

        hashingStream.Dispose();

        // Should still be able to read from inner stream
        Assert.Equal(1, innerStream.ReadByte());
    }

    [Fact]
    public void Read_EmptyStream_ReturnsZero()
    {
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingReadStream(innerStream, HashAlgorithmName.SHA256);

        var buffer = new byte[10];
        var bytesRead = hashingStream.Read(buffer, 0, 10);

        Assert.Equal(0, bytesRead);
        Assert.Equal(0, hashingStream.BytesRead);
    }

    [Fact]
    public async Task ReadAsync_EmptyStream_ReturnsZero()
    {
        using var innerStream = new MemoryStream();
        using var hashingStream = new HashingReadStream(innerStream, HashAlgorithmName.SHA256);

        var buffer = new byte[10];
        var bytesRead = await hashingStream.ReadAsync(buffer.AsMemory(), CancellationToken.None);

        Assert.Equal(0, bytesRead);
        Assert.Equal(0, hashingStream.BytesRead);
    }
}
