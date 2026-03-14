using Pmad.Git.LocalRepositories.Helpers;

namespace Pmad.Git.LocalRepositories.Test.Helpers;

public sealed class GitFileLastChangeHelperTests
{
    private static GitCommit MakeCommit(string hashHex)
    {
        var id = new GitHash(hashHex);
        var tree = new GitHash("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        return new GitCommit(id, tree, [], new Dictionary<string, string>(), string.Empty);
    }

    private static IReadOnlyList<GitFileLastChange> MakeSource()
    {
        var commit1 = MakeCommit("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var commit2 = MakeCommit("cccccccccccccccccccccccccccccccccccccccc");
        return new List<GitFileLastChange>
        {
            new("README.md",          commit1),
            new("src/Program.cs",     commit1),
            new("src/Utils.cs",       commit2),
            new("src/sub/Deep.cs",    commit2),
            new("docs/guide.md",      commit1),
        };
    }

    [Fact]
    public void ApplyFilters_NoFilters_ReturnsSameInstance()
    {
        var source = MakeSource();

        var result = GitFileLastChangeHelper.ApplyFilters(source, string.Empty, SearchOption.AllDirectories, null);

        Assert.Same(source, result);
    }

    [Fact]
    public void ApplyFilters_NullPrefix_AllDirectories_NoPredicate_ReturnsSameInstance()
    {
        var source = MakeSource();

        var result = GitFileLastChangeHelper.ApplyFilters(source, null!, SearchOption.AllDirectories, null);

        Assert.Same(source, result);
    }

    [Fact]
    public void ApplyFilters_WithPrefix_ReturnsOnlyMatchingPaths()
    {
        var source = MakeSource();

        var result = GitFileLastChangeHelper.ApplyFilters(source, "src", SearchOption.AllDirectories, null);

        Assert.Equal(3, result.Count);
        Assert.All(result, e => Assert.StartsWith("src/", e.Path, StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyFilters_WithPrefix_TopDirectoryOnly_ExcludesSubdirectories()
    {
        var source = MakeSource();

        var result = GitFileLastChangeHelper.ApplyFilters(source, "src", SearchOption.TopDirectoryOnly, null);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, e => e.Path == "src/Program.cs");
        Assert.Contains(result, e => e.Path == "src/Utils.cs");
        Assert.DoesNotContain(result, e => e.Path == "src/sub/Deep.cs");
    }

    [Fact]
    public void ApplyFilters_NoPrefix_TopDirectoryOnly_ReturnsOnlyRootFiles()
    {
        var source = MakeSource();

        var result = GitFileLastChangeHelper.ApplyFilters(source, string.Empty, SearchOption.TopDirectoryOnly, null);

        Assert.Equal(1, result.Count);
        Assert.Contains(result, e => e.Path == "README.md");
    }

    [Fact]
    public void ApplyFilters_WithPredicate_FiltersMatchingFiles()
    {
        var source = MakeSource();

        var result = GitFileLastChangeHelper.ApplyFilters(
            source, string.Empty, SearchOption.AllDirectories,
            path => path.EndsWith(".md", StringComparison.Ordinal));

        Assert.Equal(2, result.Count);
        Assert.Contains(result, e => e.Path == "README.md");
        Assert.Contains(result, e => e.Path == "docs/guide.md");
    }

    [Fact]
    public void ApplyFilters_WithPrefixAndPredicate_AppliesBothFilters()
    {
        var source = MakeSource();

        var result = GitFileLastChangeHelper.ApplyFilters(
            source, "src", SearchOption.AllDirectories,
            path => path.EndsWith(".cs", StringComparison.Ordinal));

        Assert.Equal(3, result.Count);
        Assert.All(result, e => Assert.EndsWith(".cs", e.Path, StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyFilters_NonMatchingPrefix_ReturnsEmptyList()
    {
        var source = MakeSource();

        var result = GitFileLastChangeHelper.ApplyFilters(source, "nonexistent", SearchOption.AllDirectories, null);

        Assert.Empty(result);
    }

    [Fact]
    public void ApplyFilters_PredicateMatchesNothing_ReturnsEmptyList()
    {
        var source = MakeSource();

        var result = GitFileLastChangeHelper.ApplyFilters(
            source, string.Empty, SearchOption.AllDirectories,
            _ => false);

        Assert.Empty(result);
    }

    [Fact]
    public void ApplyFilters_EmptySource_ReturnsEmptyList()
    {
        var source = new List<GitFileLastChange>();

        var result = GitFileLastChangeHelper.ApplyFilters(source, "src", SearchOption.AllDirectories, null);

        Assert.Empty(result);
    }

    [Fact]
    public void ApplyFilters_PreservesCommitReference()
    {
        var commit = MakeCommit("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var source = new List<GitFileLastChange> { new("src/file.cs", commit) };

        var result = GitFileLastChangeHelper.ApplyFilters(source, "src", SearchOption.AllDirectories, null);

        Assert.Same(commit, result[0].Commit);
    }
}
