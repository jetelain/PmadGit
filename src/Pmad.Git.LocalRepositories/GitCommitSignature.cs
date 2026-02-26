using System;
using System.Globalization;

namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Represents a git signature (name, email, timestamp) used in commit and tag objects.
/// </summary>
public sealed record GitCommitSignature
{
    /// <summary>
    /// Invalid characters that are not allowed in the name or email of a git signature, as they can break the header format.
    /// </summary>
    private static readonly char[] InvalidCharacters = ['<', '>', '\n', '\r', '\0'];

    /// <summary>
    /// Gets the invalid characters that are not allowed in the name or email of a git signature, as they can break the header format.
    /// </summary>
    /// <returns>A read-only span of invalid characters.</returns>
    public static ReadOnlySpan<char> GetInvalidCharacters() => InvalidCharacters;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitCommitSignature"/> class.
    /// </summary>
    /// <param name="name">The name of the person signing.</param>
    /// <param name="email">The email address of the person signing.</param>
    /// <param name="timestamp">The timestamp when the signature was created.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> or <paramref name="email"/> is null, empty, whitespace, or contains invalid characters (&lt;, &gt;, \n, \r, or \0).</exception>
    public GitCommitSignature(string name, string email, DateTimeOffset timestamp)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Signature name cannot be empty", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Signature email cannot be empty", nameof(email));
        }

        if (ContainsInvalidCharacters(name))
        {
            throw new ArgumentException("Signature name cannot contain '<', '>', newline, or null characters", nameof(name));
        }

        if (ContainsInvalidCharacters(email))
        {
            throw new ArgumentException("Signature email cannot contain '<', '>', newline, or null characters", nameof(email));
        }

        Name = name.Trim();
        Email = email.Trim();
        Timestamp = timestamp;
    }

    private static bool ContainsInvalidCharacters(string value)
    {
        return value.IndexOfAny(InvalidCharacters) >= 0;
    }

    /// <summary>
    /// Gets the name of the person signing.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the email address of the person signing.
    /// </summary>
    public string Email { get; }

    /// <summary>
    /// Gets the timestamp when the signature was created.
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>
    /// Serializes the signature to the canonical git header format.
    /// </summary>
    public string ToHeaderValue()
    {
        var unixSeconds = Timestamp.ToUnixTimeSeconds();
        var offsetMinutes = (int)Timestamp.Offset.TotalMinutes;
        var sign = offsetMinutes >= 0 ? '+' : '-';
        var absolute = Math.Abs(offsetMinutes);
        var hours = absolute / 60;
        var minutes = absolute % 60;
        return $"{Name} <{Email}> {unixSeconds} {sign}{hours:00}{minutes:00}";
    }

    /// <summary>
    /// Parses a git header signature value ("Name &lt;email&gt; 123456 +0100").
    /// </summary>
    /// <param name="header">The git header signature string to parse.</param>
    /// <returns>A new <see cref="GitCommitSignature"/> instance parsed from the header.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="header"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the header format is invalid or missing required information.</exception>
    public static GitCommitSignature Parse(string header)
    {
        if (header is null)
        {
            throw new ArgumentNullException(nameof(header));
        }

        var ltIndex = header.IndexOf('<');
        var gtIndex = header.IndexOf('>', ltIndex + 1);
        if (ltIndex < 0 || gtIndex < 0)
        {
            throw new InvalidOperationException("Signature header is missing email information.");
        }

        var name = header[..ltIndex].Trim();
        var email = header[(ltIndex + 1)..gtIndex].Trim();
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(email))
        {
            throw new InvalidOperationException("Signature header must include a name and email address.");
        }

        var remainder = header[(gtIndex + 1)..].Trim();
        var timestamp = DateTimeOffset.UnixEpoch;
        if (!string.IsNullOrEmpty(remainder))
        {
            var parts = remainder.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 1 &&
                long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixSeconds))
            {
                var instant = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
                if (parts.Length >= 2 && TryParseOffset(parts[1], out var offset))
                {
                    timestamp = instant.ToOffset(offset);
                }
                else
                {
                    timestamp = instant;
                }
            }
        }

        return new GitCommitSignature(name, email, timestamp);
    }

    private static bool TryParseOffset(string value, out TimeSpan offset)
    {
        offset = default;
        if (string.IsNullOrEmpty(value) || value.Length != 5)
        {
            return false;
        }

        var sign = value[0];
        if (sign != '+' && sign != '-')
        {
            return false;
        }

        if (!int.TryParse(value.AsSpan(1, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours) ||
            !int.TryParse(value.AsSpan(3, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes))
        {
            return false;
        }

        var span = new TimeSpan(hours, minutes, 0);
        offset = sign == '-' ? -span : span;
        return true;
    }
}
