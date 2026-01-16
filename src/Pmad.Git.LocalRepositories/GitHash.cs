using System;

namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Represents a Git object identifier in hexadecimal form (either SHA-1 or SHA-256).
/// </summary>
public readonly record struct GitHash
{
    public const int Sha1HexLength = 40;
    public const int Sha256HexLength = 64;
    public const int Sha1ByteLength = Sha1HexLength / 2;
    public const int Sha256ByteLength = Sha256HexLength / 2;

    /// <summary>
    /// Gets the normalized hexadecimal representation of the hash.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Initializes a new <see cref="GitHash"/> from a hexadecimal string.
    /// </summary>
    /// <param name="value">A 40 or 64 character hexadecimal string.</param>
    public GitHash(string value)
    {
        if (!TryNormalize(value, out var normalized))
        {
            throw new ArgumentException("Git hash must be a 40 or 64-character hexadecimal string", nameof(value));
        }

        Value = normalized;
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>
    /// Attempts to parse a hexadecimal string into a <see cref="GitHash"/>.
    /// </summary>
    /// <param name="value">Raw string that may contain a git hash.</param>
    /// <param name="hash">Resulting hash when the parse succeeds.</param>
    /// <returns><c>true</c> if the value represents a supported hash; otherwise <c>false</c>.</returns>
    public static bool TryParse(string? value, out GitHash hash)
    {
        if (TryNormalize(value, out var normalized))
        {
            hash = new GitHash(normalized);
            return true;
        }

        hash = default;
        return false;
    }

    private static bool TryNormalize(string? value, out string normalized)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            normalized = string.Empty;
            return false;
        }

        value = value.Trim();
        if (!IsSupportedHexLength(value.Length))
        {
            normalized = string.Empty;
            return false;
        }

        Span<char> buffer = stackalloc char[value.Length];
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (Uri.IsHexDigit(c))
            {
                buffer[i] = char.ToLowerInvariant(c);
            }
            else
            {
                normalized = string.Empty;
                return false;
            }
        }

        normalized = buffer.ToString();
        return true;
    }

    /// <summary>
    /// Creates a <see cref="GitHash"/> from raw bytes.
    /// </summary>
    /// <param name="bytes">Binary hash value (20 or 32 bytes).</param>
    /// <returns>The normalized <see cref="GitHash"/>.</returns>
    public static GitHash FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (!IsSupportedByteLength(bytes.Length))
        {
            throw new ArgumentException("Git hash must be 20 or 32 bytes", nameof(bytes));
        }

        Span<char> chars = stackalloc char[bytes.Length * 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            var b = bytes[i];
            chars[i * 2] = GetHexValue(b >> 4);
            chars[i * 2 + 1] = GetHexValue(b & 0x0F);
        }

        return new GitHash(chars.ToString());
    }

    private static char GetHexValue(int value) => value switch
    {
        < 10 => (char)('0' + value),
        _ => (char)('a' + (value - 10))
    };

    /// <summary>
    /// Converts the hash to its binary representation.
    /// </summary>
    /// <returns>A byte array containing the hash value.</returns>
    public byte[] ToByteArray()
    {
        var bytes = new byte[Value.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            var high = ParseNibble(Value[i * 2]) << 4;
            var low = ParseNibble(Value[i * 2 + 1]);
            bytes[i] = (byte)(high | low);
        }

        return bytes;
    }

    /// <summary>
    /// Gets the hash length in bytes (20 for SHA-1, 32 for SHA-256).
    /// </summary>
    public int ByteLength => Value.Length / 2;

    /// <summary>
    /// Determines whether the provided hex length is valid for git hashes.
    /// </summary>
    /// <param name="length">Length of the hex string.</param>
    /// <returns><c>true</c> when the length matches SHA-1 or SHA-256.</returns>
    public static bool IsSupportedHexLength(int length) => length == Sha1HexLength || length == Sha256HexLength;

    /// <summary>
    /// Determines whether the provided byte length is valid for git hashes.
    /// </summary>
    /// <param name="length">Length of the byte span.</param>
    /// <returns><c>true</c> when the length matches SHA-1 or SHA-256.</returns>
    public static bool IsSupportedByteLength(int length) => length == Sha1ByteLength || length == Sha256ByteLength;

    private static int ParseNibble(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => throw new FormatException("Invalid hexadecimal character")
    };
}
