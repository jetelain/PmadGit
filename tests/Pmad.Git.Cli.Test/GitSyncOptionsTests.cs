namespace Pmad.Git.Cli.Test;

/// <summary>
/// Unit tests for <see cref="GitSyncOptions"/>.
/// </summary>
public class GitSyncOptionsTests
{
    [Fact]
    public void Defaults_Are_As_Documented()
    {
        var options = new GitSyncOptions();

        Assert.Equal(TimeSpan.FromMinutes(5), options.PushDebounceDelay);
        Assert.Equal(TimeSpan.FromHours(1), options.PullInterval);
        Assert.Null(options.Remote);
        Assert.Null(options.Branch);
        Assert.Equal("git", options.GitCliPath);
    }

    [Fact]
    public void GitCliPath_Setter_Replaces_GitRunner()
    {
        var options = new GitSyncOptions();
        var originalRunner = options.GitRunner;

        options.GitCliPath = "custom-git";

        Assert.Equal("custom-git", options.GitCliPath);
        Assert.NotSame(originalRunner, options.GitRunner);
        Assert.Equal("custom-git", options.GitRunner.GitCliPath);
    }

    [Fact]
    public void GitRunner_Can_Be_Overridden_Directly_For_Tests()
    {
        var options = new GitSyncOptions();
        var fakeRunner = new Infrastructure.FakeGitRunner();

        options.GitRunner = fakeRunner;

        Assert.Same(fakeRunner, options.GitRunner);
        Assert.Equal(fakeRunner.GitCliPath, options.GitCliPath);
    }
}
