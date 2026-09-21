using System.IO;
using System.Threading.Tasks;
using Pmad.Git.LocalRepositories.Config;
using Xunit;

namespace Pmad.Git.LocalRepositories.Test;

public sealed class GitConfigFileTests
{
    private const string SampleConfig = """
        # Core settings
        [core]
        	repositoryformatversion = 0
        	filemode = false
        	bare = false
        	logallrefupdates = true

        ; Remote origin definition
        [remote "origin"]
        	url = "https://github.com/user/repo.git"
        	fetch = +refs/heads/*:refs/remotes/origin/*

        [branch "main"]
        	remote = origin
        	merge = refs/heads/main
        """;

    [Fact]
    public void Parse_ReadsSectionsAndValues()
    {
        var config = GitConfigFile.Parse(SampleConfig);

        Assert.Equal("0", config.GetValue("core.repositoryformatversion"));
        Assert.Equal("false", config.GetValue("core.bare"));
        Assert.Equal("https://github.com/user/repo.git", config.GetValue("remote.origin.url"));
        Assert.Equal("+refs/heads/*:refs/remotes/origin/*", config.GetValue("remote.origin.fetch"));
        Assert.Equal("origin", config.GetValue("branch.main.remote"));
        Assert.Equal("refs/heads/main", config.GetValue("branch.main.merge"));
        Assert.Null(config.GetValue("core.nonexistent"));
    }

    [Fact]
    public void Parse_IsCaseInsensitiveForSectionAndKey_CaseSensitiveForSubsection()
    {
        var config = GitConfigFile.Parse(SampleConfig);

        Assert.Equal("false", config.GetValue("CORE.BARE"));
        Assert.Equal("origin", config.GetValue("BRANCH", "main", "REMOTE"));
        Assert.Null(config.GetValue("BRANCH", "MAIN", "REMOTE")); // Subsection is case-sensitive
    }

    [Fact]
    public void SetValue_UpdatesExistingKeyPreservingOthers()
    {
        var config = GitConfigFile.Parse(SampleConfig);
        config.SetValue("core.bare", "true");

        Assert.Equal("true", config.GetValue("core.bare"));
        Assert.Equal("0", config.GetValue("core.repositoryformatversion"));
    }

    [Fact]
    public void SetValue_AddsNewKeyToExistingSection()
    {
        var config = GitConfigFile.Parse(SampleConfig);
        config.SetValue("core.autocrlf", "input");

        Assert.Equal("input", config.GetValue("core.autocrlf"));
    }

    [Fact]
    public void SetValue_AddsNewSectionAndKey()
    {
        var config = GitConfigFile.Parse(SampleConfig);
        config.SetValue("user.name", "Jane Doe");

        Assert.Equal("Jane Doe", config.GetValue("user.name"));
        var serialized = config.Serialize();
        Assert.Contains("[user]", serialized);
        Assert.Contains("name = Jane Doe", serialized);
    }

    [Fact]
    public void SetValue_AddsNewSubsection()
    {
        var config = GitConfigFile.Parse(SampleConfig);
        config.SetValue("remote.upstream.url", "https://github.com/upstream/repo.git");

        Assert.Equal("https://github.com/upstream/repo.git", config.GetValue("remote.upstream.url"));
        var serialized = config.Serialize();
        Assert.Contains("[remote \"upstream\"]", serialized);
    }

    [Fact]
    public void UnsetValue_RemovesKey()
    {
        var config = GitConfigFile.Parse(SampleConfig);
        var removed = config.UnsetValue("core.bare");

        Assert.True(removed);
        Assert.Null(config.GetValue("core.bare"));
        Assert.Equal("0", config.GetValue("core.repositoryformatversion"));
    }

    [Fact]
    public void UnsetValue_RemovesSubsectionWhenEmpty()
    {
        var config = GitConfigFile.Parse(SampleConfig);
        Assert.True(config.UnsetValue("branch.main.remote"));
        Assert.True(config.UnsetValue("branch.main.merge"));

        var serialized = config.Serialize();
        Assert.DoesNotContain("[branch \"main\"]", serialized);
    }

    [Fact]
    public void GetSubsections_ReturnsAllSubsections()
    {
        var config = GitConfigFile.Parse(SampleConfig);
        var remotes = config.GetSubsections("remote");

        Assert.Single(remotes);
        Assert.Equal("origin", remotes[0]);
    }

    [Fact]
    public void RenameSubsection_RenamesSubsectionHeader()
    {
        var config = GitConfigFile.Parse(SampleConfig);
        var renamed = config.RenameSubsection("branch", "main", "master");

        Assert.True(renamed);
        Assert.Equal("origin", config.GetValue("branch.master.remote"));
        Assert.Null(config.GetValue("branch.main.remote"));
    }

    [Fact]
    public async Task ReadAndWriteFile_RoundTripsSuccessfully()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            await File.WriteAllTextAsync(tempFile, SampleConfig);
            var config = await GitConfigFile.ReadFromFileAsync(tempFile);

            config.SetValue("core.bare", "true");
            config.SetValue("user.name", "Test User");
            await config.WriteToFileAsync(tempFile);

            var reloaded = await GitConfigFile.ReadFromFileAsync(tempFile);
            Assert.Equal("true", reloaded.GetValue("core.bare"));
            Assert.Equal("Test User", reloaded.GetValue("user.name"));
            Assert.Equal("https://github.com/user/repo.git", reloaded.GetValue("remote.origin.url"));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
