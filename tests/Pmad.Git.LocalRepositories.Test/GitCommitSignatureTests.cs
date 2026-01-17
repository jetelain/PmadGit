using System;
using Pmad.Git.LocalRepositories;
using Xunit;

namespace Pmad.Git.LocalRepositories.Test;

public sealed class GitCommitSignatureTests
{
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
    public void ToHeaderValue_UsesCorrectFormat()
    {
        var original = new GitCommitSignature("Bot", "bot@example.com", new DateTimeOffset(2024, 01, 02, 03, 04, 05, TimeSpan.FromHours(1.5)));

        Assert.Equal("Bot <bot@example.com> 1704159245 +0130", original.ToHeaderValue());
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
