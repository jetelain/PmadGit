using System.IO;
using Pmad.Git.Tests.Infrastructure;

namespace Pmad.Git.LocalRepositories.Test;

public sealed class GitIgnoreMatcherTests
{
    [Fact]
    public void AddRule_IgnoresCommentLines()
    {
        var matcher = new GitIgnoreMatcher();
        matcher.AddRule("# this is a comment");
        matcher.AddRule("   # indented comment");
        matcher.AddRule("*.log");

        Assert.True(matcher.IsIgnored("test.log", isDirectory: false));
        Assert.False(matcher.IsIgnored("# this is a comment", isDirectory: false));
    }

    [Fact]
    public void AddRule_IgnoresEmptyAndWhitespaceLines()
    {
        var matcher = new GitIgnoreMatcher();
        matcher.AddRule("");
        matcher.AddRule("   ");
        matcher.AddRule("*.txt");

        Assert.True(matcher.IsIgnored("file.txt", isDirectory: false));
        Assert.False(matcher.IsIgnored("file.md", isDirectory: false));
    }

    [Fact]
    public void IsIgnored_SimpleWildcard_MatchesExtension()
    {
        var matcher = new GitIgnoreMatcher();
        matcher.AddRule("*.log");

        Assert.True(matcher.IsIgnored("app.log", isDirectory: false));
        Assert.True(matcher.IsIgnored("sub/dir/debug.log", isDirectory: false));
        Assert.False(matcher.IsIgnored("app.txt", isDirectory: false));
    }

    [Fact]
    public void IsIgnored_NegatedRule_UnignoresFile()
    {
        var matcher = new GitIgnoreMatcher();
        matcher.AddRule("*.log");
        matcher.AddRule("!important.log");

        Assert.True(matcher.IsIgnored("debug.log", isDirectory: false));
        Assert.False(matcher.IsIgnored("important.log", isDirectory: false));
    }

    [Fact]
    public void IsIgnored_DirectoryOnlyRule_OnlyMatchesDirectories()
    {
        var matcher = new GitIgnoreMatcher();
        matcher.AddRule("build/");

        Assert.True(matcher.IsIgnored("build", isDirectory: true));
        Assert.False(matcher.IsIgnored("build", isDirectory: false));
        Assert.True(matcher.IsIgnored("build/output.dll", isDirectory: false));
    }

    [Fact]
    public void IsIgnored_RootAnchored_DoesNotMatchSubdirectory()
    {
        var matcher = new GitIgnoreMatcher();
        matcher.AddRule("/root.txt");

        Assert.True(matcher.IsIgnored("root.txt", isDirectory: false));
        Assert.False(matcher.IsIgnored("sub/root.txt", isDirectory: false));
    }

    [Fact]
    public void IsIgnored_FileUnderIgnoredDirectory_IsIgnored()
    {
        var matcher = new GitIgnoreMatcher();
        matcher.AddRule("bin/");

        Assert.True(matcher.IsIgnored("bin/debug/app.exe", isDirectory: false));
        Assert.True(matcher.IsIgnored("sub/bin/app.dll", isDirectory: false));
    }

    [Fact]
    public void IsIgnored_DotGitAlwaysIgnored()
    {
        var matcher = new GitIgnoreMatcher();

        Assert.True(matcher.IsIgnored(".git", isDirectory: true));
        Assert.True(matcher.IsIgnored(".git/config", isDirectory: false));
        Assert.True(matcher.IsIgnored(".git/HEAD", isDirectory: false));
    }

    [Fact]
    public void IsIgnored_DoubleStarPattern_MatchesAcrossDirectories()
    {
        var matcher = new GitIgnoreMatcher();
        matcher.AddRule("**/temp/*");

        Assert.True(matcher.IsIgnored("temp/a.txt", isDirectory: false));
        Assert.True(matcher.IsIgnored("sub/temp/b.txt", isDirectory: false));
        Assert.True(matcher.IsIgnored("sub/nested/temp/c.txt", isDirectory: false));
        Assert.False(matcher.IsIgnored("other/d.txt", isDirectory: false));
    }

