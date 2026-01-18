using System.Diagnostics;
using System.Security.Cryptography;
using Pmad.Git.HttpServer.Pack;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.HttpServer.Test.Pack;

public sealed class GitPackBuilderTest : IDisposable
{
    private readonly string _workingDirectory;
    private readonly string _gitDirectory;

    public GitPackBuilderTest()
    {
        _workingDirectory = Path.Combine(Path.GetTempPath(), "PmadGitPackBuilderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workingDirectory);
        _gitDirectory = Path.Combine(_workingDirectory, ".git");
        InitializeRepository();
    }

    private void InitializeRepository()
    {
        RunGit("init --quiet");
        RunGit("config user.name \"Test User\"");
        RunGit("config user.email test@example.com");
    }

    [Fact]
    public async Task WriteAsync_WithSimpleCommit_ShouldCreateValidPack()
    {
        // Create a commit with git
        CreateFile("README.md", "# Test Repository");
        RunGit("add README.md");
        RunGit("commit -m \"Initial commit\" --quiet");

        var repository = GitRepository.Open(_workingDirectory);
        var commit = await repository.GetCommitAsync();
        
        // Collect all objects
        var objects = await CollectAllObjectsAsync(repository, commit.Id);

        // Write pack file
        var packPath = Path.Combine(_workingDirectory, "test.pack");
        await using (var packStream = new FileStream(packPath, FileMode.Create, FileAccess.Write))
        {
            var builder = new GitPackBuilder();
            await builder.WriteAsync(repository, objects, packStream, CancellationToken.None);
        }

        // Verify pack file with git
        VerifyPackFileWithGit(packPath);
    }

    [Fact]
    public async Task WriteAsync_WithMultipleCommits_ShouldCreateValidPack()
    {
        CreateFile("file1.txt", "Content 1");
        RunGit("add file1.txt");
        RunGit("commit -m \"First commit\" --quiet");

        CreateFile("file2.txt", "Content 2");
        RunGit("add file2.txt");
        RunGit("commit -m \"Second commit\" --quiet");

        CreateFile("file3.txt", "Content 3");
        RunGit("add file3.txt");
        RunGit("commit -m \"Third commit\" --quiet");

        var repository = GitRepository.Open(_workingDirectory);
        var commit = await repository.GetCommitAsync();
        
        var objects = await CollectAllObjectsAsync(repository, commit.Id);

        var packPath = Path.Combine(_workingDirectory, "test.pack");
        await using (var packStream = new FileStream(packPath, FileMode.Create, FileAccess.Write))
        {
            var builder = new GitPackBuilder();
            await builder.WriteAsync(repository, objects, packStream, CancellationToken.None);
        }

        VerifyPackFileWithGit(packPath);
        
        // Verify we can read it back with GitPackReader
        await VerifyPackWithReaderAsync(packPath, objects.Count);
    }

    [Fact]
    public async Task WriteAsync_WithNestedDirectories_ShouldCreateValidPack()
    {
        Directory.CreateDirectory(Path.Combine(_workingDirectory, "src", "lib"));
        CreateFile("src/lib/file.txt", "nested content");
        CreateFile("src/main.txt", "main content");
        RunGit("add -A");
        RunGit("commit -m \"Add nested structure\" --quiet");

        var repository = GitRepository.Open(_workingDirectory);
        var commit = await repository.GetCommitAsync();
        
        var objects = await CollectAllObjectsAsync(repository, commit.Id);

        var packPath = Path.Combine(_workingDirectory, "test.pack");
        await using (var packStream = new FileStream(packPath, FileMode.Create, FileAccess.Write))
        {
            var builder = new GitPackBuilder();
            await builder.WriteAsync(repository, objects, packStream, CancellationToken.None);
        }

        VerifyPackFileWithGit(packPath);
        await VerifyPackWithReaderAsync(packPath, objects.Count);
    }

    [Fact]
    public async Task WriteAsync_WithLargeFile_ShouldCreateValidPack()
    {
        CreateFile("large.txt", new string('A', 50000));
        RunGit("add large.txt");
        RunGit("commit -m \"Add large file\" --quiet");

        var repository = GitRepository.Open(_workingDirectory);
        var commit = await repository.GetCommitAsync();
        
        var objects = await CollectAllObjectsAsync(repository, commit.Id);

        var packPath = Path.Combine(_workingDirectory, "test.pack");
        await using (var packStream = new FileStream(packPath, FileMode.Create, FileAccess.Write))
        {
            var builder = new GitPackBuilder();
            await builder.WriteAsync(repository, objects, packStream, CancellationToken.None);
        }

        VerifyPackFileWithGit(packPath);
        await VerifyPackWithReaderAsync(packPath, objects.Count);
    }

    [Fact]
    public async Task WriteAsync_WithEmptyObjectList_ShouldCreateValidEmptyPack()
    {
        var repository = GitRepository.Open(_workingDirectory);
        var objects = new List<GitHash>();

        var packPath = Path.Combine(_workingDirectory, "empty.pack");
        await using (var packStream = new FileStream(packPath, FileMode.Create, FileAccess.Write))
        {
            var builder = new GitPackBuilder();
            await builder.WriteAsync(repository, objects, packStream, CancellationToken.None);
        }

        // Verify pack file structure
        var packData = await File.ReadAllBytesAsync(packPath);
        
        // Verify header
        Assert.Equal((byte)'P', packData[0]);
        Assert.Equal((byte)'A', packData[1]);
        Assert.Equal((byte)'C', packData[2]);
        Assert.Equal((byte)'K', packData[3]);
        
        // Verify version (2)
        Assert.Equal(0, packData[4]);
        Assert.Equal(0, packData[5]);
        Assert.Equal(0, packData[6]);
        Assert.Equal(2, packData[7]);
        
        // Verify object count (0)
        Assert.Equal(0, packData[8]);
        Assert.Equal(0, packData[9]);
        Assert.Equal(0, packData[10]);
        Assert.Equal(0, packData[11]);
        
        // Verify checksum
        using var sha1 = SHA1.Create();
        var computedHash = sha1.ComputeHash(packData, 0, 12);
        var storedHash = packData.Skip(12).Take(20).ToArray();
        Assert.Equal(computedHash, storedHash);
    }

    [Fact]
    public async Task WriteAsync_WithNullRepository_ShouldThrowArgumentNullException()
    {
        var packStream = new MemoryStream();
        var builder = new GitPackBuilder();

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await builder.WriteAsync(null!, new List<GitHash>(), packStream, CancellationToken.None));
    }

