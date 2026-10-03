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
        private bool _disposed;

        public IndexMutationLock(IDisposable refLock, FileStream lockFileStream)
        {
            _refLock = refLock;
            _lockFileStream = lockFileStream;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                // Closing the stream automatically deletes the file because it was created
                // with FileOptions.DeleteOnClose. We NEVER call File.Delete separately after
                // closing, as that could race and delete a subsequent owner's lock file.
                _lockFileStream.Dispose();
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
                    var currentMode = GitIndexEntry.GetFileMode(fileInfo!);
                    if (currentMode != normalIndexEntry.FileMode)
                    {
                        // Executable mode changed in working tree
                        workTreeStatus = GitFileStatus.Modified;
                    }
                    else if (fileInfo!.Length != normalIndexEntry.FileSize)
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
                            // Stat cache matches: content and mode are unmodified
                            workTreeStatus = GitFileStatus.Clean;
                            workTreeHash = normalIndexEntry.Hash;
                        }
                        else
                        {
                            // Timestamp changed: verify content hash
                            var computedHash = await ComputeFileBlobHashAsync(fileInfo.FullName, cancellationToken).ConfigureAwait(false);
                            workTreeHash = computedHash;
                            workTreeStatus = (computedHash == normalIndexEntry.Hash && currentMode == normalIndexEntry.FileMode)
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
            await StageCoreAsync(pathsList, cancellationToken).ConfigureAwait(false);
        }
    }

    internal async Task StageCoreAsync(IReadOnlyList<string> pathsList, CancellationToken cancellationToken)
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

                var existingEntry = index.FindEntry(path, stage: 0) ??
                                    index.Entries.FirstOrDefault(e => e.Path.Equals(path.Replace('\\', '/'), StringComparison.Ordinal));
                int? preserveMode = (OperatingSystem.IsWindows() && existingEntry != null)
                    ? existingEntry.FileMode
                    : null;

                var entry = GitIndexEntry.FromFileInfo(path, fileInfo, blobHash, preserveFileMode: preserveMode);
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
        using (await AcquireIndexMutationLockAsync(cancellationToken).ConfigureAwait(false))
        {
            await StageAllCoreAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    internal async Task StageAllCoreAsync(CancellationToken cancellationToken)
    {
        var status = await GetStatusAsync(includeUntracked: true, includeClean: false, cancellationToken).ConfigureAwait(false);
        var pathsToStage = status.Entries
            .Where(e => e.HasWorkingTreeChanges || e.IsConflicted)
            .Select(e => e.Path)
            .ToList();

        if (pathsToStage.Count > 0)
        {
            await StageCoreAsync(pathsToStage, cancellationToken).ConfigureAwait(false);
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
    /// Restores index entries for the specified relative paths from a source tree (or HEAD if source is null).
    /// </summary>
    /// <param name="relativePaths">Collection of repository-relative file paths.</param>
    /// <param name="source">Optional commit or tree-ish to restore from.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    public async Task RestoreIndexAsync(IEnumerable<string> relativePaths, string? source = null, CancellationToken cancellationToken = default)
    {
        if (relativePaths is null)
        {
            throw new ArgumentNullException(nameof(relativePaths));
        }

        var pathsList = relativePaths.Select(NormalizeAndValidateRelativePath).ToList();

        using (await AcquireIndexMutationLockAsync(cancellationToken).ConfigureAwait(false))
        {
            var index = await GitIndex.ReadAsync(IndexPath, _repository.HashLengthBytes, cancellationToken).ConfigureAwait(false);
            var sourceFiles = source != null
                ? await GetTreeFilesAsync(source, cancellationToken).ConfigureAwait(false)
                : await GetHeadFilesAsync(cancellationToken).ConfigureAwait(false);

            foreach (var path in pathsList)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Remove all conflict stages
                index.Remove(path, stage: 1);
                index.Remove(path, stage: 2);
                index.Remove(path, stage: 3);

                if (sourceFiles.TryGetValue(path, out var sourceEntry))
                {
                    var entry = new GitIndexEntry(path, sourceEntry.Hash, sourceEntry.Mode);
                    index.AddOrUpdate(entry);
                }
                else
                {
                    // File was not in source: completely remove from index
                    index.Remove(path, stage: 0);
                }
            }

            await index.WriteAsync(IndexPath, _repository.HashLengthBytes, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Unstages multiple files by reverting their index entries to match HEAD.
    /// </summary>
    /// <param name="relativePaths">Collection of repository-relative file paths.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    public Task UnstageAsync(IEnumerable<string> relativePaths, CancellationToken cancellationToken = default) =>
        RestoreIndexAsync(relativePaths, null, cancellationToken);

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
    /// and executable file mode from the index (or HEAD if not in index).
    /// </summary>
    /// <param name="relativePath">Repository-relative file path.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    public Task RestoreFileAsync(string relativePath, CancellationToken cancellationToken = default) =>
        RestoreFileAsync(relativePath, null, cancellationToken);

    /// <summary>
    /// Discards working tree changes for the specified file by restoring its content
    /// and executable file mode from the index, HEAD, or a specified source tree-ish.
    /// </summary>
    /// <param name="relativePath">Repository-relative file path.</param>
    /// <param name="source">Optional commit or tree-ish to restore from.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    public async Task RestoreFileAsync(string relativePath, string? source, CancellationToken cancellationToken = default)
    {
        var path = NormalizeAndValidateRelativePath(relativePath);

        GitHash? targetHash = null;
        int? targetMode = null;

        if (source is not null)
        {
            var sourceFiles = await GetTreeFilesAsync(source, cancellationToken).ConfigureAwait(false);
            if (sourceFiles.TryGetValue(path, out var sourceEntry))
            {
                targetHash = sourceEntry.Hash;
                targetMode = sourceEntry.Mode;
            }
        }
        else
        {
            var index = await GitIndex.ReadAsync(IndexPath, _repository.HashLengthBytes, cancellationToken).ConfigureAwait(false);
            var entry = index.FindEntry(path);

            if (entry is not null)
            {
                targetHash = entry.Hash;
                targetMode = entry.FileMode;
            }
            else
            {
                var headFiles = await GetHeadFilesAsync(cancellationToken).ConfigureAwait(false);
                if (headFiles.TryGetValue(path, out var headEntry))
                {
                    targetHash = headEntry.Hash;
                    targetMode = headEntry.Mode;
                }
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

            await using (var fileStream = new FileStream(fullPath, options))
            {
                await objectStream.Content.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            }

            // Restore executable file mode if required on Unix platforms
            if (targetMode.HasValue && !OperatingSystem.IsWindows())
            {
                try
                {
                    var currentUnixMode = File.GetUnixFileMode(fullPath);
                    if (targetMode.Value == 33261) // 100755
                    {
                        File.SetUnixFileMode(fullPath, currentUnixMode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
                    }
                    else if (targetMode.Value == 33188) // 100644
                    {
                        File.SetUnixFileMode(fullPath, currentUnixMode & ~(UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute));
                    }
                }
                catch
                {
                    // Best effort for file systems that do not support Unix permissions
                }
            }
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

    private sealed record PlannedMove(string SourcePath, string DestinationPath, List<GitIndexEntry> Entries, bool IsCaseOnly);
    private sealed record PlannedDirMove(string SourceDir, string DestinationDir, bool IsCaseOnly);

    /// <summary>
    /// Moves or renames a tracked file or directory in the working tree and index.
    /// </summary>
    public Task MoveAsync(string sourcePath, string destinationPath, bool force = false, CancellationToken cancellationToken = default)
    {
        return MoveAsync([sourcePath], destinationPath, new GitMoveOptions { Force = force }, cancellationToken);
    }

    /// <summary>
    /// Moves multiple tracked files or directories into a target destination directory in the working tree and index.
    /// </summary>
    public Task MoveAsync(IEnumerable<string> sourcePaths, string destinationDirectory, bool force = false, CancellationToken cancellationToken = default)
    {
        return MoveAsync(sourcePaths, destinationDirectory, new GitMoveOptions { Force = force }, cancellationToken);
    }

    /// <summary>
    /// Moves tracked files or directories with advanced options (force, skip errors, dry-run).
    /// </summary>
    public async Task<GitMoveResult> MoveAsync(
        IEnumerable<string> sourcePaths,
        string destinationPath,
        GitMoveOptions options,
        CancellationToken cancellationToken = default)
    {
        if (sourcePaths is null)
        {
            throw new ArgumentNullException(nameof(sourcePaths));
        }
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException("Destination path cannot be empty.", nameof(destinationPath));
        }

        var sourcesList = sourcePaths.ToList();
        if (sourcesList.Count == 0)
        {
            return new GitMoveResult { MovedItems = [] };
        }

        using (await AcquireIndexMutationLockAsync(cancellationToken).ConfigureAwait(false))
        {
            return await MoveCoreAsync(sourcesList, destinationPath, options, cancellationToken).ConfigureAwait(false);
        }
    }

    internal async Task<GitMoveResult> MoveCoreAsync(
        IReadOnlyList<string> sourcesList,
        string destinationPath,
        GitMoveOptions opt,
        CancellationToken cancellationToken)
    {
        var index = await GitIndex.ReadAsync(IndexPath, _repository.HashLengthBytes, cancellationToken).ConfigureAwait(false);
        var isCaseInsensitiveFs = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();
        var destHasTrailingSlash = destinationPath.EndsWith('/') || destinationPath.EndsWith('\\');

        string normalizedDest;
        bool destIsDir;
        if (destinationPath == "." || destinationPath == "./" || destinationPath == ".\\")
        {
            normalizedDest = "";
            destIsDir = true;
        }
        else
        {
            normalizedDest = NormalizeAndValidateRelativePath(destinationPath);
            var fullDest = Path.Combine(WorkingDirectory, normalizedDest);
            destIsDir = Directory.Exists(fullDest);
            if (!destIsDir && index.Entries.Any(e => e.Path.StartsWith(normalizedDest + "/", StringComparison.Ordinal)))
            {
                destIsDir = true;
            }
        }

        if (destHasTrailingSlash && !destIsDir)
        {
            throw new InvalidOperationException($"destination '{destinationPath}' is not a directory");
        }

        if (sourcesList.Count > 1 && !destIsDir)
        {
            throw new InvalidOperationException($"destination '{destinationPath}' is not a directory");
        }

        static string CombineRelative(string dir, string file)
        {
            return string.IsNullOrEmpty(dir) ? file : dir + "/" + file;
        }

        var plannedFileMoves = new List<PlannedMove>();
        var plannedDirMoves = new List<PlannedDirMove>();

        foreach (var source in sourcesList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string normalizedSrc;
            try
            {
                normalizedSrc = NormalizeAndValidateRelativePath(source);
            }
            catch
            {
                if (opt.SkipErrors)
                {
                    continue;
                }
                throw;
            }

            var fullSrc = Path.Combine(WorkingDirectory, normalizedSrc);
            var srcIsFile = File.Exists(fullSrc);
            var srcIsDir = Directory.Exists(fullSrc);

            if (!srcIsFile && !srcIsDir)
            {
                if (index.Entries.Any(e => e.Path.Equals(normalizedSrc, StringComparison.Ordinal)))
                {
                    srcIsFile = true;
                }
                else if (index.Entries.Any(e => e.Path.StartsWith(normalizedSrc + "/", StringComparison.Ordinal)))
                {
                    srcIsDir = true;
                }
                else
                {
                    if (opt.SkipErrors)
                    {
                        continue;
                    }
                    throw new FileNotFoundException($"bad source, source={source}, destination={destinationPath}");
                }
            }

            if (srcIsFile)
            {
                var fileEntries = index.Entries.Where(e => e.Path.Equals(normalizedSrc, StringComparison.Ordinal)).ToList();
                if (fileEntries.Count == 0)
                {
                    if (opt.SkipErrors)
                    {
                        continue;
                    }
                    throw new InvalidOperationException($"not under version control, source={source}, destination={destinationPath}");
                }

                if (fileEntries.Any(e => e.Stage != 0))
                {
                    if (opt.SkipErrors)
                    {
                        continue;
                    }
                    throw new InvalidOperationException($"conflicted, source={source}, destination={destinationPath}");
                }

                var destFilePath = destIsDir
                    ? CombineRelative(normalizedDest, Path.GetFileName(normalizedSrc))
                    : normalizedDest;

                try
                {
                    NormalizeAndValidateRelativePath(destFilePath);
                }
                catch
                {
                    if (opt.SkipErrors)
                    {
                        continue;
                    }
                    throw;
                }

                if (normalizedSrc.Equals(destFilePath, StringComparison.Ordinal))
                {
                    if (opt.SkipErrors)
                    {
                        continue;
                    }
                    throw new InvalidOperationException($"destination exists, source={source}, destination={destinationPath}");
                }

                // Reject if target has indexed descendants (file/directory conflict in index)
                if (index.Entries.Any(e => e.Path.StartsWith(destFilePath + "/", StringComparison.Ordinal)))
                {
                    if (opt.SkipErrors)
                    {
                        continue;
                    }
                    throw new InvalidOperationException($"destination exists, source={source}, destination={destFilePath}");
                }

                // Reject if any ancestor of target is an indexed file
                var segs = destFilePath.Split('/');
                var ancestorConflict = false;
                var currentAncestor = "";
                for (var s = 0; s < segs.Length - 1; s++)
                {
                    currentAncestor = CombineRelative(currentAncestor, segs[s]);
                    if (index.Entries.Any(e => e.Path.Equals(currentAncestor, StringComparison.Ordinal)))
                    {
                        ancestorConflict = true;
                        break;
                    }
                }
                if (ancestorConflict)
                {
                    if (opt.SkipErrors)
                    {
                        continue;
                    }
                    throw new InvalidOperationException($"destination exists, source={source}, destination={destFilePath}");
                }

                var destEntries = index.Entries.Where(e => e.Path.Equals(destFilePath, StringComparison.Ordinal)).ToList();
                if (destEntries.Any(e => e.Stage != 0))
                {
                    if (opt.SkipErrors)
                    {
                        continue;
                    }
                    throw new InvalidOperationException($"conflicted, source={source}, destination={destFilePath}");
                }

                var fullDestFilePath = Path.Combine(WorkingDirectory, destFilePath);
                var destExistsOnDisk = File.Exists(fullDestFilePath) || Directory.Exists(fullDestFilePath);
                var destExistsInIndex = destEntries.Count > 0;
                var isCaseOnlyRename = isCaseInsensitiveFs && normalizedSrc.Equals(destFilePath, StringComparison.OrdinalIgnoreCase);

                if (!isCaseOnlyRename && (destExistsOnDisk || destExistsInIndex) && !opt.Force)
                {
                    if (opt.SkipErrors)
                    {
                        continue;
                    }
                    throw new InvalidOperationException($"destination exists, source={source}, destination={destFilePath}");
                }

                plannedFileMoves.Add(new PlannedMove(normalizedSrc, destFilePath, fileEntries, isCaseOnlyRename));
            }
            else // srcIsDir
            {
                var prefix = normalizedSrc + "/";
                var dirEntries = index.Entries.Where(e => e.Path.StartsWith(prefix, StringComparison.Ordinal)).ToList();
                if (dirEntries.Count == 0)
                {
                    if (opt.SkipErrors)
                    {
                        continue;
                    }
                    throw new InvalidOperationException($"not under version control, source={source}, destination={destinationPath}");
                }

                if (dirEntries.Any(e => e.Stage != 0))
                {
                    if (opt.SkipErrors)
                    {
                        continue;
                    }
                    throw new InvalidOperationException($"conflicted, source={source}, destination={destinationPath}");
                }

                var isCaseOnlyDirRename = isCaseInsensitiveFs &&
                    normalizedSrc.Equals(normalizedDest, StringComparison.OrdinalIgnoreCase) &&
                    !normalizedSrc.Equals(normalizedDest, StringComparison.Ordinal);

                string finalDestDir;
                if (destIsDir && !isCaseOnlyDirRename)
                {
                    var srcDirName = Path.GetFileName(normalizedSrc.TrimEnd('/'));
                    finalDestDir = CombineRelative(normalizedDest, srcDirName);
                }
                else
                {
                    finalDestDir = normalizedDest;
                }

                // Check self-move and moving directory into itself or child
                if (normalizedSrc.Equals(finalDestDir, StringComparison.Ordinal))
                {
                    if (opt.SkipErrors)
                    {
                        continue;
                    }
                    throw new InvalidOperationException($"can not move directory into itself, source={source}, destination={destinationPath}");
                }

                if (finalDestDir.StartsWith(normalizedSrc + "/", StringComparison.Ordinal) ||
                    (isCaseInsensitiveFs && !isCaseOnlyDirRename && finalDestDir.StartsWith(normalizedSrc + "/", StringComparison.OrdinalIgnoreCase)))
                {
                    if (opt.SkipErrors)
                    {
                        continue;
                    }
                    throw new InvalidOperationException($"can not move directory into itself, source={source}, destination={destinationPath}");
                }

                var grouped = dirEntries.GroupBy(e => e.Path, StringComparer.Ordinal);
                var dirPlannedMoves = new List<PlannedMove>();
                var hasErrorInDir = false;

                foreach (var g in grouped)
                {
                    var subPath = g.Key.Substring(prefix.Length);
                    var targetFilePath = CombineRelative(finalDestDir, subPath);

                    try
                    {
                        NormalizeAndValidateRelativePath(g.Key);
                        NormalizeAndValidateRelativePath(targetFilePath);
                    }
                    catch
                    {
                        if (opt.SkipErrors)
                        {
                            hasErrorInDir = true;
                            break;
                        }
                        throw;
                    }

                    // Reject if target has indexed descendants
                    if (index.Entries.Any(e => e.Path.StartsWith(targetFilePath + "/", StringComparison.Ordinal)))
                    {
                        if (opt.SkipErrors)
                        {
                            hasErrorInDir = true;
                            break;
                        }
                        throw new InvalidOperationException($"destination exists, source={g.Key}, destination={targetFilePath}");
                    }

                    // Reject if destination entry has unresolved conflict
                    var targetEntries = index.Entries.Where(e => e.Path.Equals(targetFilePath, StringComparison.Ordinal)).ToList();
                    if (targetEntries.Any(e => e.Stage != 0))
                    {
                        if (opt.SkipErrors)
                        {
                            hasErrorInDir = true;
                            break;
                        }
                        throw new InvalidOperationException($"conflicted, source={g.Key}, destination={targetFilePath}");
                    }

                    var fullTarget = Path.Combine(WorkingDirectory, targetFilePath);
                    var targetExists = File.Exists(fullTarget) || Directory.Exists(fullTarget) || targetEntries.Count > 0;
                    var isCaseOnly = isCaseInsensitiveFs && g.Key.Equals(targetFilePath, StringComparison.OrdinalIgnoreCase);

                    if (!isCaseOnly && targetExists && !opt.Force)
                    {
                        if (opt.SkipErrors)
                        {
                            hasErrorInDir = true;
                            break;
                        }
                        throw new InvalidOperationException($"destination exists, source={g.Key}, destination={targetFilePath}");
                    }

                    dirPlannedMoves.Add(new PlannedMove(g.Key, targetFilePath, g.ToList(), isCaseOnly));
                }

                if (!hasErrorInDir)
                {
                    plannedFileMoves.AddRange(dirPlannedMoves);
                    plannedDirMoves.Add(new PlannedDirMove(normalizedSrc, finalDestDir, isCaseOnlyDirRename));
                }
            }
        }

        // Filter every duplicate-destination group
        var duplicateGroups = plannedFileMoves.GroupBy(p => p.DestinationPath, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .ToList();
        if (duplicateGroups.Count > 0)
        {
            if (!opt.SkipErrors)
            {
                throw new InvalidOperationException($"destination exists, source={duplicateGroups[0].Last().SourcePath}, destination={duplicateGroups[0].Key}");
            }
            foreach (var group in duplicateGroups)
            {
                plannedFileMoves.RemoveAll(p => p.DestinationPath.Equals(group.Key, StringComparison.Ordinal));
            }
        }

        // Preflight destination types: File.Move cannot overwrite a directory with a file
        var rootFull = Path.GetFullPath(WorkingDirectory);
        var invalidPlannedMoves = new HashSet<PlannedMove>();
        foreach (var planned in plannedFileMoves)
        {
            var dstFull = Path.Combine(WorkingDirectory, planned.DestinationPath);
            if (Directory.Exists(dstFull))
            {
                if (!opt.SkipErrors)
                {
                    throw new InvalidOperationException($"destination exists, source={planned.SourcePath}, destination={planned.DestinationPath}");
                }
                invalidPlannedMoves.Add(planned);
                continue;
            }

            var parent = Path.GetDirectoryName(dstFull);
            while (!string.IsNullOrEmpty(parent) && !parent.Equals(rootFull, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(parent))
                {
                    if (!opt.SkipErrors)
                    {
                        throw new InvalidOperationException($"cannot create directory '{parent}' because a file exists with that name");
                    }
                    invalidPlannedMoves.Add(planned);
                    break;
                }
                parent = Path.GetDirectoryName(parent);
            }
        }

        if (invalidPlannedMoves.Count > 0)
        {
            plannedFileMoves.RemoveAll(invalidPlannedMoves.Contains);
        }

        if (opt.DryRun)
        {
            return new GitMoveResult
            {
                MovedItems = plannedFileMoves.Select(p => new GitMoveItem(p.SourcePath, p.DestinationPath)).ToList()
            };
        }

        // Execute filesystem directory moves first
        var completedFilesystemMoves = new List<(string From, string To)>();
        try
        {
            foreach (var dirMove in plannedDirMoves)
            {
                var srcDirFull = Path.Combine(WorkingDirectory, dirMove.SourceDir);
                var dstDirFull = Path.Combine(WorkingDirectory, dirMove.DestinationDir);

                if (Directory.Exists(srcDirFull))
                {
                    if (dirMove.IsCaseOnly && isCaseInsensitiveFs)
                    {
                        var tempDir = srcDirFull + "_git_mv_temp_" + Guid.NewGuid().ToString("N");
                        Directory.Move(srcDirFull, tempDir);
                        Directory.Move(tempDir, dstDirFull);
                        completedFilesystemMoves.Add((srcDirFull, dstDirFull));
                    }
                    else if (!Directory.Exists(dstDirFull))
                    {
                        var parentDst = Path.GetDirectoryName(dstDirFull);
                        if (!string.IsNullOrEmpty(parentDst))
                        {
                            Directory.CreateDirectory(parentDst);
                        }
                        Directory.Move(srcDirFull, dstDirFull);
                        completedFilesystemMoves.Add((srcDirFull, dstDirFull));
                    }
                    else
                    {
                        // Target directory already exists: move all items from src into dst
                        MoveDirectoryContents(srcDirFull, dstDirFull, opt.Force);
                    }
                }
            }

            // Move any remaining individual files (whose parent directory wasn't moved as a whole)
            foreach (var planned in plannedFileMoves)
            {
                var srcFull = Path.Combine(WorkingDirectory, planned.SourcePath);
                var dstFull = Path.Combine(WorkingDirectory, planned.DestinationPath);

                if (File.Exists(srcFull))
                {
                    var dstDir = Path.GetDirectoryName(dstFull);
                    if (!string.IsNullOrEmpty(dstDir))
                    {
                        Directory.CreateDirectory(dstDir);
                    }
                    if (planned.IsCaseOnly && isCaseInsensitiveFs)
                    {
                        var tempFile = srcFull + "_git_mv_temp_" + Guid.NewGuid().ToString("N");
                        File.Move(srcFull, tempFile);
                        File.Move(tempFile, dstFull);
                        completedFilesystemMoves.Add((srcFull, dstFull));
                    }
                    else
                    {
                        File.Move(srcFull, dstFull, overwrite: opt.Force);
                        completedFilesystemMoves.Add((srcFull, dstFull));
                    }
                }
            }
        }
        catch
        {
            // Restore consistency if filesystem moves failed partway through
            for (var i = completedFilesystemMoves.Count - 1; i >= 0; i--)
            {
                var (from, to) = completedFilesystemMoves[i];
                try
                {
                    if (File.Exists(to))
                    {
                        if (isCaseInsensitiveFs && from.Equals(to, StringComparison.OrdinalIgnoreCase) && !from.Equals(to, StringComparison.Ordinal))
                        {
                            var tempFile = to + "_git_mv_temp_" + Guid.NewGuid().ToString("N");
                            File.Move(to, tempFile);
                            File.Move(tempFile, from);
                        }
                        else
                        {
                            File.Move(to, from, overwrite: true);
                        }
                    }
                    else if (Directory.Exists(to) && !Directory.Exists(from))
                    {
                        if (isCaseInsensitiveFs && from.Equals(to, StringComparison.OrdinalIgnoreCase) && !from.Equals(to, StringComparison.Ordinal))
                        {
                            var tempDir = to + "_git_mv_temp_" + Guid.NewGuid().ToString("N");
                            Directory.Move(to, tempDir);
                            Directory.Move(tempDir, from);
                        }
                        else
                        {
                            Directory.Move(to, from);
                        }
                    }
                }
                catch { /* best-effort rollback */ }
            }
            throw;
        }

        // Update index entries
        foreach (var planned in plannedFileMoves)
        {
            var dstFull = Path.Combine(WorkingDirectory, planned.DestinationPath);

            index.Remove(planned.DestinationPath, stage: 0);
            index.Remove(planned.DestinationPath, stage: 1);
            index.Remove(planned.DestinationPath, stage: 2);
            index.Remove(planned.DestinationPath, stage: 3);

            var fileOnDisk = File.Exists(dstFull);
            FileInfo? dstFileInfo = fileOnDisk ? new FileInfo(dstFull) : null;
            GitHash? computedHash = fileOnDisk
                ? await ComputeFileBlobHashAsync(dstFull, cancellationToken).ConfigureAwait(false)
                : null;

            foreach (var entry in planned.Entries)
            {
                index.Remove(planned.SourcePath, stage: entry.Stage);

                GitIndexEntry newEntry;
                if (!fileOnDisk || computedHash != entry.Hash)
                {
                    // File absent or has unstaged modifications: preserve original staged stat cache
                    newEntry = new GitIndexEntry(
                        planned.DestinationPath,
                        entry.Hash,
                        fileMode: entry.FileMode,
                        fileSize: entry.FileSize,
                        mtimeSeconds: entry.MtimeSeconds,
                        mtimeNanoseconds: entry.MtimeNanoseconds,
                        ctimeSeconds: entry.CtimeSeconds,
                        ctimeNanoseconds: entry.CtimeNanoseconds,
                        dev: entry.Dev,
                        ino: entry.Ino,
                        uid: entry.Uid,
                        gid: entry.Gid,
                        flags: 0);
                }
                else
                {
                    // File is clean: refresh stat cache with dstFileInfo
                    newEntry = GitIndexEntry.FromFileInfo(
                        planned.DestinationPath,
                        dstFileInfo!,
                        entry.Hash,
                        stage: entry.Stage,
                        preserveFileMode: entry.FileMode);
                }

                // Preserve upper flags (stage, assume-unchanged) and extended flags
                var pathBytesLen = Encoding.UTF8.GetByteCount(planned.DestinationPath);
                var pathLen = (ushort)Math.Min(pathBytesLen, 0x0FFF);
                var upperFlags = (ushort)(entry.Flags & ~0x0FFF);
                newEntry.Flags = (ushort)(upperFlags | pathLen);
                newEntry.ExtendedFlags = entry.ExtendedFlags;

                index.AddOrUpdate(newEntry);
            }
        }

        // Clean up empty directories
        foreach (var planned in plannedFileMoves)
        {
            var srcDir = Path.GetDirectoryName(Path.Combine(WorkingDirectory, planned.SourcePath));
            while (!string.IsNullOrEmpty(srcDir) && !srcDir.Equals(rootFull, StringComparison.OrdinalIgnoreCase))
            {
                if (Directory.Exists(srcDir) && !Directory.EnumerateFileSystemEntries(srcDir).Any())
                {
                    try
                    {
                        Directory.Delete(srcDir);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        break;
                    }
                }
                else
                {
                    break;
                }
                srcDir = Path.GetDirectoryName(srcDir);
            }
        }

        await index.WriteAsync(IndexPath, _repository.HashLengthBytes, cancellationToken).ConfigureAwait(false);

        return new GitMoveResult
        {
            MovedItems = plannedFileMoves.Select(p => new GitMoveItem(p.SourcePath, p.DestinationPath)).ToList()
        };
    }

    private static void MoveDirectoryContents(string sourceDir, string targetDir, bool force)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            var fileName = Path.GetFileName(file);
            var destFile = Path.Combine(targetDir, fileName);
            File.Move(file, destFile, overwrite: force);
        }
        foreach (var subDir in Directory.EnumerateDirectories(sourceDir))
        {
            var dirName = Path.GetFileName(subDir);
            var destSub = Path.Combine(targetDir, dirName);
            MoveDirectoryContents(subDir, destSub, force);
        }
        if (!Directory.EnumerateFileSystemEntries(sourceDir).Any())
        {
            Directory.Delete(sourceDir);
        }
    }

    internal string NormalizeAndValidateRelativePath(string relativePath)
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

        // Check each path component for symbolic links/reparse points escaping the repository
        var current = workingDirFull;
        for (var i = 0; i < segments.Length; i++)
        {
            current = Path.Combine(current, segments[i]);
            if (Directory.Exists(current) || File.Exists(current))
            {
                var fileInfo = new FileInfo(current);
                if ((fileInfo.Attributes & FileAttributes.ReparsePoint) != 0 || fileInfo.LinkTarget != null)
                {
                    var resolvedTarget = fileInfo.ResolveLinkTarget(returnFinalTarget: true);
                    if (resolvedTarget != null)
                    {
                        var targetFull = resolvedTarget.FullName;
                        var repoRootTrimmed = workingDirFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        if (!targetFull.StartsWith(repoRootTrimmed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                            !targetFull.Equals(repoRootTrimmed, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new ArgumentException($"Path traverses a symlink escaping the working directory: '{relativePath}'", nameof(relativePath));
                        }
                    }
                    else if (i < segments.Length - 1)
                    {
                        throw new ArgumentException($"Path traverses an unresolved symlink directory: '{relativePath}'", nameof(relativePath));
                    }
                }
            }
        }

        return normalized;
    }

    internal Task<IDisposable> AcquireIndexMutationLockAsync(CancellationToken cancellationToken)
        => AcquireIndexMutationLockAsync(null, cancellationToken);

    internal async Task<IDisposable> AcquireIndexMutationLockAsync(string? targetRef, CancellationToken cancellationToken)
    {
        var normalizedRef = targetRef != null
            ? GitReferenceStore.NormalizeReferenceOrHead(targetRef)
            : null;

        var lockPaths = normalizedRef != null
            ? new[] { "index", normalizedRef }
            : new[] { "index" };

        var refLock = await _repository.LockManager.AcquireMultipleReferenceLocksAsync(lockPaths, cancellationToken).ConfigureAwait(false);
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
                    // Use FileOptions.DeleteOnClose so that the OS kernel automatically and atomically
                    // deletes the lock file when the handle is closed, preventing race conditions with
                    // other processes acquiring the lock.
                    lockStream = new FileStream(lockFilePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose);
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

            return new IndexMutationLock(refLock, lockStream);
        }
        catch
        {
            lockStream?.Dispose();
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

    private async Task<Dictionary<string, GitTreeEntry>> GetTreeFilesAsync(string treeIsh, CancellationToken cancellationToken)
    {
        var files = new Dictionary<string, GitTreeEntry>(StringComparer.Ordinal);
        var commit = await _repository.GetCommitAsync(treeIsh, cancellationToken).ConfigureAwait(false);
        await foreach (var item in _repository.EnumerateCommitTreeAsync(commit.Id.ToString(), null, SearchOption.AllDirectories, cancellationToken).ConfigureAwait(false))
        {
            if (item.Entry.Kind == GitTreeEntryKind.Blob)
            {
                files[item.Path] = item.Entry;
            }
        }
        return files;
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

            // Skip directory symlinks and reparse points to avoid escaping the working tree or recursing infinitely
            if ((subDir.Attributes & FileAttributes.ReparsePoint) != 0 || subDir.LinkTarget != null)
            {
                continue;
            }

            // If ignored, skip unless index/HEAD tracks files inside this directory
            if (ignoreMatcher.IsIgnored(dirRelPath, isDirectory: true) &&
                !trackedPrefixes.Contains(dirRelPath))
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