    [Fact]
    public void IsIgnored_NegatedRuleInIgnoredDirectory_CannotReincludeWhenParentDirectoryIsIgnored()
    {
        var matcher = new GitIgnoreMatcher();
        matcher.AddRule("build/");
        matcher.AddRule("!build/keep.txt");

        Assert.True(matcher.IsIgnored("build/trash.txt", isDirectory: false));
        // Per Git spec, cannot re-include a file if its parent directory is excluded
        Assert.True(matcher.IsIgnored("build/keep.txt", isDirectory: false));
    }

    [Fact]
    public void IsIgnored_NegatedRuleInWildcardDirectory_CanReincludeWhenParentDirectoryNotIgnored()
    {
        var matcher = new GitIgnoreMatcher();
        matcher.AddRule("build/*");
        matcher.AddRule("!build/keep.txt");

        Assert.True(matcher.IsIgnored("build/trash.txt", isDirectory: false));
        // Since build/ itself was not excluded, !build/keep.txt can re-include it
        Assert.False(matcher.IsIgnored("build/keep.txt", isDirectory: false));
    }

    [Fact]
    public void Load_InfoExcludeLoadedBeforeRootGitIgnore()
    {
        using var testRepo = GitTestRepository.Create();

        // Write rule to info/exclude
        var infoDir = Path.Combine(testRepo.WorkingDirectory, ".git", "info");
        Directory.CreateDirectory(infoDir);
        File.WriteAllText(Path.Combine(infoDir, "exclude"), "*.tmp\n");

        // Write overriding negation rule to root .gitignore
        File.WriteAllText(Path.Combine(testRepo.WorkingDirectory, ".gitignore"), "!important.tmp\n");

        var matcher = GitIgnoreMatcher.Load(testRepo.WorkingDirectory);

        // *.tmp was in exclude, but root .gitignore negated important.tmp (last rule wins)
        Assert.True(matcher.IsIgnored("other.tmp", isDirectory: false));
        Assert.False(matcher.IsIgnored("important.tmp", isDirectory: false));
    }

