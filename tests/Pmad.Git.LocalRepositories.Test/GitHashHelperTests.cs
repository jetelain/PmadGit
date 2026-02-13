using System.Security.Cryptography;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.LocalRepositories.Test;

/// <summary>
/// Tests for GitHashHelper class methods.
/// </summary>
public sealed class GitHashHelperTests
{
    #region GetAlgorithmName Tests

    [Fact]
    public void GetAlgorithmName_WithSha1Length_ReturnsSHA1()
    {
        // Arrange
        var hashLength = GitHash.Sha1ByteLength; // 20 bytes

        // Act
        var algorithmName = GitHashHelper.GetAlgorithmName(hashLength);

        // Assert
        Assert.Equal(HashAlgorithmName.SHA1, algorithmName);
    }

    [Fact]
    public void GetAlgorithmName_WithSha256Length_ReturnsSHA256()
    {
        // Arrange
        var hashLength = GitHash.Sha256ByteLength; // 32 bytes

        // Act
        var algorithmName = GitHashHelper.GetAlgorithmName(hashLength);

        // Assert
        Assert.Equal(HashAlgorithmName.SHA256, algorithmName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(19)]
    [InlineData(21)]
    [InlineData(24)]
    [InlineData(28)]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(40)]
    [InlineData(64)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void GetAlgorithmName_WithUnsupportedLength_ThrowsNotSupportedException(int hashLength)
    {
        // Act & Assert
        var exception = Assert.Throws<NotSupportedException>(
            () => GitHashHelper.GetAlgorithmName(hashLength));

        Assert.Contains("Unsupported git hash length", exception.Message);
    }

    [Fact]
    public void GetAlgorithmName_ReturnsConsistentResults()
    {
        // Arrange & Act
        var result1 = GitHashHelper.GetAlgorithmName(GitHash.Sha1ByteLength);
        var result2 = GitHashHelper.GetAlgorithmName(GitHash.Sha1ByteLength);

        // Assert
        Assert.Equal(result1, result2);
    }

    #endregion

    #region CreateHashAlgorithm Tests

    [Fact]
    public void CreateHashAlgorithm_WithSha1Length_ReturnsSHA1Instance()
    {
        // Arrange
        var hashLength = GitHash.Sha1ByteLength;

        // Act
        using var algorithm = GitHashHelper.CreateHashAlgorithm(hashLength);

        // Assert
        Assert.NotNull(algorithm);
        Assert.IsAssignableFrom<SHA1>(algorithm);
        Assert.Equal(160, algorithm.HashSize); // SHA-1 produces 160-bit hashes
    }

    [Fact]
    public void CreateHashAlgorithm_WithSha256Length_ReturnsSHA256Instance()
    {
        // Arrange
        var hashLength = GitHash.Sha256ByteLength;

        // Act
        using var algorithm = GitHashHelper.CreateHashAlgorithm(hashLength);

        // Assert
        Assert.NotNull(algorithm);
        Assert.IsAssignableFrom<SHA256>(algorithm);
        Assert.Equal(256, algorithm.HashSize); // SHA-256 produces 256-bit hashes
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(19)]
    [InlineData(21)]
    [InlineData(24)]
    [InlineData(28)]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(40)]
    [InlineData(64)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void CreateHashAlgorithm_WithUnsupportedLength_ThrowsNotSupportedException(int hashLength)
    {
        // Act & Assert
        var exception = Assert.Throws<NotSupportedException>(
            () => GitHashHelper.CreateHashAlgorithm(hashLength));

        Assert.Contains("Unsupported git object hash length", exception.Message);
    }

    [Fact]
    public void CreateHashAlgorithm_SHA1_ProducesCorrectHashLength()
    {
        // Arrange
        using var algorithm = GitHashHelper.CreateHashAlgorithm(GitHash.Sha1ByteLength);
        var testData = "test data"u8.ToArray();

        // Act
        var hash = algorithm.ComputeHash(testData);

        // Assert
        Assert.Equal(GitHash.Sha1ByteLength, hash.Length);
    }

    [Fact]
    public void CreateHashAlgorithm_SHA256_ProducesCorrectHashLength()
    {
        // Arrange
        using var algorithm = GitHashHelper.CreateHashAlgorithm(GitHash.Sha256ByteLength);
        var testData = "test data"u8.ToArray();

        // Act
        var hash = algorithm.ComputeHash(testData);

        // Assert
        Assert.Equal(GitHash.Sha256ByteLength, hash.Length);
    }

    [Fact]
    public void CreateHashAlgorithm_SHA1_ProducesExpectedHash()
    {
        // Arrange
        using var algorithm = GitHashHelper.CreateHashAlgorithm(GitHash.Sha1ByteLength);
        var testData = "hello world"u8.ToArray();

        // Act
        var hash = algorithm.ComputeHash(testData);
        var hexHash = Convert.ToHexString(hash).ToLowerInvariant();

        // Assert - SHA-1 of "hello world" is a known value
        Assert.Equal("2aae6c35c94fcfb415dbe95f408b9ce91ee846ed", hexHash);
    }

    [Fact]
    public void CreateHashAlgorithm_SHA256_ProducesExpectedHash()
    {
        // Arrange
        using var algorithm = GitHashHelper.CreateHashAlgorithm(GitHash.Sha256ByteLength);
        var testData = "hello world"u8.ToArray();

        // Act
        var hash = algorithm.ComputeHash(testData);
        var hexHash = Convert.ToHexString(hash).ToLowerInvariant();

        // Assert - SHA-256 of "hello world" is a known value
        Assert.Equal("b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9", hexHash);
    }

    [Fact]
    public void CreateHashAlgorithm_CreatesNewInstanceEachTime()
    {
        // Arrange & Act
        using var algorithm1 = GitHashHelper.CreateHashAlgorithm(GitHash.Sha1ByteLength);
        using var algorithm2 = GitHashHelper.CreateHashAlgorithm(GitHash.Sha1ByteLength);

        // Assert - Different instances
        Assert.NotSame(algorithm1, algorithm2);
    }

    [Fact]
    public void CreateHashAlgorithm_CanBeUsedMultipleTimes()
    {
        // Arrange
        using var algorithm = GitHashHelper.CreateHashAlgorithm(GitHash.Sha1ByteLength);
        var data1 = "first"u8.ToArray();
        var data2 = "second"u8.ToArray();

        // Act
        var hash1 = algorithm.ComputeHash(data1);
        var hash2 = algorithm.ComputeHash(data2);

        // Assert
        Assert.Equal(GitHash.Sha1ByteLength, hash1.Length);
        Assert.Equal(GitHash.Sha1ByteLength, hash2.Length);
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void CreateHashAlgorithm_CanHandleEmptyData()
    {
        // Arrange
        using var algorithm = GitHashHelper.CreateHashAlgorithm(GitHash.Sha1ByteLength);
        var emptyData = Array.Empty<byte>();

        // Act
        var hash = algorithm.ComputeHash(emptyData);

        // Assert
        Assert.Equal(GitHash.Sha1ByteLength, hash.Length);
        // SHA-1 of empty string
        var hexHash = Convert.ToHexString(hash).ToLowerInvariant();
        Assert.Equal("da39a3ee5e6b4b0d3255bfef95601890afd80709", hexHash);
    }

    [Fact]
    public void CreateHashAlgorithm_CanHandleLargeData()
    {
        // Arrange
        using var algorithm = GitHashHelper.CreateHashAlgorithm(GitHash.Sha256ByteLength);
        var largeData = new byte[1024 * 1024]; // 1MB
        Array.Fill(largeData, (byte)0x42);

        // Act
        var hash = algorithm.ComputeHash(largeData);

        // Assert
        Assert.Equal(GitHash.Sha256ByteLength, hash.Length);
        Assert.NotEqual(new byte[32], hash); // Should not be all zeros
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void GetAlgorithmName_And_CreateHashAlgorithm_AreConsistent_ForSha1()
    {
        // Arrange
        var hashLength = GitHash.Sha1ByteLength;

        // Act
        var algorithmName = GitHashHelper.GetAlgorithmName(hashLength);
        using var algorithm = GitHashHelper.CreateHashAlgorithm(hashLength);

        // Assert
        Assert.Equal(HashAlgorithmName.SHA1, algorithmName);
        Assert.IsAssignableFrom<SHA1>(algorithm);
    }

    [Fact]
    public void GetAlgorithmName_And_CreateHashAlgorithm_AreConsistent_ForSha256()
    {
        // Arrange
        var hashLength = GitHash.Sha256ByteLength;

        // Act
        var algorithmName = GitHashHelper.GetAlgorithmName(hashLength);
        using var algorithm = GitHashHelper.CreateHashAlgorithm(hashLength);

        // Assert
        Assert.Equal(HashAlgorithmName.SHA256, algorithmName);
        Assert.IsAssignableFrom<SHA256>(algorithm);
    }

    [Fact]
    public void CreateHashAlgorithm_ProducesHashMatchingGitHashLength_Sha1()
    {
        // Arrange
        using var algorithm = GitHashHelper.CreateHashAlgorithm(GitHash.Sha1ByteLength);
        var testData = "test"u8.ToArray();

        // Act
        var hash = algorithm.ComputeHash(testData);
        var gitHash = GitHash.FromBytes(hash);

        // Assert
        Assert.Equal(GitHash.Sha1ByteLength, gitHash.ByteLength);
        Assert.Equal(GitHash.Sha1HexLength, gitHash.Value.Length);
    }

    [Fact]
    public void CreateHashAlgorithm_ProducesHashMatchingGitHashLength_Sha256()
    {
        // Arrange
        using var algorithm = GitHashHelper.CreateHashAlgorithm(GitHash.Sha256ByteLength);
        var testData = "test"u8.ToArray();

        // Act
        var hash = algorithm.ComputeHash(testData);
        var gitHash = GitHash.FromBytes(hash);

        // Assert
        Assert.Equal(GitHash.Sha256ByteLength, gitHash.ByteLength);
        Assert.Equal(GitHash.Sha256HexLength, gitHash.Value.Length);
    }

    #endregion

    #region Edge Cases and Thread Safety

    [Fact]
    public void CreateHashAlgorithm_CanBeCalledConcurrently()
    {
        // Arrange
        const int threadCount = 10;
        var tasks = new Task[threadCount];

        // Act
        for (int i = 0; i < threadCount; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                using var algorithm = GitHashHelper.CreateHashAlgorithm(GitHash.Sha1ByteLength);
                var hash = algorithm.ComputeHash("concurrent test"u8.ToArray());
                Assert.Equal(GitHash.Sha1ByteLength, hash.Length);
            });
        }

        // Assert
        Task.WaitAll(tasks);
    }

    [Fact]
    public void GetAlgorithmName_CanBeCalledConcurrently()
    {
        // Arrange
        const int threadCount = 10;
        var tasks = new Task[threadCount];

        // Act
        for (int i = 0; i < threadCount; i++)
        {
            var hashLength = i % 2 == 0 ? GitHash.Sha1ByteLength : GitHash.Sha256ByteLength;
            tasks[i] = Task.Run(() =>
            {
                var name = GitHashHelper.GetAlgorithmName(hashLength);
                Assert.NotEqual(default, name);
            });
        }

        // Assert
        Task.WaitAll(tasks);
    }

    [Fact]
    public void CreateHashAlgorithm_Sha1_ProducesDeterministicResults()
    {
        // Arrange
        var testData = "deterministic test"u8.ToArray();

        // Act
        byte[] hash1, hash2;
        using (var algorithm1 = GitHashHelper.CreateHashAlgorithm(GitHash.Sha1ByteLength))
        {
            hash1 = algorithm1.ComputeHash(testData);
        }
        using (var algorithm2 = GitHashHelper.CreateHashAlgorithm(GitHash.Sha1ByteLength))
        {
            hash2 = algorithm2.ComputeHash(testData);
        }

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void CreateHashAlgorithm_Sha256_ProducesDeterministicResults()
    {
        // Arrange
        var testData = "deterministic test"u8.ToArray();

        // Act
        byte[] hash1, hash2;
        using (var algorithm1 = GitHashHelper.CreateHashAlgorithm(GitHash.Sha256ByteLength))
        {
            hash1 = algorithm1.ComputeHash(testData);
        }
        using (var algorithm2 = GitHashHelper.CreateHashAlgorithm(GitHash.Sha256ByteLength))
        {
            hash2 = algorithm2.ComputeHash(testData);
        }

        // Assert
        Assert.Equal(hash1, hash2);
    }

    #endregion

    #region Error Message Consistency Tests

    [Fact]
    public void GetAlgorithmName_ErrorMessage_IsDescriptive()
    {
        // Act & Assert
        var exception = Assert.Throws<NotSupportedException>(
            () => GitHashHelper.GetAlgorithmName(999));

        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
        Assert.Contains("hash", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateHashAlgorithm_ErrorMessage_IsDescriptive()
    {
        // Act & Assert
        var exception = Assert.Throws<NotSupportedException>(
            () => GitHashHelper.CreateHashAlgorithm(999));

        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
        Assert.Contains("hash", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetAlgorithmName_And_CreateHashAlgorithm_HaveDifferentErrorMessages()
    {
        // Act
        var exception1 = Assert.Throws<NotSupportedException>(
            () => GitHashHelper.GetAlgorithmName(999));
        var exception2 = Assert.Throws<NotSupportedException>(
            () => GitHashHelper.CreateHashAlgorithm(999));

        // Assert - Messages should be different but both descriptive
        Assert.NotEqual(exception1.Message, exception2.Message);
    }

    #endregion

    #region Boundary Value Tests

    [Theory]
    [InlineData(GitHash.Sha1ByteLength)]
    [InlineData(GitHash.Sha256ByteLength)]
    public void GetAlgorithmName_WithExactValidLengths_Succeeds(int hashLength)
    {
        // Act
        var algorithmName = GitHashHelper.GetAlgorithmName(hashLength);

        // Assert
        Assert.NotEqual(default, algorithmName);
    }

    [Theory]
    [InlineData(GitHash.Sha1ByteLength - 1)]
    [InlineData(GitHash.Sha1ByteLength + 1)]
    [InlineData(GitHash.Sha256ByteLength - 1)]
    [InlineData(GitHash.Sha256ByteLength + 1)]
    public void GetAlgorithmName_WithNearValidLengths_ThrowsNotSupportedException(int hashLength)
    {
        // Act & Assert
        Assert.Throws<NotSupportedException>(
            () => GitHashHelper.GetAlgorithmName(hashLength));
    }

    [Theory]
    [InlineData(GitHash.Sha1ByteLength)]
    [InlineData(GitHash.Sha256ByteLength)]
    public void CreateHashAlgorithm_WithExactValidLengths_Succeeds(int hashLength)
    {
        // Act
        using var algorithm = GitHashHelper.CreateHashAlgorithm(hashLength);

        // Assert
        Assert.NotNull(algorithm);
    }

    [Theory]
    [InlineData(GitHash.Sha1ByteLength - 1)]
    [InlineData(GitHash.Sha1ByteLength + 1)]
    [InlineData(GitHash.Sha256ByteLength - 1)]
    [InlineData(GitHash.Sha256ByteLength + 1)]
    public void CreateHashAlgorithm_WithNearValidLengths_ThrowsNotSupportedException(int hashLength)
    {
        // Act & Assert
        Assert.Throws<NotSupportedException>(
            () => GitHashHelper.CreateHashAlgorithm(hashLength));
    }

    #endregion
}
