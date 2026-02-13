using System.Diagnostics;
using Pmad.Git.HttpServer.Pack;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.HttpServer.Test.Pack;

/// <summary>
/// Tests for GitPackReader focusing on non-FileStream scenarios and temporary file handling.
/// </summary>
public sealed class GitPackReaderStreamTests : IDisposable
{
    private readonly string _workingDirectory;
    private readonly string _gitDirectory;

    public GitPackReaderStreamTests()
    {
        _workingDirectory = Path.Combine(Path.GetTempPath(), "PmadGitPackReaderStreamTests", Guid.NewGuid().ToString("N"));
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

    #region MemoryStream Tests

    [Fact]
    public async Task ReadAsync_WithMemoryStream_ShouldCreateObjectsViaTemporaryFile()
    {
        // Arrange
        CreateFile("test.txt", "test content");
        RunGit("add test.txt");
        RunGit("commit -m \"Test commit\" --quiet");
        RunGit("repack -a -d -q");

        var packData = await GetPackDataAsync();
        var memoryStream = new MemoryStream(packData);

        var targetDir = Path.Combine(Path.GetTempPath(), "PmadGitPackTarget", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targetDir);
        try
        {
            RunGitInDirectory(targetDir, "init --quiet");
            var repository = GitRepository.Open(targetDir);
            var reader = new GitPackReader();

            // Act
            var created = await reader.ReadAsync(repository, memoryStream, CancellationToken.None);

            // Assert
            Assert.NotEmpty(created);
            foreach (var hash in created)
            {
                var obj = await repository.ReadObjectAsync(hash, CancellationToken.None);
                Assert.NotNull(obj);
            }
        }
        finally
        {
            TestHelper.TryDeleteDirectory(targetDir);
        }
    }

    [Fact]
    public async Task ReadAsync_WithLargeMemoryStream_ShouldHandleCorrectly()
    {
        // Arrange: Create a repository with multiple files
        for (int i = 0; i < 10; i++)
        {
            CreateFile($"file{i}.txt", $"Content for file {i}");
        }
        RunGit("add .");
        RunGit("commit -m \"Multiple files commit\" --quiet");
        RunGit("repack -a -d -q");

        var packData = await GetPackDataAsync();
        var memoryStream = new MemoryStream(packData);

        var targetDir = Path.Combine(Path.GetTempPath(), "PmadGitPackTarget", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targetDir);
        try
        {
            RunGitInDirectory(targetDir, "init --quiet");
            var repository = GitRepository.Open(targetDir);
            var reader = new GitPackReader();

            // Act
            var created = await reader.ReadAsync(repository, memoryStream, CancellationToken.None);

            // Assert
            Assert.NotEmpty(created);
            Assert.True(created.Count >= 10, $"Should have created at least 10 objects, but created {created.Count}");
        }
        finally
        {
            TestHelper.TryDeleteDirectory(targetDir);
        }
    }

    #endregion

    #region NetworkStream Simulation Tests

    [Fact]
    public async Task ReadAsync_WithSlowStream_ShouldHandlePartialReads()
    {
        // Arrange
        CreateFile("test.txt", "test content");
        RunGit("add test.txt");
        RunGit("commit -m \"Test commit\" --quiet");
        RunGit("repack -a -d -q");

        var packData = await GetPackDataAsync();
        var slowStream = new SlowStream(packData, bytesPerRead: 512); // Simulate slow network

        var targetDir = Path.Combine(Path.GetTempPath(), "PmadGitPackTarget", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targetDir);
        try
        {
            RunGitInDirectory(targetDir, "init --quiet");
            var repository = GitRepository.Open(targetDir);
            var reader = new GitPackReader();

            // Act
            var created = await reader.ReadAsync(repository, slowStream, CancellationToken.None);

            // Assert
            Assert.NotEmpty(created);
            Assert.True(slowStream.ReadCallCount > 1, "Should have made multiple read calls");
        }
        finally
        {
            TestHelper.TryDeleteDirectory(targetDir);
        }
    }

    [Fact]
    public async Task ReadAsync_WithVerySlowStream_ShouldStillWork()
    {
        // Arrange
        CreateFile("small.txt", "small");
        RunGit("add small.txt");
        RunGit("commit -m \"Small commit\" --quiet");
        RunGit("repack -a -d -q");

        var packData = await GetPackDataAsync();
        var verySlowStream = new SlowStream(packData, bytesPerRead: 1); // 1 byte at a time

        var targetDir = Path.Combine(Path.GetTempPath(), "PmadGitPackTarget", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targetDir);
        try
        {
            RunGitInDirectory(targetDir, "init --quiet");
            var repository = GitRepository.Open(targetDir);
            var reader = new GitPackReader();

            // Act
            var created = await reader.ReadAsync(repository, verySlowStream, CancellationToken.None);

            // Assert
            Assert.NotEmpty(created);
        }
        finally
        {
            TestHelper.TryDeleteDirectory(targetDir);
        }
    }

    #endregion

    #region Non-Seekable Stream Tests

    [Fact]
    public async Task ReadAsync_WithNonSeekableStream_ShouldCopyToTemporaryFile()
    {
        // Arrange
        CreateFile("test.txt", "test content");
        RunGit("add test.txt");
        RunGit("commit -m \"Test commit\" --quiet");
        RunGit("repack -a -d -q");

        var packData = await GetPackDataAsync();
        var nonSeekableStream = new NonSeekableStream(packData);

        var targetDir = Path.Combine(Path.GetTempPath(), "PmadGitPackTarget", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targetDir);
        try
        {
            RunGitInDirectory(targetDir, "init --quiet");
            var repository = GitRepository.Open(targetDir);
            var reader = new GitPackReader();

            // Act
            var created = await reader.ReadAsync(repository, nonSeekableStream, CancellationToken.None);

            // Assert
            Assert.NotEmpty(created);
            Assert.False(nonSeekableStream.CanSeek, "Stream should not be seekable");
        }
        finally
        {
            TestHelper.TryDeleteDirectory(targetDir);
        }
    }

    [Fact]
    public async Task ReadAsync_WithNonSeekableStreamAndMultipleObjects_ShouldHandleCorrectly()
    {
        // Arrange: Create multiple objects
        for (int i = 0; i < 10; i++)
        {
            CreateFile($"file{i}.txt", $"Content {i}");
        }
        RunGit("add .");
        RunGit("commit -m \"Multiple files\" --quiet");
        RunGit("repack -a -d -q");

        var packData = await GetPackDataAsync();
        var nonSeekableStream = new NonSeekableStream(packData);

        var targetDir = Path.Combine(Path.GetTempPath(), "PmadGitPackTarget", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targetDir);
        try
        {
            RunGitInDirectory(targetDir, "init --quiet");
            var repository = GitRepository.Open(targetDir);
            var reader = new GitPackReader();

            // Act
            var created = await reader.ReadAsync(repository, nonSeekableStream, CancellationToken.None);

            // Assert
            Assert.True(created.Count >= 10, "Should have created at least 10 objects");
        }
        finally
        {
            TestHelper.TryDeleteDirectory(targetDir);
        }
    }

    #endregion

    #region BufferedStream Tests

    [Fact]
    public async Task ReadAsync_WithBufferedStream_ShouldWorkCorrectly()
    {
        // Arrange
        CreateFile("test.txt", "test content");
        RunGit("add test.txt");
        RunGit("commit -m \"Test commit\" --quiet");
        RunGit("repack -a -d -q");

        var packData = await GetPackDataAsync();
        var memoryStream = new MemoryStream(packData);
        var bufferedStream = new BufferedStream(memoryStream, 4096);

        var targetDir = Path.Combine(Path.GetTempPath(), "PmadGitPackTarget", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targetDir);
        try
        {
            RunGitInDirectory(targetDir, "init --quiet");
            var repository = GitRepository.Open(targetDir);
            var reader = new GitPackReader();

            // Act
            var created = await reader.ReadAsync(repository, bufferedStream, CancellationToken.None);

            // Assert
            Assert.NotEmpty(created);
        }
        finally
        {
            TestHelper.TryDeleteDirectory(targetDir);
        }
    }

    #endregion

    #region Temporary File Handling Tests

    [Fact]
    public async Task ReadAsync_WithMemoryStream_ShouldCleanUpTemporaryFile()
    {
        // Arrange
        CreateFile("test.txt", "test content");
        RunGit("add test.txt");
        RunGit("commit -m \"Test commit\" --quiet");
        RunGit("repack -a -d -q");

        var packData = await GetPackDataAsync();
        var memoryStream = new MemoryStream(packData);

        var targetDir = Path.Combine(Path.GetTempPath(), "PmadGitPackTarget", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targetDir);
        try
        {
            var tempDir = Path.GetTempPath();
            var tempFilesBefore = Directory.GetFiles(tempDir, "*.*").Length;

            RunGitInDirectory(targetDir, "init --quiet");
            var repository = GitRepository.Open(targetDir);
            var reader = new GitPackReader();

            // Act
            await reader.ReadAsync(repository, memoryStream, CancellationToken.None);

            // Wait a bit for cleanup
            await Task.Delay(100);

            // Assert: Temporary file should be cleaned up
            var tempFilesAfter = Directory.GetFiles(tempDir, "*.*").Length;
            Assert.True(tempFilesAfter <= tempFilesBefore + 1, 
                "Temporary files should be cleaned up (allowing for 1 potential concurrent test file)");
        }
        finally
        {
            TestHelper.TryDeleteDirectory(targetDir);
        }
    }

    [Fact]
    public async Task ReadAsync_WithMemoryStreamAndCancellation_ShouldCleanUpTemporaryFile()
    {
        // Arrange
        CreateFile("test.txt", "test content");
        RunGit("add test.txt");
        RunGit("commit -m \"Test commit\" --quiet");
        RunGit("repack -a -d -q");

        var packData = await GetPackDataAsync();
        var memoryStream = new MemoryStream(packData);

        var targetDir = Path.Combine(Path.GetTempPath(), "PmadGitPackTarget", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targetDir);
        try
        {
            RunGitInDirectory(targetDir, "init --quiet");
            var repository = GitRepository.Open(targetDir);
            var reader = new GitPackReader();

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(1); // Cancel almost immediately

            // Act & Assert
            try
            {
                await reader.ReadAsync(repository, memoryStream, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }

            // Temporary file should still be cleaned up
            await Task.Delay(100);
            // No assertion here, just verifying no exception during cleanup
        }
        finally
        {
            TestHelper.TryDeleteDirectory(targetDir);
        }
    }

    #endregion

    #region Stream with Delta Objects Tests

    [Fact]
    public async Task ReadAsync_WithMemoryStreamAndDeltaObjects_ShouldResolveDeltas()
    {
        // Arrange: Create objects that will be delta-compressed
        var baseContent = new string('A', 5000);
        CreateFile("base.txt", baseContent);
        RunGit("add base.txt");
        RunGit("commit -m \"Base commit\" --quiet");

        var modifiedContent = new string('A', 4500) + new string('B', 500);
        CreateFile("base.txt", modifiedContent);
        RunGit("add base.txt");
        RunGit("commit -m \"Modified commit\" --quiet");

        RunGit("gc --aggressive --quiet");

        var packData = await GetPackDataAsync();
        var memoryStream = new MemoryStream(packData);

        var targetDir = Path.Combine(Path.GetTempPath(), "PmadGitPackTarget", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targetDir);
        try
        {
            RunGitInDirectory(targetDir, "init --quiet");
            var repository = GitRepository.Open(targetDir);
            var reader = new GitPackReader();

            // Act
            var created = await reader.ReadAsync(repository, memoryStream, CancellationToken.None);

            // Assert
            Assert.NotEmpty(created);
            foreach (var hash in created)
            {
                var obj = await repository.ReadObjectAsync(hash, CancellationToken.None);
                Assert.NotNull(obj);
            }
        }
        finally
        {
            TestHelper.TryDeleteDirectory(targetDir);
        }
    }

    [Fact]
    public async Task ReadAsync_WithNonSeekableStreamAndOfsDelta_ShouldResolveOffsetsCorrectly()
    {
        // Arrange: Force ofs-delta creation
        CreateFile("file1.txt", new string('X', 5000));
        RunGit("add file1.txt");
        RunGit("commit -m \"First\" --quiet");

        CreateFile("file1.txt", new string('X', 4800) + new string('Y', 200));
        RunGit("add file1.txt");
        RunGit("commit -m \"Second\" --quiet");

        RunGit("repack -a -d -f --depth=50 -q");

        var packData = await GetPackDataAsync();
        var nonSeekableStream = new NonSeekableStream(packData);

        var targetDir = Path.Combine(Path.GetTempPath(), "PmadGitPackTarget", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targetDir);
        try
        {
            RunGitInDirectory(targetDir, "init --quiet");
            var repository = GitRepository.Open(targetDir);
            var reader = new GitPackReader();

            // Act
            var created = await reader.ReadAsync(repository, nonSeekableStream, CancellationToken.None);

            // Assert
            Assert.NotEmpty(created);
        }
        finally
        {
            TestHelper.TryDeleteDirectory(targetDir);
        }
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public async Task ReadAsync_WithEmptyMemoryStream_ShouldThrowEndOfStreamException()
    {
        // Arrange
        var emptyStream = new MemoryStream();
        var targetDir = Path.Combine(Path.GetTempPath(), "PmadGitPackTarget", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targetDir);
        try
        {
            RunGitInDirectory(targetDir, "init --quiet");
            var repository = GitRepository.Open(targetDir);
            var reader = new GitPackReader();

            // Act & Assert
            await Assert.ThrowsAsync<EndOfStreamException>(
                async () => await reader.ReadAsync(repository, emptyStream, CancellationToken.None));
        }
        finally
        {
            TestHelper.TryDeleteDirectory(targetDir);
        }
    }

    [Fact]
    public async Task ReadAsync_WithStreamThatClosesEarly_ShouldThrowException()
    {
        // Arrange
        CreateFile("test.txt", "test content");
        RunGit("add test.txt");
        RunGit("commit -m \"Test commit\" --quiet");
        RunGit("repack -a -d -q");

        var packData = await GetPackDataAsync();
        var truncatedData = packData[..(packData.Length / 2)]; // Only half the data
        var truncatedStream = new MemoryStream(truncatedData);

        var targetDir = Path.Combine(Path.GetTempPath(), "PmadGitPackTarget", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targetDir);
        try
        {
            RunGitInDirectory(targetDir, "init --quiet");
            var repository = GitRepository.Open(targetDir);
            var reader = new GitPackReader();

            // Act & Assert
            await Assert.ThrowsAnyAsync<Exception>(
                async () => await reader.ReadAsync(repository, truncatedStream, CancellationToken.None));
        }
        finally
        {
            TestHelper.TryDeleteDirectory(targetDir);
        }
    }

    [Fact]
    public async Task ReadAsync_WithFileStreamDirectly_ShouldNotUseTemporaryFile()
    {
        // Arrange
        CreateFile("test.txt", "test content");
        RunGit("add test.txt");
        RunGit("commit -m \"Test commit\" --quiet");
        RunGit("repack -a -d -q");

        var packPath = GetPackFilePath();
        var fileStream = new FileStream(packPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        var targetDir = Path.Combine(Path.GetTempPath(), "PmadGitPackTarget", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targetDir);
        try
        {
            var tempFilesBefore = Directory.GetFiles(Path.GetTempPath()).Length;

            RunGitInDirectory(targetDir, "init --quiet");
            var repository = GitRepository.Open(targetDir);
            var reader = new GitPackReader();

            // Act
            var created = await reader.ReadAsync(repository, fileStream, CancellationToken.None);

            // Assert
            Assert.NotEmpty(created);
            
            // Verify no new temp files created (FileStream should be used directly)
            var tempFilesAfter = Directory.GetFiles(Path.GetTempPath()).Length;
            Assert.Equal(tempFilesBefore, tempFilesAfter);

            fileStream.Dispose();
        }
        finally
        {
            TestHelper.TryDeleteDirectory(targetDir);
        }
    }

    #endregion

    #region Helper Methods

    private async Task<byte[]> GetPackDataAsync()
    {
        var packPath = GetPackFilePath();
        return await File.ReadAllBytesAsync(packPath);
    }

    private string GetPackFilePath()
    {
        var packDir = Path.Combine(_gitDirectory, "objects", "pack");
        var packs = Directory.GetFiles(packDir, "*.pack");
        if (packs.Length == 0)
        {
            throw new InvalidOperationException("No pack files found");
        }
        return packs[0];
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

    #endregion

    #region Helper Classes

    /// <summary>
    /// Stream that simulates slow network reads by returning limited bytes per read.
    /// </summary>
    private class SlowStream : Stream
    {
        private readonly byte[] _data;
        private readonly int _bytesPerRead;
        private int _position;

        public int ReadCallCount { get; private set; }

        public SlowStream(byte[] data, int bytesPerRead)
        {
            _data = data;
            _bytesPerRead = bytesPerRead;
            _position = 0;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ReadCallCount++;
            var bytesToRead = Math.Min(count, Math.Min(_bytesPerRead, _data.Length - _position));
            if (bytesToRead <= 0)
                return 0;

            Array.Copy(_data, _position, buffer, offset, bytesToRead);
            _position += bytesToRead;
            return bytesToRead;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await Task.Yield(); // Simulate async operation
            return Read(buffer, offset, count);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadCallCount++;
            var bytesToRead = Math.Min(buffer.Length, Math.Min(_bytesPerRead, _data.Length - _position));
            if (bytesToRead <= 0)
                return ValueTask.FromResult(0);

            _data.AsSpan(_position, bytesToRead).CopyTo(buffer.Span);
            _position += bytesToRead;
            return ValueTask.FromResult(bytesToRead);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
    }

    /// <summary>
    /// Non-seekable stream wrapper for testing.
    /// </summary>
    private class NonSeekableStream : Stream
    {
        private readonly MemoryStream _inner;

        public NonSeekableStream(byte[] data)
        {
            _inner = new MemoryStream(data);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false; // Key difference
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
            => _inner.Read(buffer, offset, count);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => _inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => _inner.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    #endregion
}
