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

    [Fact]
    public void Parse_KeyOnlyBoolean_EvaluatesToTrue()
    {
        var content = """
            [core]
            	bare
            	filemode = false
            """;
        var config = GitConfigFile.Parse(content);

        Assert.Equal("true", config.GetValue("core.bare"));
        Assert.True(config.GetBoolean("core.bare"));
        Assert.Equal("false", config.GetValue("core.filemode"));
        Assert.False(config.GetBoolean("core.filemode"));
    }

    [Fact]
    public void UnsetValue_KeyOnlyBoolean_RemovesKey()
    {
        var content = """
            [core]
            	bare
            	filemode = false
            """;
        var config = GitConfigFile.Parse(content);
        var removed = config.UnsetValue("core.bare");

        Assert.True(removed);
        Assert.Null(config.GetValue("core.bare"));
        Assert.Equal("false", config.GetValue("core.filemode"));
        Assert.DoesNotContain("bare", config.Serialize());
    }

    [Fact]
    public void SetValue_KeyOnlyBoolean_UpdatesValue()
    {
        var content = """
            [core]
            	bare
            """;
        var config = GitConfigFile.Parse(content);
        config.SetValue("core.bare", "false");

        Assert.Equal("false", config.GetValue("core.bare"));
        Assert.Contains("bare = false", config.Serialize());
    }

    [Fact]
    public void Parse_InlineComments_AreStrippedFromValues()
    {
        var content = """
            [user]
            	name = John Doe # full name
            	email = john@example.com ; primary email
            """;
        var config = GitConfigFile.Parse(content);

        Assert.Equal("John Doe", config.GetValue("user.name"));
        Assert.Equal("john@example.com", config.GetValue("user.email"));
    }

    [Fact]
    public void Parse_InvalidSyntaxOutsideSection_ThrowsFormatException()
    {
        var content = "not-in-a-section = value\n[core]\n\tbare";
        Assert.Throws<System.FormatException>(() => GitConfigFile.Parse(content));
    }

    [Fact]
    public void Parse_InvalidSyntaxInsideSection_ThrowsFormatException()
    {
        var content = "[core]\n\t123 invalid key ???";
        Assert.Throws<System.FormatException>(() => GitConfigFile.Parse(content));
    }

    [Fact]
    public void GetAllValues_ReturnsAllMatchingValues()
    {
        var content = """
            [remote "origin"]
            	fetch = +refs/heads/*:refs/remotes/origin/*
            	fetch = +refs/tags/*:refs/tags/*
            """;
        var config = GitConfigFile.Parse(content);
        var values = config.GetAllValues("remote.origin.fetch");

        Assert.Equal(2, values.Count);
        Assert.Equal("+refs/heads/*:refs/remotes/origin/*", values[0]);
        Assert.Equal("+refs/tags/*:refs/tags/*", values[1]);
    }

    [Fact]
    public async Task ReadWithIncludesAsync_ResolvesIncludesAndPrecedence()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            var mainFile = Path.Combine(tempDir, "main.config");
            var incFile = Path.Combine(tempDir, "inc.config");

            var incContent = """
                [user]
                	name = Overridden Name
                	email = inc@example.com
                """;
            var mainContent = """
                [user]
                	name = Initial Name
                [include]
                	path = inc.config
                [other]
                	key = value
                """;

            await File.WriteAllTextAsync(incFile, incContent);
            await File.WriteAllTextAsync(mainFile, mainContent);

            var config = await GitConfigFile.ReadWithIncludesAsync(mainFile);

            // inc.config was included after Initial Name, so it overrides user.name
            Assert.Equal("Overridden Name", config.GetValue("user.name"));
            Assert.Equal("inc@example.com", config.GetValue("user.email"));
            Assert.Equal("value", config.GetValue("other.key"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
