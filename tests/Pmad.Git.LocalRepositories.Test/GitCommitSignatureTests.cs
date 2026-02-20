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

    [Theory]
    [InlineData("John<Doe")]
    [InlineData("John>Doe")]
    [InlineData("<JohnDoe")]
    [InlineData("JohnDoe>")]
    [InlineData("John<>Doe")]
    [InlineData("<>")]
    public void Constructor_NameWithAngleBrackets_Throws(string name)
    {
        var exception = Assert.Throws<ArgumentException>(() => new GitCommitSignature(name, "user@example.com", DateTimeOffset.UnixEpoch));
        Assert.Contains("'<'", exception.Message);
        Assert.Equal("name", exception.ParamName);
    }

    [Theory]
    [InlineData("user<@example.com")]
    [InlineData("user>@example.com")]
    [InlineData("<user@example.com")]
    [InlineData("user@example.com>")]
    [InlineData("user<>@example.com")]
    [InlineData("<>")]
    public void Constructor_EmailWithAngleBrackets_Throws(string email)
    {
        var exception = Assert.Throws<ArgumentException>(() => new GitCommitSignature("User", email, DateTimeOffset.UnixEpoch));
        Assert.Contains("'<'", exception.Message);
        Assert.Equal("email", exception.ParamName);
    }

    [Theory]
    [InlineData("John\nDoe")]
    [InlineData("John\rDoe")]
    [InlineData("John\r\nDoe")]
    [InlineData("John\0Doe")]
    [InlineData("\nJohnDoe")]
    [InlineData("JohnDoe\n")]
    public void Constructor_NameWithControlCharacters_Throws(string name)
    {
        var exception = Assert.Throws<ArgumentException>(() => new GitCommitSignature(name, "user@example.com", DateTimeOffset.UnixEpoch));
        Assert.Contains("newline", exception.Message);
        Assert.Equal("name", exception.ParamName);
    }

    [Theory]
    [InlineData("user\n@example.com")]
    [InlineData("user\r@example.com")]
    [InlineData("user\r\n@example.com")]
    [InlineData("user\0@example.com")]
    [InlineData("\nuser@example.com")]
    [InlineData("user@example.com\n")]
    public void Constructor_EmailWithControlCharacters_Throws(string email)
    {
        var exception = Assert.Throws<ArgumentException>(() => new GitCommitSignature("User", email, DateTimeOffset.UnixEpoch));
        Assert.Contains("newline", exception.Message);
        Assert.Equal("email", exception.ParamName);
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

    [Fact]
    public void Constructor_ValidNameWithSpecialCharacters_Succeeds()
    {
        // Other special characters should be allowed (though they may not be common)
        var signature = new GitCommitSignature("John O'Doe-Smith", "user@example.com", DateTimeOffset.UnixEpoch);
        Assert.Equal("John O'Doe-Smith", signature.Name);
    }

    [Fact]
    public void Constructor_ValidEmailWithSpecialCharacters_Succeeds()
    {
        // Other special characters should be allowed in email
        var signature = new GitCommitSignature("User", "user+tag@example.co.uk", DateTimeOffset.UnixEpoch);
        Assert.Equal("user+tag@example.co.uk", signature.Email);
    }

    [Fact]
    public void Constructor_NameWithAngleBrackets_PreventsFormatCorruption()
    {
        // Verify that if we somehow allowed angle brackets, it would corrupt the format
        // This test documents WHY we validate for angle brackets
        var validSignature = new GitCommitSignature("John Doe", "john@example.com", DateTimeOffset.UnixEpoch);
        var header = validSignature.ToHeaderValue();
        
        // Header should be parseable
        var parsed = GitCommitSignature.Parse(header);
        Assert.Equal("John Doe", parsed.Name);
        Assert.Equal("john@example.com", parsed.Email);
        
        // If we had a name with angle brackets, parsing would fail or produce wrong results
        // This is prevented by our validation
        Assert.Throws<ArgumentException>(() => new GitCommitSignature("John<Fake>", "john@example.com", DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void GetInvalidCharacters_ReturnsExpectedCharacters()
    {
        // Act
        var invalidChars = GitCommitSignature.GetInvalidCharacters();

        // Assert
        Assert.Equal(5, invalidChars.Length);
        Assert.True(invalidChars.Contains('<'));
        Assert.True(invalidChars.Contains('>'));
        Assert.True(invalidChars.Contains('\n'));
        Assert.True(invalidChars.Contains('\r'));
        Assert.True(invalidChars.Contains('\0'));
    }

    [Fact]
    public void GetInvalidCharacters_CanBeUsedForValidation()
    {
        // Arrange
        var invalidChars = GitCommitSignature.GetInvalidCharacters();
        var validName = "John Doe";
        var invalidName = "John<Doe";

        // Act & Assert
        Assert.Equal(-1, validName.AsSpan().IndexOfAny(invalidChars));
        Assert.True(invalidName.AsSpan().IndexOfAny(invalidChars) >= 0);
    }
}
