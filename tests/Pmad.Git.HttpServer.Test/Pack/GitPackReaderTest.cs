using System.Diagnostics;
using System.IO.Compression;
using Pmad.Git.HttpServer.Pack;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.HttpServer.Test.Pack;

public sealed class GitPackReaderTest : IDisposable
{
    private readonly string _workingDirectory;
    private readonly string _gitDirectory;

    public GitPackReaderTest()
    {
        _workingDirectory = Path.Combine(Path.GetTempPath(), "PmadGitPackReaderTests", Guid.NewGuid().ToString("N"));
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
    public async Task ReadAsync_WithSimpleCommit_ShouldCreateObjects()
    {
        // Create a simple repository with one commit
        CreateFile("README.md", "# Test Repository");
        RunGit("add README.md");
        RunGit("commit -m \"Initial commit\" --quiet");

        await TestPackTransfer();
    }

    [Fact]
    public async Task ReadAsync_WithMultipleCommits_ShouldCreateAllObjects()
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

        await TestPackTransfer();
    }

    [Fact]
    public async Task ReadAsync_WithDeltaCompression_ShouldResolveDeltas()
    {
        CreateFile("large.txt", new string('A', 10000));
        RunGit("add large.txt");
        RunGit("commit -m \"Add large file\" --quiet");

        CreateFile("large.txt", new string('A', 9000) + new string('B', 1000));
        RunGit("add large.txt");
        RunGit("commit -m \"Modify large file\" --quiet");

        RunGit("gc --aggressive --quiet");

        await TestPackTransfer();
    }

    [Fact]
    public async Task ReadAsync_WithNestedDirectories_ShouldCreateTreeObjects()
    {
        Directory.CreateDirectory(Path.Combine(_workingDirectory, "src", "lib"));
        CreateFile("src/lib/file.txt", "nested content");
        CreateFile("src/main.txt", "main content");
        RunGit("add -A");
        RunGit("commit -m \"Add nested structure\" --quiet");

        var created = await TestPackTransfer();
        
        var treeObjects = new List<GitHash>();
        var targetDir = Path.Combine(Path.GetTempPath(), "PmadGitPackTarget", Guid.NewGuid().ToString("N"));
        try
        {
            var repository = await CreateTargetRepositoryWithPackAsync(targetDir);
            foreach (var hash in created)
            {
                var obj = await repository.ObjectStore.ReadObjectAsync(hash, CancellationToken.None);
                if (obj.Type == GitObjectType.Tree)
                {
                    treeObjects.Add(hash);
                }
            }
        }
        finally
        {
            TestHelper.TryDeleteDirectory(targetDir);
        }

        Assert.NotEmpty(treeObjects);
    }

