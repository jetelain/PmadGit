using System;
using System.Linq;
using Pmad.Git.LocalRepositories;
using Xunit;

namespace Pmad.Git.LocalRepositories.Test;

public sealed class GitHashTests
{
    [Fact]
    public void Constructor_NormalizesHexValue()
    {
        var input = "0123456789ABCDEF0123456789ABCDEF01234567";
        var hash = new GitHash(input);
        Assert.Equal(input.ToLowerInvariant(), hash.Value);
    }

	[Fact]
	public void Constructor_AcceptsSha256Length()
	{
		var input = new string('a', GitHash.Sha256HexLength);
		var hash = new GitHash(input);
		Assert.Equal(input, hash.Value);
		Assert.Equal(GitHash.Sha256ByteLength, hash.ByteLength);
	}

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("0123456789abcdef0123456789abcdef0123456x")]
    public void Constructor_InvalidInput_Throws(string? value)
    {
        Assert.Throws<ArgumentException>(() => _ = new GitHash(value!));
    }

    [Fact]
    public void TryParse_ReturnsFalseForInvalidValue()
    {
        var result = GitHash.TryParse("invalid", out _);
        Assert.False(result);
    }

    [Fact]
    public void TryParse_NormalizesValidValue()
    {
        const string input = "abcdef0123456789abcdef0123456789abcdef01";
        var success = GitHash.TryParse(input.ToUpperInvariant(), out var hash);
        Assert.True(success);
        Assert.Equal(input, hash.Value);
    }

    [Fact]
    public void FromBytes_RoundTripsThroughToByteArray()
    {
        var bytes = Enumerable.Range(0, 20).Select(i => (byte)i).ToArray();
        var hash = GitHash.FromBytes(bytes);
        var roundtrip = hash.ToByteArray();
        Assert.Equal(bytes, roundtrip);
    }

	[Fact]
	public void FromBytes_SupportsSha256()
	{
		var bytes = Enumerable.Range(0, 32).Select(i => (byte)(i * 3)).ToArray();
		var hash = GitHash.FromBytes(bytes);
		Assert.Equal(bytes, hash.ToByteArray());
	}
}
