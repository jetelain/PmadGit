using System;
using Pmad.Git.LocalRepositories;
using Xunit;

namespace Pmad.Git.LocalRepositories.Test;

public sealed class GitObjectTypeHelperTests
{
    [Theory]
    [InlineData("commit", GitObjectType.Commit)]
    [InlineData("tree", GitObjectType.Tree)]
    [InlineData("blob", GitObjectType.Blob)]
    [InlineData("tag", GitObjectType.Tag)]
    public void ParseType_KnownValues(string input, GitObjectType expected)
    {
        var result = GitObjectTypeHelper.ParseType(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseType_UnknownValue_Throws()
    {
        Assert.Throws<NotSupportedException>(() => GitObjectTypeHelper.ParseType("unknown"));
    }

    [Theory]
    [InlineData(GitObjectType.Commit, "commit")]
    [InlineData(GitObjectType.Tree, "tree")]
    [InlineData(GitObjectType.Blob, "blob")]
    [InlineData(GitObjectType.Tag, "tag")]
    public void GetObjectTypeName_KnownValues(GitObjectType type, string expected)
    {
        var result = GitObjectTypeHelper.GetObjectTypeName(type);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetObjectTypeName_InvalidValue_Throws()
    {
        Assert.Throws<NotSupportedException>(() => GitObjectTypeHelper.GetObjectTypeName((GitObjectType)999));
    }
}
