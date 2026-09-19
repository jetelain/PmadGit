using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Manages the Git working tree, staging area (.git/index), and their synchronization
/// with the Git object database.
/// </summary>
public sealed class GitIndexManager
{
    private sealed class IndexMutationLock : IDisposable
    {
        private readonly IDisposable _refLock;
        private readonly FileStream _lockFileStream;
        private readonly string _lockFilePath;
        private bool _disposed;

        public IndexMutationLock(IDisposable refLock, FileStream lockFileStream, string lockFilePath)
        {
            _refLock = refLock;
            _lockFileStream = lockFileStream;
            _lockFilePath = lockFilePath;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _lockFileStream.Dispose();
                try
                {
                    if (File.Exists(_lockFilePath))
                    {
                        File.Delete(_lockFilePath);
                    }
                }
                catch
                {
                    // Ignore deletion failure if file was moved/deleted
                }
                _refLock.Dispose();
            }
        }
    }

    private readonly IGitRepository _repository;

    /// <summary>
    /// Gets the underlying repository.
    /// </summary>
    public IGitRepository Repository => _repository;

    /// <summary>
    /// Gets the absolute path to the repository working tree root.
    /// </summary>
    public string WorkingDirectory { get; }

    /// <summary>
    /// Gets the absolute path to the .git/index file.
    /// </summary>
    public string IndexPath { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="GitIndexManager"/> class.
    /// </summary>
    /// <param name="repository">The git repository instance.</param>
    /// <param name="workingDirectory">Absolute path to the working tree.</param>
    /// <param name="indexPath">Optional custom path to the index file (defaults to .git/index).</param>
    public GitIndexManager(IGitRepository repository, string workingDirectory, string? indexPath = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        WorkingDirectory = workingDirectory ?? throw new ArgumentNullException(nameof(workingDirectory));
        IndexPath = indexPath ?? Path.Combine(repository.GitDirectory, "index");
    }

    /// <summary>
    /// Inspects the working tree and staging area, returning status for all modified,
    /// staged, deleted, untracked, and conflicted files.
    /// </summary>
    /// <param name="includeUntracked">Whether to detect untracked files in the working tree.</param>
    /// <param name="includeClean">Whether to include unmodified/clean files in the result.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>A <see cref="GitStatusResult"/> describing the repository state.</returns>
    public async Task<GitStatusResult> GetStatusAsync(
        bool includeUntracked = true,
        bool includeClean = false,
        CancellationToken cancellationToken = default)
    {
        var index = await GitIndex.ReadAsync(IndexPath, _repository.HashLengthBytes, cancellationToken).ConfigureAwait(false);

        var headFiles = await GetHeadFilesAsync(cancellationToken).ConfigureAwait(false);

        var indexByPath = index.Entries.GroupBy(e => e.Path, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var ignoreMatcher = GitIgnoreMatcher.Load(WorkingDirectory);
        var diskFiles = new Dictionary<string, FileInfo>(StringComparer.Ordinal);

        var trackedFiles = new HashSet<string>(StringComparer.Ordinal);
        trackedFiles.UnionWith(headFiles.Keys);
        trackedFiles.UnionWith(indexByPath.Keys);

        var trackedPrefixes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in trackedFiles)
        {
            var slashIdx = path.IndexOf('/');
            while (slashIdx >= 0)
            {
                trackedPrefixes.Add(path[..slashIdx]);
                slashIdx = path.IndexOf('/', slashIdx + 1);
            }
        }

        ScanWorkingDirectory(new DirectoryInfo(WorkingDirectory), string.Empty, ignoreMatcher, trackedPrefixes, trackedFiles, diskFiles);

        var allPaths = new HashSet<string>(StringComparer.Ordinal);
        allPaths.UnionWith(headFiles.Keys);
        allPaths.UnionWith(indexByPath.Keys);
        if (includeUntracked)
        {
            allPaths.UnionWith(diskFiles.Keys);
        }

        var entries = new List<GitStatusEntry>();

        foreach (var path in allPaths.OrderBy(p => p, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            headFiles.TryGetValue(path, out var headEntry);
            var headHash = headEntry?.Hash;

            indexByPath.TryGetValue(path, out var indexEntries);
            var isConflicted = indexEntries != null && indexEntries.Any(e => e.Stage > 0);
            var normalIndexEntry = indexEntries?.FirstOrDefault(e => e.Stage == 0);
            var indexHash = normalIndexEntry?.Hash;

            diskFiles.TryGetValue(path, out var fileInfo);
            var onDisk = fileInfo != null && fileInfo.Exists;

            GitFileStatus stagedStatus;
            if (isConflicted)
            {
                stagedStatus = GitFileStatus.Conflicted;
            }
            else if (normalIndexEntry != null)
            {
                if (headEntry != null)
                {
                    stagedStatus = (normalIndexEntry.Hash == headEntry.Hash && normalIndexEntry.FileMode == headEntry.Mode)
                        ? GitFileStatus.Clean
                        : GitFileStatus.StagedModified;
                }
                else
                {
                    stagedStatus = GitFileStatus.StagedNew;
                }
            }
            else
            {
                stagedStatus = headEntry != null
                    ? GitFileStatus.StagedDeleted
                    : GitFileStatus.Clean;
            }

            GitFileStatus workTreeStatus;
            GitHash? workTreeHash = null;

            if (isConflicted)
            {
                workTreeStatus = GitFileStatus.Conflicted;
            }
            else if (normalIndexEntry != null)
            {
                if (!onDisk)
                {
                    workTreeStatus = GitFileStatus.Deleted;
                }
                else
                {
                    // Stat cache fast-path check
                    if (fileInfo!.Length != normalIndexEntry.FileSize)
                    {
                        workTreeStatus = GitFileStatus.Modified;
                    }
                    else
                    {
                        var mtimeUtc = fileInfo.LastWriteTimeUtc;
                        var mtimeSec = (uint)Math.Max(0, new DateTimeOffset(mtimeUtc).ToUnixTimeSeconds());
                        var mtimeNano = (uint)((mtimeUtc.Ticks % TimeSpan.TicksPerSecond) * 100);
                        if (mtimeSec == normalIndexEntry.MtimeSeconds && mtimeNano == normalIndexEntry.MtimeNanoseconds)
                        {
                            // Stat cache matches: content is unmodified
                            workTreeStatus = GitFileStatus.Clean;
                            workTreeHash = normalIndexEntry.Hash;
                        }
                        else
                        {
                            // Timestamp changed: verify content hash
                            var computedHash = await ComputeFileBlobHashAsync(fileInfo.FullName, cancellationToken).ConfigureAwait(false);
                            workTreeHash = computedHash;
                            workTreeStatus = computedHash == normalIndexEntry.Hash
                                ? GitFileStatus.Clean
                                : GitFileStatus.Modified;
                        }
                    }
                }
            }
            else
            {
                workTreeStatus = onDisk ? GitFileStatus.Untracked : GitFileStatus.Clean;
            }

            var entry = new GitStatusEntry(path, stagedStatus, workTreeStatus, headHash, indexHash, workTreeHash);
            if (includeClean || !entry.IsClean)
            {
                entries.Add(entry);
            }
        }

        return new GitStatusResult(entries);
    }

    /// <summary>
    /// Quick check returning true if the working tree has no modified, deleted,
    /// untracked, staged, or conflicted changes.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>True if the working tree is clean; false otherwise.</returns>
    public async Task<bool> IsWorkingTreeCleanAsync(CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(includeUntracked: true, includeClean: false, cancellationToken).ConfigureAwait(false);
        return status.IsClean;
    }

    /// <summary>
    /// Stages a file into the index (.git/index).
    /// If the file is deleted on disk, removes it from the index.
    /// </summary>
    /// <param name="relativePath">Repository-relative path of the file.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    public Task StageAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        return StageAsync(new[] { relativePath }, cancellationToken);
    }

    /// <summary>
    /// Stages multiple files into the index in a single batch.
    /// </summary>
    /// <param name="relativePaths">Collection of repository-relative paths.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    public async Task StageAsync(IEnumerable<string> relativePaths, CancellationToken cancellationToken = default)
    {
        if (relativePaths is null)
        {
            throw new ArgumentNullException(nameof(relativePaths));
        }

        var pathsList = relativePaths.Select(NormalizeAndValidateRelativePath).ToList();

        using (await AcquireIndexMutationLockAsync(cancellationToken).ConfigureAwait(false))
        {
            var index = await GitIndex.ReadAsync(IndexPath, _repository.HashLengthBytes, cancellationToken).ConfigureAwait(false);

            foreach (var path in pathsList)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fullPath = Path.Combine(WorkingDirectory, path);

                if (File.Exists(fullPath))
                {
                    var fileInfo = new FileInfo(fullPath);
                    GitHash blobHash;
                    var options = new FileStreamOptions
                    {
                        Mode = FileMode.Open,
                        Access = FileAccess.Read,
                        Share = FileShare.ReadWrite | FileShare.Delete,
                        Options = FileOptions.Asynchronous | FileOptions.SequentialScan
                    };

                    await using (var stream = new FileStream(fullPath, options))
                    {
                        blobHash = await _repository.ObjectStore.WriteObjectAsync(
                            GitObjectType.Blob,
                            stream,
                            fileInfo.Length,
                            cancellationToken).ConfigureAwait(false);
                    }

                    var entry = GitIndexEntry.FromFileInfo(path, fileInfo, blobHash);
                    // Clear any merge conflict stages
                    index.Remove(path, stage: 1);
                    index.Remove(path, stage: 2);
                    index.Remove(path, stage: 3);
                    index.AddOrUpdate(entry);
                }
                else
                {
                    // File deleted on disk: remove all stages from index
                    index.Remove(path, stage: 0);
                    index.Remove(path, stage: 1);
                    index.Remove(path, stage: 2);
                    index.Remove(path, stage: 3);
                }
            }

            await index.WriteAsync(IndexPath, _repository.HashLengthBytes, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Stages all modified, added, deleted, and conflicted files in the working tree.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    public async Task StageAllAsync(CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(includeUntracked: true, includeClean: false, cancellationToken).ConfigureAwait(false);
        var pathsToStage = status.Entries
            .Where(e => e.HasWorkingTreeChanges || e.IsConflicted)
            .Select(e => e.Path)
            .ToList();

        if (pathsToStage.Count > 0)
        {
            await StageAsync(pathsToStage, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Unstages a file by reverting its index entry to match HEAD (or removing it if not in HEAD).
    /// </summary>
    /// <param name="relativePath">Repository-relative file path.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    public Task UnstageAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        return UnstageAsync(new[] { relativePath }, cancellationToken);
    }

    /// <summary>
    /// Unstages multiple files by reverting their index entries to match HEAD.
    /// </summary>
    /// <param name="relativePaths">Collection of repository-relative file paths.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    public async Task UnstageAsync(IEnumerable<string> relativePaths, CancellationToken cancellationToken = default)
    {
        if (relativePaths is null)
        {
            throw new ArgumentNullException(nameof(relativePaths));
        }

        var pathsList = relativePaths.Select(NormalizeAndValidateRelativePath).ToList();

        using (await AcquireIndexMutationLockAsync(cancellationToken).ConfigureAwait(false))
        {
            var index = await GitIndex.ReadAsync(IndexPath, _repository.HashLengthBytes, cancellationToken).ConfigureAwait(false);
            var headFiles = await GetHeadFilesAsync(cancellationToken).ConfigureAwait(false);

            foreach (var path in pathsList)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Remove all conflict stages
                index.Remove(path, stage: 1);
                index.Remove(path, stage: 2);
                index.Remove(path, stage: 3);

                if (headFiles.TryGetValue(path, out var headEntry))
                {
                    var entry = new GitIndexEntry(path, headEntry.Hash, headEntry.Mode);
                    index.AddOrUpdate(entry);
                }
                else
                {
                    // File was not in HEAD: completely remove from index
                    index.Remove(path, stage: 0);
                }
            }

            await index.WriteAsync(IndexPath, _repository.HashLengthBytes, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Unstages all files, resetting the entire index (.git/index) to match HEAD.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    public async Task UnstageAllAsync(CancellationToken cancellationToken = default)
    {
        using (await AcquireIndexMutationLockAsync(cancellationToken).ConfigureAwait(false))
        {
            var headFiles = await GetHeadFilesAsync(cancellationToken).ConfigureAwait(false);
            var newIndex = new GitIndex();

            foreach (var (path, headEntry) in headFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = new GitIndexEntry(path, headEntry.Hash, headEntry.Mode);
                newIndex.AddOrUpdate(entry);
            }

            await newIndex.WriteAsync(IndexPath, _repository.HashLengthBytes, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Discards working tree changes for the specified file by restoring its content
    /// from the index (or HEAD if not in index).
    /// </summary>
    /// <param name="relativePath">Repository-relative file path.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    public async Task RestoreFileAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var path = NormalizeAndValidateRelativePath(relativePath);
        var index = await GitIndex.ReadAsync(IndexPath, _repository.HashLengthBytes, cancellationToken).ConfigureAwait(false);
        var entry = index.FindEntry(path);

        GitHash? targetHash = null;
        if (entry is not null)
        {
            targetHash = entry.Hash;
        }
        else
        {
            var headFiles = await GetHeadFilesAsync(cancellationToken).ConfigureAwait(false);
            if (headFiles.TryGetValue(path, out var headEntry))
            {
                targetHash = headEntry.Hash;
            }
        }

        var fullPath = Path.Combine(WorkingDirectory, path);

        if (targetHash.HasValue)
        {
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (File.Exists(fullPath))
            {
                File.SetAttributes(fullPath, FileAttributes.Normal);
            }

            await using var objectStream = await _repository.ObjectStore.ReadObjectStreamAsync(targetHash.Value, cancellationToken).ConfigureAwait(false);
            var options = new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous
            };

            await using var fileStream = new FileStream(fullPath, options);
            await objectStream.Content.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
        }
        else if (File.Exists(fullPath))
        {
            // Untracked file: discard removes it
            File.Delete(fullPath);
        }
    }

    /// <summary>
    /// Discards all working tree modifications and deletions by restoring files from the index.
    /// </summary>
    /// <param name="removeUntracked">Whether to also delete untracked files from disk.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    public async Task RestoreAllAsync(bool removeUntracked = false, CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(includeUntracked: removeUntracked, includeClean: false, cancellationToken).ConfigureAwait(false);

        foreach (var entry in status.ModifiedEntries.Concat(status.DeletedEntries))
        {
            await RestoreFileAsync(entry.Path, cancellationToken).ConfigureAwait(false);
        }

        if (removeUntracked)
        {
            foreach (var entry in status.UntrackedEntries)
            {
                var fullPath = Path.Combine(WorkingDirectory, entry.Path);
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }
            }
        }
    }

    private string NormalizeAndValidateRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Path cannot be empty.", nameof(relativePath));
        }

        if (Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException($"Path cannot be rooted: '{relativePath}'", nameof(relativePath));
        }

        var normalized = relativePath.Replace('\\', '/').Trim('/');
        if (string.IsNullOrEmpty(normalized))
        {
            throw new ArgumentException("Path cannot be empty.", nameof(relativePath));
        }

        var segments = normalized.Split('/');
        foreach (var segment in segments)
        {
            if (segment == "." || segment == "..")
            {
                throw new ArgumentException($"Path traversal is not allowed: '{relativePath}'", nameof(relativePath));
            }
        }

        var fullPath = Path.GetFullPath(Path.Combine(WorkingDirectory, normalized));
        var workingDirFull = Path.GetFullPath(WorkingDirectory);
        if (!workingDirFull.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) &&
            !workingDirFull.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
        {
            workingDirFull += Path.DirectorySeparatorChar;
        }

        if (!fullPath.StartsWith(workingDirFull, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Path escapes the working directory: '{relativePath}'", nameof(relativePath));
        }

        return normalized;
    }

    private async Task<IDisposable> AcquireIndexMutationLockAsync(CancellationToken cancellationToken)
    {
        var refLock = await _repository.LockManager.AcquireReferenceLockAsync("index", cancellationToken).ConfigureAwait(false);
        FileStream? lockStream = null;
        var lockFilePath = IndexPath + ".lock";
        try
        {
            var dir = Path.GetDirectoryName(lockFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            const int maxAttempts = 10;
            for (var i = 0; i < maxAttempts; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    lockStream = new FileStream(lockFilePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                    break;
                }
                catch (IOException) when (i < maxAttempts - 1)
                {
                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                }
            }

            if (lockStream == null)
            {
                throw new IOException($"Could not acquire index lock '{lockFilePath}'. The file already exists or is locked by another process.");
            }

            return new IndexMutationLock(refLock, lockStream, lockFilePath);
        }
        catch
        {
            lockStream?.Dispose();
            try
            {
                if (File.Exists(lockFilePath))
                {
                    File.Delete(lockFilePath);
                }
            }
            catch
            {
            }
            refLock.Dispose();
            throw;
        }
    }

    private async Task<Dictionary<string, GitTreeEntry>> GetHeadFilesAsync(CancellationToken cancellationToken)
    {
        var headFiles = new Dictionary<string, GitTreeEntry>(StringComparer.Ordinal);
        var headHash = await _repository.ReferenceStore.TryResolveReferenceAsync("HEAD", cancellationToken).ConfigureAwait(false);
        if (headHash.HasValue)
        {
            await foreach (var item in _repository.EnumerateCommitTreeAsync(headHash.Value.ToString(), null, SearchOption.AllDirectories, cancellationToken).ConfigureAwait(false))
            {
                if (item.Entry.Kind == GitTreeEntryKind.Blob)
                {
                    headFiles[item.Path] = item.Entry;
                }
            }
        }

        return headFiles;
    }

    private static void ScanWorkingDirectory(
        DirectoryInfo directory,
        string relativePrefix,
        GitIgnoreMatcher ignoreMatcher,
        HashSet<string> trackedPrefixes,
        HashSet<string> trackedFiles,
        Dictionary<string, FileInfo> results)
    {
        if (!directory.Exists)
        {
            return;
        }

        foreach (var file in directory.EnumerateFiles())
        {
            var relPath = string.IsNullOrEmpty(relativePrefix) ? file.Name : $"{relativePrefix}/{file.Name}";
            if (trackedFiles.Contains(relPath) || !ignoreMatcher.IsIgnored(relPath, isDirectory: false))
            {
                results[relPath] = file;
            }
        }

        foreach (var subDir in directory.EnumerateDirectories())
        {
            var dirRelPath = string.IsNullOrEmpty(relativePrefix) ? subDir.Name : $"{relativePrefix}/{subDir.Name}";

            // If ignored, skip unless index/HEAD tracks files inside this directory or negated rules exist
            if (ignoreMatcher.IsIgnored(dirRelPath, isDirectory: true) &&
                !trackedPrefixes.Contains(dirRelPath) &&
                !ignoreMatcher.HasNegatedRuleUnder(dirRelPath))
            {
                continue;
            }

            ScanWorkingDirectory(subDir, dirRelPath, ignoreMatcher, trackedPrefixes, trackedFiles, results);
        }
    }

    private async Task<GitHash> ComputeFileBlobHashAsync(string fullPath, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(fullPath);
        var algorithmName = GitHashHelper.GetAlgorithmName(_repository.HashLengthBytes);
        using var hashAlgo = IncrementalHash.CreateHash(algorithmName);

        var header = Encoding.ASCII.GetBytes($"blob {fileInfo.Length}\0");
        hashAlgo.AppendData(header);

        var options = new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.ReadWrite | FileShare.Delete,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        };

        await using var stream = new FileStream(fullPath, options);
        var buffer = new byte[81920];
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            hashAlgo.AppendData(buffer, 0, bytesRead);
        }

        var hashBytes = hashAlgo.GetHashAndReset();
        return GitHash.FromBytes(hashBytes);
    }
}
