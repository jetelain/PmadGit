using Pmad.Git.LocalRepositories.Diff;
using Xunit;

namespace Pmad.Git.LocalRepositories.Test;

public class GitDiffStatTests
{
    [Fact]
    public void ToShortStat_Empty_ReturnsEmpty()
    {
        var stat = new GitDiffStat(0, 0, 0);
        Assert.Equal(string.Empty, stat.ToShortStat());
    }

    [Fact]
    public void ToShortStat_SingleFileSingleInsertion()
    {
        var stat = new GitDiffStat(1, 1, 0);
        Assert.Equal("1 file changed, 1 insertion(+)", stat.ToShortStat());
    }

    [Fact]
    public void ToShortStat_MultipleFilesMultipleInsertionsAndDeletions()
    {
        var stat = new GitDiffStat(3, 10, 5);
        Assert.Equal("3 files changed, 10 insertions(+), 5 deletions(-)", stat.ToShortStat());
    }

    [Fact]
    public void ToShortStat_OnlyDeletions()
    {
        var stat = new GitDiffStat(2, 0, 1);
        Assert.Equal("2 files changed, 1 deletion(-)", stat.ToShortStat());
    }

    [Fact]
    public void ParseShortStat_StandardOutput_ParsesCorrectly()
    {
        var text = " 3 files changed, 10 insertions(+), 5 deletions(-)";
        var stat = GitDiffStat.ParseShortStat(text);

        Assert.Equal(3, stat.FilesChanged);
        Assert.Equal(10, stat.Insertions);
        Assert.Equal(5, stat.Deletions);
    }

    [Fact]
    public void ParseShortStat_OnlyInsertions_ParsesCorrectly()
    {
        var text = " 1 file changed, 25 insertions(+)";
        var stat = GitDiffStat.ParseShortStat(text);

        Assert.Equal(1, stat.FilesChanged);
        Assert.Equal(25, stat.Insertions);
        Assert.Equal(0, stat.Deletions);
    }

    [Fact]
    public void ParseShortStat_EmptyOrNull_ReturnsZeroes()
    {
        var stat1 = GitDiffStat.ParseShortStat("");
        Assert.Equal(0, stat1.FilesChanged);

        var stat2 = GitDiffStat.ParseShortStat(null!);
        Assert.Equal(0, stat2.FilesChanged);
    }
}