    [Fact]
    public async Task WriteAsync_WithNullObjects_ShouldThrowArgumentNullException()
    {
        var repository = GitRepository.Open(_workingDirectory);
        var packStream = new MemoryStream();
        var builder = new GitPackBuilder();

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await builder.WriteAsync(repository, null!, packStream, CancellationToken.None));
    }

    [Fact]
    public async Task WriteAsync_WithCancellationToken_ShouldRespectCancellation()
    {
        CreateFile("file.txt", "content");
        RunGit("add file.txt");
        RunGit("commit -m \"Commit\" --quiet");

        var repository = GitRepository.Open(_workingDirectory);
        var commit = await repository.GetCommitAsync();
        var objects = await CollectAllObjectsAsync(repository, commit.Id);

        var packStream = new MemoryStream();
        var builder = new GitPackBuilder();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await builder.WriteAsync(repository, objects, packStream, cts.Token));
    }

    [Fact]
    public async Task WriteAsync_WithTag_ShouldCreateValidPack()
    {
        CreateFile("test.txt", "test content");
        RunGit("add test.txt");
        RunGit("commit -m \"Test commit\" --quiet");
        RunGit("tag -a v1.0 -m \"Version 1.0\"");

        var repository = GitRepository.Open(_workingDirectory);
        var commit = await repository.GetCommitAsync();
        
        // Collect all objects including the tag
        var objects = await CollectAllObjectsAsync(repository, commit.Id);
        
        // Add the tag object
        var refs = await repository.GetReferencesAsync();
        if (refs.TryGetValue("refs/tags/v1.0", out var tagHash))
        {
            var tagData = await repository.ReadObjectAsync(tagHash);
            if (tagData.Type == GitObjectType.Tag)
            {
                objects = objects.Append(tagHash).ToList();
            }
        }

        var packPath = Path.Combine(_workingDirectory, "test.pack");
        await using (var packStream = new FileStream(packPath, FileMode.Create, FileAccess.Write))
        {
            var builder = new GitPackBuilder();
            await builder.WriteAsync(repository, objects, packStream, CancellationToken.None);
        }

        VerifyPackFileWithGit(packPath);
        await VerifyPackWithReaderAsync(packPath, objects.Count);
    }

    [Fact]
    public async Task WriteAsync_RoundTrip_ShouldPreserveAllData()
    {
        // Create various types of content
        CreateFile("file1.txt", "Simple content");
        CreateFile("file2.txt", "Content with\nMultiple\nLines");
        Directory.CreateDirectory(Path.Combine(_workingDirectory, "subdir"));
        CreateFile("subdir/nested.txt", "Nested file");
        RunGit("add -A");
        RunGit("commit -m \"Complex commit\" --quiet");

        var repository = GitRepository.Open(_workingDirectory);
        var commit = await repository.GetCommitAsync();
        var objects = await CollectAllObjectsAsync(repository, commit.Id);

        // Write pack
        var packPath = Path.Combine(_workingDirectory, "test.pack");
        await using (var packStream = new FileStream(packPath, FileMode.Create, FileAccess.Write))
        {
            var builder = new GitPackBuilder();
            await builder.WriteAsync(repository, objects, packStream, CancellationToken.None);
        }

        // Read pack into new repository
        var targetDir = Path.Combine(Path.GetTempPath(), "PmadGitPackTarget", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targetDir);
        try
        {
            RunGitInDirectory(targetDir, "init --quiet");
            RunGitInDirectory(targetDir, "config user.name \"Test User\"");
            RunGitInDirectory(targetDir, "config user.email test@example.com");

            using var packStream = new FileStream(packPath, FileMode.Open, FileAccess.Read);
            var targetRepo = GitRepository.Open(targetDir);
            var reader = new GitPackReader();
            var createdObjects = await reader.ReadAsync(targetRepo, packStream, CancellationToken.None);

            // Verify all objects were recreated
            Assert.Equal(objects.Count, createdObjects.Count);
            
            // Verify each object's content matches
            foreach (var hash in objects)
            {
                var original = await repository.ReadObjectAsync(hash);
                var restored = await targetRepo.ReadObjectAsync(hash);
                
                Assert.Equal(original.Type, restored.Type);
                Assert.Equal(original.Content, restored.Content);
            }
        }
        finally
        {
            try { Directory.Delete(targetDir, true); } catch { }
        }
    }

    [Fact]
    public async Task WriteAsync_PackFormat_ShouldMatchGitSpecification()
    {
        CreateFile("test.txt", "test");
        RunGit("add test.txt");
        RunGit("commit -m \"Test\" --quiet");

        var repository = GitRepository.Open(_workingDirectory);
        var commit = await repository.GetCommitAsync();
        var objects = await CollectAllObjectsAsync(repository, commit.Id);

        var packStream = new MemoryStream();
        var builder = new GitPackBuilder();
        await builder.WriteAsync(repository, objects, packStream, CancellationToken.None);

        var packData = packStream.ToArray();

        // Verify pack signature
        Assert.Equal((byte)'P', packData[0]);
        Assert.Equal((byte)'A', packData[1]);
        Assert.Equal((byte)'C', packData[2]);
        Assert.Equal((byte)'K', packData[3]);

        // Verify version
        var version = (packData[4] << 24) | (packData[5] << 16) | (packData[6] << 8) | packData[7];
        Assert.Equal(2, version);

        // Verify object count
        var objectCount = (uint)((packData[8] << 24) | (packData[9] << 16) | (packData[10] << 8) | packData[11]);
        Assert.Equal((uint)objects.Count, objectCount);

        // Verify checksum
        using var sha1 = SHA1.Create();
        var computedHash = sha1.ComputeHash(packData, 0, packData.Length - 20);
        var storedHash = packData.Skip(packData.Length - 20).ToArray();
        Assert.Equal(computedHash, storedHash);
    }

    private async Task<List<GitHash>> CollectAllObjectsAsync(GitRepository repository, GitHash commitHash)
    {
        var objects = new List<GitHash>();
        var visited = new HashSet<string>();

        async Task CollectCommitAsync(GitHash hash)
        {
            if (!visited.Add(hash.Value))
            {
                return;
            }

            var commit = await repository.GetCommitAsync(hash.Value);
            objects.Add(hash);

            await CollectTreeAsync(commit.Tree);

            foreach (var parent in commit.Parents)
            {
                await CollectCommitAsync(parent);
            }
        }

        async Task CollectTreeAsync(GitHash treeHash)
        {
            if (!visited.Add(treeHash.Value))
            {
                return;
            }

            objects.Add(treeHash);
            
            await foreach (var item in repository.EnumerateCommitTreeAsync(commitHash.Value))
            {
                if (!visited.Contains(item.Entry.Hash.Value))
                {
                    if (item.Entry.Kind == GitTreeEntryKind.Tree)
                    {
                        await CollectTreeAsync(item.Entry.Hash);
                    }
                    else if (item.Entry.Kind == GitTreeEntryKind.Blob)
                    {
                        if (visited.Add(item.Entry.Hash.Value))
                        {
                            objects.Add(item.Entry.Hash);
                        }
                    }
                }
            }
        }

        await CollectCommitAsync(commitHash);
        return objects;
    }

    private void VerifyPackFileWithGit(string packPath)
    {
        // Copy pack to git's objects/pack directory with a proper name
        var packDir = Path.Combine(_gitDirectory, "objects", "pack");
        Directory.CreateDirectory(packDir);
        
        var packName = $"pack-{Guid.NewGuid():N}.pack";
        var gitPackPath = Path.Combine(packDir, packName);
        File.Copy(packPath, gitPackPath, true);
        
        try
        {
            // Use git index-pack to verify and index the pack file
            var output = RunGit($"index-pack \"{gitPackPath}\"");
            
            // Now verify it
            var verifyOutput = RunGit($"verify-pack -v \"{gitPackPath}\"");
            Assert.NotEmpty(verifyOutput);
        }
        finally
        {
            // Clean up
            try
            {
                File.Delete(gitPackPath);
                var idxPath = Path.ChangeExtension(gitPackPath, ".idx");
                if (File.Exists(idxPath))
                {
                    File.Delete(idxPath);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    private async Task VerifyPackWithReaderAsync(string packPath, int expectedObjectCount)
    {
        var targetDir = Path.Combine(Path.GetTempPath(), "PmadGitPackVerify", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targetDir);
        try
        {
            RunGitInDirectory(targetDir, "init --quiet");
            RunGitInDirectory(targetDir, "config user.name \"Test User\"");
            RunGitInDirectory(targetDir, "config user.email test@example.com");

            using var packStream = new FileStream(packPath, FileMode.Open, FileAccess.Read);
            var repository = GitRepository.Open(targetDir);
            var reader = new GitPackReader();
            var created = await reader.ReadAsync(repository, packStream, CancellationToken.None);

            Assert.Equal(expectedObjectCount, created.Count);
            
            foreach (var hash in created)
            {
                var obj = await repository.ReadObjectAsync(hash);
                Assert.NotNull(obj);
            }
        }
        finally
        {
            try { Directory.Delete(targetDir, true); } catch { }
        }
    }

    private void CreateFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_workingDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, content);
    }

    private string RunGit(string arguments)
    {
        return RunGitInDirectory(_workingDirectory, arguments);
    }

    private string RunGitInDirectory(string workingDirectory, string arguments)
    {
        var startInfo = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start git process");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {arguments} failed with exit code {process.ExitCode}:{Environment.NewLine}{error}");
        }

        return string.IsNullOrEmpty(output) ? error : output;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workingDirectory, recursive: true);
        }
        catch
        {
            // Ignore cleanup failures in tests
        }
    }
}