    [Fact]
    public void Load_SubdirectoryGitignoreFiles_AreLoadedAndScoped()
    {
        using var testRepo = GitTestRepository.Create();

        // Create sub directory with its own .gitignore
        var subDir = Path.Combine(testRepo.WorkingDirectory, "sub");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, ".gitignore"), "*.obj\n/local.txt\n");

        var matcher = GitIgnoreMatcher.Load(testRepo.WorkingDirectory);

        // *.obj should be ignored in sub, but NOT at repository root
        Assert.True(matcher.IsIgnored("sub/output.obj", isDirectory: false));
        Assert.True(matcher.IsIgnored("sub/nested/output.obj", isDirectory: false));
        Assert.False(matcher.IsIgnored("root.obj", isDirectory: false));

        // /local.txt is anchored to sub/
        Assert.True(matcher.IsIgnored("sub/local.txt", isDirectory: false));
        Assert.False(matcher.IsIgnored("local.txt", isDirectory: false));
        Assert.False(matcher.IsIgnored("sub/nested/local.txt", isDirectory: false));
    }

    [Fact]
    public void Load_SkipsDirectorySymlinksAndReparsePoints()
    {
        using var testRepo = GitTestRepository.Create();

        // Create an external directory outside the repository with a .gitignore
        var externalDir = Path.Combine(Path.GetTempPath(), $"pmad_ext_{Guid.NewGuid():N}");
        Directory.CreateDirectory(externalDir);
        try
        {
            File.WriteAllText(Path.Combine(externalDir, ".gitignore"), "*.external\n");

            var linkPath = Path.Combine(testRepo.WorkingDirectory, "external_link");
            if (!TryCreateDirectoryLink(linkPath, externalDir))
            {
                // Platform could not create symlink/junction in test environment
                return;
            }

            var matcher = GitIgnoreMatcher.Load(testRepo.WorkingDirectory);

            // Rules from external_link/.gitignore must NOT be loaded
            Assert.False(matcher.IsIgnored("file.external", isDirectory: false));
            Assert.False(matcher.IsIgnored("external_link/file.external", isDirectory: false));
        }
        finally
        {
            try { Directory.Delete(externalDir, true); } catch { }
        }
    }

    private static bool TryCreateDirectoryLink(string linkPath, string targetPath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c mklink /J \"{linkPath}\" \"{targetPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                process?.WaitForExit();
                return Directory.Exists(linkPath);
            }
            else
            {
                Directory.CreateSymbolicLink(linkPath, targetPath);
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    [Fact]
    public void Load_IgnoredDirectoryWithManySubdirectories_DoesNotRecurseAndLoadsQuickly()
    {
        using var testRepo = GitTestRepository.Create();
        var nodeModulesDir = Path.Combine(testRepo.WorkingDirectory, "node_modules");
        Directory.CreateDirectory(nodeModulesDir);

        // Create mock node_modules subdirectories and a sentinel .gitignore inside
        for (var i = 0; i < 5000; i++)
        {
            Directory.CreateDirectory(Path.Combine(nodeModulesDir, $"pkg_{i}"));
        }
        File.WriteAllText(Path.Combine(nodeModulesDir, "pkg_0", ".gitignore"), "*.sentinel_ignore\n");

        File.WriteAllText(Path.Combine(testRepo.WorkingDirectory, ".gitignore"), "node_modules/\n");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var matcher = GitIgnoreMatcher.Load(testRepo.WorkingDirectory);
        stopwatch.Stop();

        Assert.True(matcher.IsIgnored("node_modules/pkg_1/index.js", isDirectory: false));
        // Sentinel rule inside ignored node_modules must NOT have been loaded
        Assert.False(matcher.IsIgnored("outside.sentinel_ignore", isDirectory: false));
        // Must complete very quickly (< 2 seconds) because it skips node_modules enumeration completely
        Assert.True(stopwatch.ElapsedMilliseconds < 2000, $"Load took {stopwatch.ElapsedMilliseconds} ms");
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("///")]
    public void IsIgnored_EmptyPathOrRoot_NeverIgnored(string rootPath)
    {
        var matcher = new GitIgnoreMatcher();
        matcher.AddRule("*");

        Assert.False(matcher.IsIgnored(rootPath, isDirectory: true));
        Assert.False(matcher.IsIgnored(rootPath, isDirectory: false));
    }

    [Fact]
    public void IsIgnored_TrailingSlashes_HandledConsistently()
    {
        var matcher = new GitIgnoreMatcher();
        matcher.AddRule("build/");

        Assert.True(matcher.IsIgnored("build", isDirectory: true));
        Assert.True(matcher.IsIgnored("build/", isDirectory: true));
        Assert.True(matcher.IsIgnored("dir/build", isDirectory: true));
        Assert.True(matcher.IsIgnored("dir/build/", isDirectory: true));
    }

    [Fact]
    public void IsIgnored_SubdirectoryWithWildcard_DoesNotIgnoreSubdirectoryItself()
    {
        var matcher = new GitIgnoreMatcher();
        matcher.AddRule("*", basePrefix: "sub");

        // The subdirectory itself is never ignored by rules defined inside it
        Assert.False(matcher.IsIgnored("sub", isDirectory: true));
        Assert.False(matcher.IsIgnored("sub/", isDirectory: true));

        // But files inside sub are ignored
        Assert.True(matcher.IsIgnored("sub/file.txt", isDirectory: false));
        Assert.True(matcher.IsIgnored("sub/nested/file.txt", isDirectory: false));
    }
}

