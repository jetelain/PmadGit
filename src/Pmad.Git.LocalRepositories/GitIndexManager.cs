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

        var trackedPrefixes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in index.Entries)
        {
            var slashIdx = entry.Path.IndexOf('/');
            while (slashIdx >= 0)
            {
                trackedPrefixes.Add(entry.Path[..slashIdx]);
                slashIdx = entry.Path.IndexOf('/', slashIdx + 1);
            }
        }

        ScanWorkingDirectory(new DirectoryInfo(WorkingDirectory), string.Empty, ignoreMatcher, trackedPrefixes, diskFiles);

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
                    stagedStatus = normalIndexEntry.Hash == headEntry.Hash
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
                        var mtimeSec = (uint)Math.Max(0, new DateTimeOffset(fileInfo.LastWriteTimeUtc).ToUnixTimeSeconds());
                        if (mtimeSec == normalIndexEntry.MtimeSeconds)
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

        var index = await GitIndex.ReadAsync(IndexPath, _repository.HashLengthBytes, cancellationToken).ConfigureAwait(false);

        foreach (var rawPath in relativePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = rawPath.TrimStart('/', '\\').Replace('\\', '/');
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
                        stream.Length,
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

        var index = await GitIndex.ReadAsync(IndexPath, _repository.HashLengthBytes, cancellationToken).ConfigureAwait(false);
        var headFiles = await GetHeadFilesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var rawPath in relativePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = rawPath.TrimStart('/', '\\').Replace('\\', '/');

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

    /// <summary>
    /// Unstages all files, resetting the entire index (.git/index) to match HEAD.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    public async Task UnstageAllAsync(CancellationToken cancellationToken = default)
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

    /// <summary>
    /// Discards working tree changes for the specified file by restoring its content
    /// from the index (or HEAD if not in index).
    /// </summary>
    /// <param name="relativePath">Repository-relative file path.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    public async Task RestoreFileAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var path = relativePath.TrimStart('/', '\\').Replace('\\', '/');
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
        Dictionary<string, FileInfo> results)
    {
        if (!directory.Exists)
        {
            return;
        }

        foreach (var file in directory.EnumerateFiles())
        {
            var relPath = string.IsNullOrEmpty(relativePrefix) ? file.Name : $"{relativePrefix}/{file.Name}";
            if (!ignoreMatcher.IsIgnored(relPath, isDirectory: false))
            {
                results[relPath] = file;
            }
        }

        foreach (var subDir in directory.EnumerateDirectories())
        {
            var dirRelPath = string.IsNullOrEmpty(relativePrefix) ? subDir.Name : $"{relativePrefix}/{subDir.Name}";

            // If ignored, skip unless index tracks files inside this directory
            if (ignoreMatcher.IsIgnored(dirRelPath, isDirectory: true) && !trackedPrefixes.Contains(dirRelPath))
            {
                continue;
            }

            ScanWorkingDirectory(subDir, dirRelPath, ignoreMatcher, trackedPrefixes, results);
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
