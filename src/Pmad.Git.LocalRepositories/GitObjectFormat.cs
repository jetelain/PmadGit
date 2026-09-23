namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Specifies the object format (hash algorithm) used by a Git repository.
/// </summary>
public enum GitObjectFormat
{
    /// <summary>
    /// The standard SHA-1 hash algorithm (160-bit / 20-byte hashes).
    /// </summary>
    Sha1 = 0,

    /// <summary>
    /// The SHA-256 hash algorithm (256-bit / 32-byte hashes).
    /// </summary>
    Sha256 = 1
}

/// <summary>
/// Extension and helper methods for <see cref="GitObjectFormat"/>.
/// </summary>
public static class GitObjectFormatExtensions
{
    /// <summary>
    /// Gets the standard lowercase format name string used in Git configurations and protocol capabilities (e.g. "sha1" or "sha256").
    /// </summary>
    /// <param name="format">The object format.</param>
    /// <returns>The lowercase format name.</returns>
    public static string ToFormatName(this GitObjectFormat format) => format switch
    {
        GitObjectFormat.Sha256 => "sha256",
        _ => "sha1"
    };

    /// <summary>
    /// Gets the hash length in bytes for the specified object format.
    /// </summary>
    /// <param name="format">The object format.</param>
    /// <returns>20 for SHA-1, 32 for SHA-256.</returns>
    public static int GetHashLengthBytes(this GitObjectFormat format) => format switch
    {
        GitObjectFormat.Sha256 => GitHash.Sha256ByteLength,
        _ => GitHash.Sha1ByteLength
    };

    /// <summary>
    /// Attempts to parse a Git object format name string (case-insensitive).
    /// </summary>
    /// <param name="value">The format string to parse (e.g. "sha1" or "sha256").</param>
    /// <param name="format">When this method returns, contains the parsed <see cref="GitObjectFormat"/>.</param>
    /// <returns><see langword="true"/> if successfully parsed; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse(string? value, out GitObjectFormat format)
    {
        if (string.Equals(value, "sha1", StringComparison.OrdinalIgnoreCase))
        {
            format = GitObjectFormat.Sha1;
            return true;
        }
        if (string.Equals(value, "sha256", StringComparison.OrdinalIgnoreCase))
        {
            format = GitObjectFormat.Sha256;
            return true;
        }
        format = GitObjectFormat.Sha1;
        return false;
    }
}

