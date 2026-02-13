using System.Security.Cryptography;

namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Provides helper methods for working with Git hash algorithms and their associated properties.
/// </summary>
public static class GitHashHelper
{
    /// <summary>
    /// Gets the <see cref="HashAlgorithmName"/> corresponding to the specified hash length in bytes.
    /// </summary>
    /// <param name="hashLengthBytes">The length, in bytes, of the desired hash algorithm. Supported values are 20 for SHA-1 and 32 for SHA-256.</param>
    /// <returns>The corresponding <see cref="HashAlgorithmName"/>.</returns>
    /// <exception cref="NotSupportedException">Thrown when the hash length is not supported.</exception>
    public static HashAlgorithmName GetAlgorithmName(int hashLengthBytes) => hashLengthBytes switch
    {
        GitHash.Sha1ByteLength => HashAlgorithmName.SHA1,
        GitHash.Sha256ByteLength => HashAlgorithmName.SHA256,
        _ => throw new NotSupportedException("Unsupported git hash length")
    };

    /// <summary>
    /// Creates a hash algorithm instance corresponding to the specified hash length in bytes.
    /// </summary>
    /// <param name="hashLengthBytes">The length, in bytes, of the desired hash algorithm. Supported values are 20 for SHA-1 and 32 for SHA-256.</param>
    /// <returns>A HashAlgorithm instance corresponding to the specified hash length.</returns>
    /// <exception cref="NotSupportedException">Thrown when the hash length is not supported.</exception>
    public static HashAlgorithm CreateHashAlgorithm(int hashLengthBytes) => hashLengthBytes switch
    {
        GitHash.Sha1ByteLength => SHA1.Create(),
        GitHash.Sha256ByteLength => SHA256.Create(),
        _ => throw new NotSupportedException("Unsupported git object hash length.")
    };
}
