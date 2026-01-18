using System;
using Pmad.Git.LocalRepositories;
using Xunit;

namespace Pmad.Git.LocalRepositories.Test;

public sealed class GitCommitSignatureTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_InvalidName_Throws(string? name)
    {
        Assert.Throws<ArgumentException>(() => new GitCommitSignature(name!, "user@example.com", DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_InvalidEmail_Throws(string? email)
    {
        Assert.Throws<ArgumentException>(() => new GitCommitSignature("User", email!, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Parse_NullHeader_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => GitCommitSignature.Parse(null!));
    }

    [Fact]
    public void Parse_MissingEmailDelimiters_Throws()
    {
        const string header = "Jane Doe jane@example.com 1 +0000";
        Assert.Throws<InvalidOperationException>(() => GitCommitSignature.Parse(header));
    }

    [Fact]
    public void Parse_BlankName_Throws()
    {
        const string header = "   <jane@example.com> 1 +0000";
        Assert.Throws<InvalidOperationException>(() => GitCommitSignature.Parse(header));
    }

    [Fact]
    public void Parse_BlankEmail_Throws()
    {
        const string header = "Jane Doe <> 1 +0000";
        Assert.Throws<InvalidOperationException>(() => GitCommitSignature.Parse(header));
    }

    [Fact]
    public void Parse_WithoutTimestamp_DefaultsToUnixEpoch()
    {
        const string header = "Jane Doe <jane@example.com>";

        var signature = GitCommitSignature.Parse(header);

        Assert.Equal(DateTimeOffset.UnixEpoch, signature.Timestamp);
    }

    [Fact]
    public void Parse_ReadsNameEmailAndTimestamp()
    {
        var header = "Jane Doe <jane@example.com> 1700000000 -0230";

        var signature = GitCommitSignature.Parse(header);

        Assert.Equal("Jane Doe", signature.Name);
        Assert.Equal("jane@example.com", signature.Email);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000).ToOffset(new TimeSpan(-2, 30, 0)), signature.Timestamp);
    }

    [Fact]
    public void Parse_ReadsNameEmailAndTimestampNoOffset()
    {
        var header = "Jane Doe <jane@example.com> 1700000000";

        var signature = GitCommitSignature.Parse(header);

        Assert.Equal("Jane Doe", signature.Name);
        Assert.Equal("jane@example.com", signature.Email);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), signature.Timestamp);
    }

    [Fact]
    public void Parse_InvalidFormat()
    {
        var header = "Jane <Doe> <jane@example.com> 1700000000 -0230";

        var signature = GitCommitSignature.Parse(header);

        Assert.Equal("Jane", signature.Name);
        Assert.Equal("Doe", signature.Email);
        Assert.Equal(DateTimeOffset.UnixEpoch, signature.Timestamp);
    }

    [Fact]
    public void ToHeaderValue_UsesCorrectFormat()
    {
        var original = new GitCommitSignature("Bot", "bot@example.com", new DateTimeOffset(2024, 01, 02, 03, 04, 05, TimeSpan.FromHours(1.5)));

        Assert.Equal("Bot <bot@example.com> 1704159245 +0130", original.ToHeaderValue());
    }

    [Fact]
    public void ToHeaderValue_UsesCorrectFormat_NegativeOffset()
    {
        var original = new GitCommitSignature("Bot", "bot@example.com", new DateTimeOffset(2024, 01, 02, 03, 04, 05, TimeSpan.FromHours(-1.5)));

        Assert.Equal("Bot <bot@example.com> 1704170045 -0130", original.ToHeaderValue());
    }

    [Fact]
    public void ToHeaderValue_RoundTripsThroughParse()
    {
        var original = new GitCommitSignature("Bot", "bot@example.com", new DateTimeOffset(2024, 01, 02, 03, 04, 05, TimeSpan.FromHours(1.5)));

        var parsed = GitCommitSignature.Parse(original.ToHeaderValue());

        Assert.Equal(original.Name, parsed.Name);
        Assert.Equal(original.Email, parsed.Email);
        Assert.Equal(original.Timestamp, parsed.Timestamp);
    }
}
