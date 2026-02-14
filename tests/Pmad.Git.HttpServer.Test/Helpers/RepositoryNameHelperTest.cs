using Pmad.Git.HttpServer.Helpers;

namespace Pmad.Git.HttpServer.Test.Helpers;

public sealed class RepositoryNameHelperTest
{
    #region Valid Repository Names

    [Fact]
    public void DefaultRepositoryNameValidator_WithValidAlphanumericName_ShouldReturnTrue()
    {
        // Arrange
        var name = "myrepo";

        // Act
        var result = RepositoryNameHelper.DefaultRepositoryNameValidator(name);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void DefaultRepositoryNameValidator_WithValidMixedCaseName_ShouldReturnTrue()
    {
        // Arrange
        var name = "MyRepo123";

        // Act
        var result = RepositoryNameHelper.DefaultRepositoryNameValidator(name);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void DefaultRepositoryNameValidator_WithHyphens_ShouldReturnTrue()
    {
        // Arrange
        var name = "my-repo-name";

        // Act
        var result = RepositoryNameHelper.DefaultRepositoryNameValidator(name);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void DefaultRepositoryNameValidator_WithUnderscores_ShouldReturnTrue()
    {
        // Arrange
        var name = "my_repo_name";

        // Act
        var result = RepositoryNameHelper.DefaultRepositoryNameValidator(name);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void DefaultRepositoryNameValidator_WithMixedValidCharacters_ShouldReturnTrue()
    {
        // Arrange
        var name = "My_Repo-123";

        // Act
        var result = RepositoryNameHelper.DefaultRepositoryNameValidator(name);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void DefaultRepositoryNameValidator_WithSingleForwardSlash_ShouldReturnTrue()
    {
        // Arrange
        var name = "org/repo";

        // Act
        var result = RepositoryNameHelper.DefaultRepositoryNameValidator(name);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void DefaultRepositoryNameValidator_WithMultipleForwardSlashes_ShouldReturnTrue()
    {
        // Arrange
        var name = "org/subgroup/project";

        // Act
        var result = RepositoryNameHelper.DefaultRepositoryNameValidator(name);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void DefaultRepositoryNameValidator_WithDeepNestedPath_ShouldReturnTrue()
    {
        // Arrange
        var name = "org/team/category/subcategory/project";

        // Act
        var result = RepositoryNameHelper.DefaultRepositoryNameValidator(name);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region Invalid Repository Names - Empty or Null

    [Fact]
    public void DefaultRepositoryNameValidator_WithNullName_ShouldReturnFalse()
    {
        // Arrange
        string? name = null;

        // Act
        var result = RepositoryNameHelper.DefaultRepositoryNameValidator(name!);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void DefaultRepositoryNameValidator_WithEmptyName_ShouldReturnFalse()
    {
        // Arrange
        var name = string.Empty;

        // Act
        var result = RepositoryNameHelper.DefaultRepositoryNameValidator(name);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void DefaultRepositoryNameValidator_WithWhitespaceOnly_ShouldReturnFalse()
    {
        // Arrange
        var name = "   ";

        // Act
        var result = RepositoryNameHelper.DefaultRepositoryNameValidator(name);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Invalid Repository Names - Leading/Trailing/Repeated Slashes

    [Fact]
    public void DefaultRepositoryNameValidator_WithLeadingSlash_ShouldReturnFalse()
    {
        // Arrange
        var name = "/repo";

        // Act
        var result = RepositoryNameHelper.DefaultRepositoryNameValidator(name);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void DefaultRepositoryNameValidator_WithTrailingSlash_ShouldReturnFalse()
    {
        // Arrange
        var name = "repo/";

        // Act
        var result = RepositoryNameHelper.DefaultRepositoryNameValidator(name);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void DefaultRepositoryNameValidator_WithLeadingAndTrailingSlash_ShouldReturnFalse()
    {
        // Arrange
        var name = "/repo/";

        // Act
        var result = RepositoryNameHelper.DefaultRepositoryNameValidator(name);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void DefaultRepositoryNameValidator_WithRepeatedSlashes_ShouldReturnFalse()
    {
        // Arrange
        var name = "org//repo";

        // Act
        var result = RepositoryNameHelper.DefaultRepositoryNameValidator(name);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void DefaultRepositoryNameValidator_WithMultipleRepeatedSlashes_ShouldReturnFalse()
    {
        // Arrange
        var name = "org///repo";

        // Act
        var result = RepositoryNameHelper.DefaultRepositoryNameValidator(name);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void DefaultRepositoryNameValidator_WithOnlySlashes_ShouldReturnFalse()
    {
        // Arrange
        var name = "///";

        // Act
        var result = RepositoryNameHelper.DefaultRepositoryNameValidator(name);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void DefaultRepositoryNameValidator_WithSingleSlash_ShouldReturnFalse()
    {
        // Arrange
        var name = "/";

        // Act
        var result = RepositoryNameHelper.DefaultRepositoryNameValidator(name);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Invalid Repository Names - Special Characters

    [Fact]
    public void DefaultRepositoryNameValidator_WithSpace_ShouldReturnFalse()
    {
        // Arrange
        var name = "my repo";

        // Act
        var result = RepositoryNameHelper.DefaultRepositoryNameValidator(name);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void DefaultRepositoryNameValidator_WithDot_ShouldReturnFalse()
    {
        // Arrange
        var name = "my.repo";

        // Act
        var result = RepositoryNameHelper.DefaultRepositoryNameValidator(name);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData("repo@name")]
    [InlineData("repo#name")]
    [InlineData("repo$name")]
    [InlineData("repo%name")]
    [InlineData("repo&name")]
    [InlineData("repo*name")]
    [InlineData("repo+name")]
    [InlineData("repo=name")]
    [InlineData("repo[name")]
    [InlineData("repo]name")]
    [InlineData("repo{name")]
    [InlineData("repo}name")]
    [InlineData("repo|name")]
    [InlineData("repo\\name")]
    [InlineData("repo:name")]
    [InlineData("repo;name")]
    [InlineData("repo\"name")]
    [InlineData("repo'name")]
    [InlineData("repo<name")]
    [InlineData("repo>name")]
    [InlineData("repo,name")]
    [InlineData("repo?name")]
    [InlineData("repo!name")]
    [InlineData("repo~name")]
    [InlineData("repo`name")]
    public void DefaultRepositoryNameValidator_WithSpecialCharacters_ShouldReturnFalse(string name)
    {
        // Act
        var result = RepositoryNameHelper.DefaultRepositoryNameValidator(name);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Invalid Repository Names - Security Concerns

    [Fact]
    public void DefaultRepositoryNameValidator_WithDotDot_ShouldReturnFalse()
    {
        // Arrange
        var name = "..";

        // Act
        var result = RepositoryNameHelper.DefaultRepositoryNameValidator(name);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void DefaultRepositoryNameValidator_WithDotDotSlash_ShouldReturnFalse()
    {
        // Arrange
        var name = "../repo";

        // Act
        var result = RepositoryNameHelper.DefaultRepositoryNameValidator(name);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void DefaultRepositoryNameValidator_WithPathTraversal_ShouldReturnFalse()
    {
        // Arrange
        var name = "../../etc/passwd";

        // Act
        var result = RepositoryNameHelper.DefaultRepositoryNameValidator(name);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void DefaultRepositoryNameValidator_WithBackslash_ShouldReturnFalse()
    {
        // Arrange
        var name = "repo\\name";

        // Act
        var result = RepositoryNameHelper.DefaultRepositoryNameValidator(name);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void DefaultRepositoryNameValidator_WithNullByte_ShouldReturnFalse()
    {
        // Arrange
        var name = "repo\0name";

        // Act
        var result = RepositoryNameHelper.DefaultRepositoryNameValidator(name);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void DefaultRepositoryNameValidator_WithSingleCharacter_ShouldReturnTrue()
    {
        // Arrange
        var name = "a";

        // Act
        var result = RepositoryNameHelper.DefaultRepositoryNameValidator(name);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void DefaultRepositoryNameValidator_WithSingleDigit_ShouldReturnTrue()
    {
        // Arrange
        var name = "1";

        // Act
        var result = RepositoryNameHelper.DefaultRepositoryNameValidator(name);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void DefaultRepositoryNameValidator_WithSingleHyphen_ShouldReturnTrue()
    {
        // Arrange
        var name = "-";

        // Act
        var result = RepositoryNameHelper.DefaultRepositoryNameValidator(name);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void DefaultRepositoryNameValidator_WithSingleUnderscore_ShouldReturnTrue()
    {
        // Arrange
        var name = "_";

        // Act
        var result = RepositoryNameHelper.DefaultRepositoryNameValidator(name);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void DefaultRepositoryNameValidator_WithVeryLongName_ShouldReturnTrue()
    {
        // Arrange
        var name = new string('a', 1000);

        // Act
        var result = RepositoryNameHelper.DefaultRepositoryNameValidator(name);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void DefaultRepositoryNameValidator_WithVeryLongPathWithSlashes_ShouldReturnTrue()
    {
        // Arrange
        var name = string.Join("/", Enumerable.Repeat("segment", 100));

        // Act
        var result = RepositoryNameHelper.DefaultRepositoryNameValidator(name);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region Unicode and International Characters

    [Fact]
    public void DefaultRepositoryNameValidator_WithUnicodeCharacters_ShouldReturnFalse()
    {
        // Arrange
        var name = "repo-名前";

        // Act
        var result = RepositoryNameHelper.DefaultRepositoryNameValidator(name);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void DefaultRepositoryNameValidator_WithEmoji_ShouldReturnFalse()
    {
        // Arrange
        var name = "repo-😀";

        // Act
        var result = RepositoryNameHelper.DefaultRepositoryNameValidator(name);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void DefaultRepositoryNameValidator_WithAccentedCharacters_ShouldReturnFalse()
    {
        // Arrange
        var name = "repó";

        // Act
        var result = RepositoryNameHelper.DefaultRepositoryNameValidator(name);

        // Assert
        Assert.False(result);
    }

    #endregion
}
