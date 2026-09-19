namespace Pmad.Git.Cli.Test;

public class GitDiffStatTests
{
    [Fact]
    public void ParseShortStat_BothInsertionsAndDeletions()
    {
        var stat = GitDiffStat.ParseShortStat(" 3 files changed, 15 insertions(+), 4 deletions(-)");
        Assert.Equal(3, stat.FilesChanged);
        Assert.Equal(15, stat.Insertions);
        Assert.Equal(4, stat.Deletions);
    }

    [Fact]
    public void ParseShortStat_InsertionsOnly()
    {
        var stat = GitDiffStat.ParseShortStat(" 1 file changed, 42 insertions(+)");
        Assert.Equal(1, stat.FilesChanged);
        Assert.Equal(42, stat.Insertions);
        Assert.Equal(0, stat.Deletions);
    }

    [Fact]
    public void ParseShortStat_DeletionsOnly()
    {
        var stat = GitDiffStat.ParseShortStat(" 2 files changed, 10 deletions(-)");
        Assert.Equal(2, stat.FilesChanged);
        Assert.Equal(0, stat.Insertions);
        Assert.Equal(10, stat.Deletions);
    }

    [Fact]
    public void ParseShortStat_EmptyOrNull()
    {
        var stat1 = GitDiffStat.ParseShortStat("");
        Assert.Equal(0, stat1.FilesChanged);
        Assert.Equal(0, stat1.Insertions);
        Assert.Equal(0, stat1.Deletions);

        var stat2 = GitDiffStat.ParseShortStat("   \n");
        Assert.Equal(0, stat2.FilesChanged);
        Assert.Equal(0, stat2.Insertions);
        Assert.Equal(0, stat2.Deletions);
    }
}