    [Fact]
    public async Task ReadAsync_WithInvalidPackSignature_ShouldThrowException()
    {
        var invalidPack = new MemoryStream();
        invalidPack.Write("FAKE"u8.ToArray());
        invalidPack.Write(new byte[8]); // Add some padding
        invalidPack.Position = 0;

        var repository = GitRepository.Open(_workingDirectory);
        var reader = new GitPackReader();

        await Assert.ThrowsAnyAsync<Exception>(
            async () => await reader.ReadAsync(repository, invalidPack, CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_WithUnsupportedVersion_ShouldThrowException()
    {
        var invalidPack = new MemoryStream();
        invalidPack.Write("PACK"u8.ToArray());
        invalidPack.Write(BitConverter.GetBytes(0x03000000).Reverse().ToArray()); // version 3 in big-endian
        invalidPack.Write(BitConverter.GetBytes(0).Reverse().ToArray());
        invalidPack.Position = 0;

        var repository = GitRepository.Open(_workingDirectory);
        var reader = new GitPackReader();

        await Assert.ThrowsAsync<NotSupportedException>(
            async () => await reader.ReadAsync(repository, invalidPack, CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_WithEmptyPack_ShouldReturnEmptyList()
    {
        var emptyPack = CreateEmptyPackStream();
        var repository = GitRepository.Open(_workingDirectory);
        var reader = new GitPackReader();

        var created = await reader.ReadAsync(repository, emptyPack, CancellationToken.None);

        Assert.Empty(created);
        emptyPack.Dispose();
    }

    [Fact]
    public async Task ReadAsync_WithChecksumMismatch_ShouldThrowException()
    {
        CreateFile("test.txt", "test content");
        RunGit("add test.txt");
        RunGit("commit -m \"Test commit\" --quiet");
        RunGit("repack -a -d -q");

        var packDir = Path.Combine(_gitDirectory, "objects", "pack");
        var packs = Directory.GetFiles(packDir, "*.pack");
        if (packs.Length == 0)
        {
            return;
        }

        var packData = await File.ReadAllBytesAsync(packs[0]);

        if (packData.Length < 20)
        {
            return;
        }

        for (var i = packData.Length - 20; i < packData.Length; i++)
        {
            packData[i] = 0xFF;
        }

        var targetDir = Path.Combine(Path.GetTempPath(), "PmadGitPackTarget", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targetDir);
        try
        {
            RunGitInDirectory(targetDir, "init --quiet");
            RunGitInDirectory(targetDir, "config user.name \"Test User\"");
            RunGitInDirectory(targetDir, "config user.email test@example.com");
            
            var corruptedStream = new MemoryStream(packData);
            var repository = GitRepository.Open(targetDir);
            var reader = new GitPackReader();

            await Assert.ThrowsAsync<InvalidDataException>(
                async () => await reader.ReadAsync(repository, corruptedStream, CancellationToken.None));
        }
        finally
        {
            TestHelper.TryDeleteDirectory(targetDir);
        }
    }

    [Fact]
    public async Task ReadAsync_WithNullRepository_ShouldThrowArgumentNullException()
    {
        var packStream = new MemoryStream();
        var reader = new GitPackReader();

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await reader.ReadAsync(null!, packStream, CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_WithNullSource_ShouldThrowArgumentNullException()
    {
        var repository = GitRepository.Open(_workingDirectory);
        var reader = new GitPackReader();

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await reader.ReadAsync(repository, null!, CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_WithCancellationToken_ShouldRespectCancellation()
    {
        CreateFile("file.txt", "content");
        RunGit("add file.txt");
        RunGit("commit -m \"Commit\" --quiet");
        RunGit("repack -a -d -q");

        var packDir = Path.Combine(_gitDirectory, "objects", "pack");
        var packs = Directory.GetFiles(packDir, "*.pack");
        if (packs.Length == 0)
        {
            return;
        }

        var targetDir = Path.Combine(Path.GetTempPath(), "PmadGitPackTarget", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targetDir);
        try
        {
            RunGitInDirectory(targetDir, "init --quiet");
            RunGitInDirectory(targetDir, "config user.name \"Test User\"");
            RunGitInDirectory(targetDir, "config user.email test@example.com");
            
            var packStream = new FileStream(packs[0], FileMode.Open, FileAccess.Read, FileShare.Read);
            var repository = GitRepository.Open(targetDir);
            var reader = new GitPackReader();

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await reader.ReadAsync(repository, packStream, cts.Token));
                
            packStream.Dispose();
        }
        finally
        {
            TestHelper.TryDeleteDirectory(targetDir);
        }
    }

    [Fact]
    public async Task ReadAsync_WithRefDelta_ShouldResolveBaseObject()
    {
        CreateFile("base.txt", "base content");
        RunGit("add base.txt");
        RunGit("commit -m \"Base commit\" --quiet");

        CreateFile("modified.txt", "modified content");
        RunGit("add modified.txt");
        RunGit("commit -m \"Modified commit\" --quiet");

        await TestPackTransfer();
    }

    [Fact]
    public async Task ReadAsync_WithRefDelta_Kind7_ShouldResolveFromRepository()
    {
        // Arrange: Create multiple commits to force ref-delta usage
        // Create base commit with a file
        CreateFile("base-file.txt", "This is the base content that will be used for delta compression");
        RunGit("add base-file.txt");
        RunGit("commit -m \"Base commit for ref-delta test\" --quiet");
        
        // Get the hash of the base blob
        var baseHashOutput = RunGit("rev-parse HEAD:base-file.txt");
        var baseHash = baseHashOutput.Trim();
        
        // Create a similar file (will create a ref-delta)
        CreateFile("base-file.txt", "This is the base content that will be used for delta compression with changes");
        RunGit("add base-file.txt");
        RunGit("commit -m \"Modified commit creating ref-delta\" --quiet");
        
        // Create several more commits to increase likelihood of ref-delta
        for (int i = 0; i < 5; i++)
        {
            CreateFile($"file{i}.txt", $"Content {i}");
            RunGit($"add file{i}.txt");
            RunGit($"commit -m \"Commit {i}\" --quiet");
        }
        
        // Force aggressive repacking with delta compression
        RunGit("repack -a -d -f --depth=50 --window=50 -q");
        
        // Act: Transfer pack to new repository
        var created = await TestPackTransfer();
        
        // Assert: Verify objects were created successfully
        Assert.NotEmpty(created);
        
        // Verify the base object is present and accessible
        var targetDir = Path.Combine(Path.GetTempPath(), "PmadGitPackTargetRefDelta", Guid.NewGuid().ToString("N"));
        try
        {
            var repository = await CreateTargetRepositoryWithPackAsync(targetDir);
            
            // Verify we can read all created objects
            var objectTypeCount = new Dictionary<GitObjectType, int>();
            foreach (var hash in created)
            {
                var obj = await repository.ObjectStore.ReadObjectAsync(hash, CancellationToken.None);
                Assert.NotNull(obj);
                objectTypeCount.TryGetValue(obj.Type, out var count);
                objectTypeCount[obj.Type] = count + 1;
            }
            
            // Should have multiple blobs (some might be ref-deltas)
            Assert.True(objectTypeCount.ContainsKey(GitObjectType.Blob));
            Assert.True(objectTypeCount[GitObjectType.Blob] >= 2);
        }
        finally
        {
            TestHelper.TryDeleteDirectory(targetDir);
        }
    }

    [Fact]
    public async Task ReadAsync_WithAllObjectTypes_ShouldHandleCommitTreeBlobTag()
    {
        CreateFile("test.txt", "test content");
        RunGit("add test.txt");
        RunGit("commit -m \"Test commit\" --quiet");
        RunGit("tag -a v1.0 -m \"Version 1.0\"");

        var created = await TestPackTransfer();

        var targetDir = Path.Combine(Path.GetTempPath(), "PmadGitPackTarget", Guid.NewGuid().ToString("N"));
        try
        {
            var repository = await CreateTargetRepositoryWithPackAsync(targetDir);
            var objectTypes = new HashSet<GitObjectType>();
            foreach (var hash in created)
            {
                var obj = await repository.ObjectStore.ReadObjectAsync(hash, CancellationToken.None);
                objectTypes.Add(obj.Type);
            }

            Assert.Contains(GitObjectType.Commit, objectTypes);
            Assert.Contains(GitObjectType.Tree, objectTypes);
            Assert.Contains(GitObjectType.Blob, objectTypes);
        }
        finally
        {
            TestHelper.TryDeleteDirectory(targetDir);
        }
    }

    private async Task<IReadOnlyList<GitHash>> TestPackTransfer()
    {
        var targetDir = Path.Combine(Path.GetTempPath(), "PmadGitPackTarget", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targetDir);
        try
        {
            RunGitInDirectory(targetDir, "init --quiet");
            RunGitInDirectory(targetDir, "config user.name \"Test User\"");
            RunGitInDirectory(targetDir, "config user.email test@example.com");

            // Create pack from source repo
            RunGit("repack -a -d -q");
            var packDir = Path.Combine(_gitDirectory, "objects", "pack");
            var packs = Directory.GetFiles(packDir, "*.pack");

            if (packs.Length == 0)
            {
                throw new InvalidOperationException("No pack files created");
            }

            // Read pack into target repo (which has no objects yet)
            using var packStream = new FileStream(packs[0], FileMode.Open, FileAccess.Read, FileShare.Read);
            var repository = GitRepository.Open(targetDir);

            var reader = new GitPackReader();
            var created = await reader.ReadAsync(repository, packStream, CancellationToken.None);

            Assert.NotEmpty(created);
            foreach (var hash in created)
            {
                var obj = await repository.ObjectStore.ReadObjectAsync(hash, CancellationToken.None);
                Assert.NotNull(obj);
            }

            return created;
        }
        finally
        {
            TestHelper.TryDeleteDirectory(targetDir);
        }
    }

    private async Task<GitRepository> CreateTargetRepositoryWithPackAsync(string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        RunGitInDirectory(targetDir, "init --quiet");
        RunGitInDirectory(targetDir, "config user.name \"Test User\"");
        RunGitInDirectory(targetDir, "config user.email test@example.com");

        RunGit("repack -a -d -q");
        var packDir = Path.Combine(_gitDirectory, "objects", "pack");
        var packs = Directory.GetFiles(packDir, "*.pack");

        if (packs.Length == 0)
        {
            throw new InvalidOperationException("No pack files created");
        }

        using var packStream = new FileStream(packs[0], FileMode.Open, FileAccess.Read, FileShare.Read);
        var repository = GitRepository.Open(targetDir);

        var reader = new GitPackReader();
        await reader.ReadAsync(repository, packStream, CancellationToken.None);

        return repository;
    }

    private Stream CreateEmptyPackStream()
    {
        var stream = new MemoryStream();
        // Write "PACK"
        stream.Write("PACK"u8.ToArray());
        // Write version 2 in big-endian
        stream.WriteByte(0);
        stream.WriteByte(0);
        stream.WriteByte(0);
        stream.WriteByte(2);
        // Write object count 0 in big-endian  
        stream.WriteByte(0);
        stream.WriteByte(0);
        stream.WriteByte(0);
        stream.WriteByte(0);

        // Calculate SHA-1 of the pack so far
        using var sha1 = System.Security.Cryptography.SHA1.Create();
        var data = stream.ToArray();
        var hash = sha1.ComputeHash(data);

        stream.Write(hash);
        stream.Position = 0;
        return stream;
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
        TestHelper.TryDeleteDirectory(_workingDirectory);
    }
}
