namespace Pmad.Git.Cli.Test;

public class GitMergeResultTests
{
    [Fact]
    public void IsSuccess_Result_Has_No_Conflicts()
    {
        var result = new GitMergeResult(true, Array.Empty<string>());

        Assert.True(result.IsSuccess);
        Assert.False(result.HasConflicts);
        Assert.Empty(result.ConflictedFiles);
    }

    [Fact]
    public void Conflicted_Result_Exposes_ConflictedFiles()
    {
        var result = new GitMergeResult(false, new[] { "README.md", "src/File.cs" });

        Assert.False(result.IsSuccess);
        Assert.True(result.HasConflicts);
        Assert.Equal(new[] { "README.md", "src/File.cs" }, result.ConflictedFiles);
    }
}
