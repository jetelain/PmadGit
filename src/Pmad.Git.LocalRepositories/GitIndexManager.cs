using System.Collections.Concurrent;
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
        var autocrlf = await _repository.GetConfigAsync("core.autocrlf", cancellationToken: cancellationToken).ConfigureAwait(false);
        bool shouldNormalizeCrlf = string.Equals(autocrlf, "true", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(autocrlf, "input", StringComparison.OrdinalIgnoreCase);

        var expandedPaths = new List<string>();
        foreach (var path in pathsList)
        {
            var fullPath = Path.Combine(WorkingDirectory, path);
            var normalizedPath = path.Replace('\\', '/').Trim('/');
            var dirPrefix = string.IsNullOrEmpty(normalizedPath) ? "" : normalizedPath + "/";

            if (Directory.Exists(fullPath))
            {
                var ignoreMatcher = GitIgnoreMatcher.Load(WorkingDirectory);
                var subFiles = new Dictionary<string, FileInfo>(StringComparer.Ordinal);
                var dummyPrefixes = new HashSet<string>(StringComparer.Ordinal);
                var dummyTracked = new HashSet<string>(StringComparer.Ordinal);
                ScanWorkingDirectory(new DirectoryInfo(fullPath), normalizedPath, ignoreMatcher, dummyPrefixes, dummyTracked, subFiles);

                foreach (var fileRel in subFiles.Keys.OrderBy(k => k, StringComparer.Ordinal))
                {
                    expandedPaths.Add(fileRel);
                }

                foreach (var entry in index.Entries)
                {
                    if (entry.Path.StartsWith(dirPrefix, StringComparison.Ordinal) && !File.Exists(Path.Combine(WorkingDirectory, entry.Path)))
                    {
                        expandedPaths.Add(entry.Path);
                    }
                }
            }
            else if (!File.Exists(fullPath))
            {
                var matchingEntries = index.Entries
                    .Where(e => e.Path.StartsWith(dirPrefix, StringComparison.Ordinal))
                    .Select(e => e.Path)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                if (matchingEntries.Count > 0)
                {
                    expandedPaths.AddRange(matchingEntries);
                }
                else
                {
                    expandedPaths.Add(path);
                }
            }
            else
            {
                expandedPaths.Add(path);
            }
        }

        foreach (var path in expandedPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.Combine(WorkingDirectory, path);

            if (File.Exists(fullPath))
            {
                var fileInfo = new FileInfo(fullPath);
                GitHash blobHash;

                if (shouldNormalizeCrlf && fileInfo.Length <= 50 * 1024 * 1024)
                {
                    var fileBytes = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
                    if (!IsBinary(fileBytes))
                    {
                        fileBytes = NormalizeCrlfToLf(fileBytes);
                    }
                    blobHash = await _repository.ObjectStore.WriteObjectAsync(
                        GitObjectType.Blob,
                        fileBytes,
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
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

            var expandedPaths = new List<string>();
            foreach (var path in pathsList)
            {
                var dirPrefix = path.EndsWith('/') ? path : path + "/";
                var matchingFromIndex = index.Entries.Where(e => e.Path.StartsWith(dirPrefix, StringComparison.Ordinal)).Select(e => e.Path);
                var matchingFromSource = sourceFiles.Keys.Where(k => k.StartsWith(dirPrefix, StringComparison.Ordinal));
                var allMatching = matchingFromIndex.Concat(matchingFromSource).Distinct(StringComparer.Ordinal).ToList();
                if (allMatching.Count > 0)
                {
                    expandedPaths.AddRange(allMatching);
                }
                else
                {
                    expandedPaths.Add(path);
                }
            }

            foreach (var path in expandedPaths)
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

    private abstract class PlannedOp
    {
        public abstract IReadOnlyList<PlannedMove> FileMoves { get; }
    }

    private sealed class PlannedFileOp(PlannedMove move) : PlannedOp
    {
        public PlannedMove Move { get; } = move;
        public override IReadOnlyList<PlannedMove> FileMoves => [Move];
    }

    private sealed class PlannedDirOp(string sourceDir, string destinationDir, bool isCaseOnly, List<PlannedMove> moves) : PlannedOp
    {
        public string SourceDir { get; } = sourceDir;
        public string DestinationDir { get; } = destinationDir;
        public bool IsCaseOnly { get; } = isCaseOnly;
        public List<PlannedMove> Moves { get; } = moves;
        public override IReadOnlyList<PlannedMove> FileMoves => Moves;
    }

    private interface IJournalAction
    {
        void Rollback();
        void Commit();
    }

    private sealed class FilesystemJournal : IDisposable
    {
        private readonly List<IJournalAction> _actions = new();
        private bool _committed;

        public void Record(IJournalAction action) => _actions.Add(action);

        public void MoveFile(string source, string destination, bool force)
        {
            var action = new MoveFileAction(source, destination, force);
            action.Execute();
            Record(action);
        }

        public void MoveCaseOnlyFile(string source, string destination)
        {
            var action = new MoveCaseOnlyFileAction(source, destination);
            action.Execute();
            Record(action);
        }

        public void MoveDirectory(string source, string destination)
        {
            var action = new MoveDirectoryAction(source, destination);
            action.Execute();
            Record(action);
        }

        public void MoveCaseOnlyDirectory(string source, string destination)
        {
            var action = new MoveCaseOnlyDirectoryAction(source, destination);
            action.Execute();
            Record(action);
        }

        public void MoveDirectoryLink(string source, string destination, bool force)
        {
            var action = new MoveDirectoryLinkAction(source, destination, force);
            action.Execute();
            Record(action);
        }

        public void EnsureDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                var action = new EnsureDirectoryAction(path);
                action.Execute();
                Record(action);
            }
        }

        public void DeleteEmptyDirectory(string path)
        {
            var action = new DeleteEmptyDirectoryAction(path);
            action.Execute();
            Record(action);
        }

        public void Rollback()
        {
            if (_committed) return;
            for (var i = _actions.Count - 1; i >= 0; i--)
            {
                try
                {
                    _actions[i].Rollback();
                }
                catch { /* best-effort rollback */ }
            }
        }

        public void Commit()
        {
            _committed = true;
            foreach (var action in _actions)
            {
                try
                {
                    action.Commit();
                }
                catch { /* best-effort commit */ }
            }
        }

        public void Dispose()
        {
            if (!_committed)
            {
                Rollback();
            }
        }
    }

    private sealed class MoveFileAction(string source, string destination, bool force) : IJournalAction
    {
        private string? _backupPath;

        public void Execute()
        {
            if (File.Exists(destination))
            {
                if (!force)
                {
                    throw new IOException($"Destination file '{destination}' already exists.");
                }
                _backupPath = destination + "_git_mv_bak_" + Guid.NewGuid().ToString("N");
                File.Move(destination, _backupPath);
            }
            File.Move(source, destination);
        }

        public void Rollback()
        {
            if (File.Exists(destination))
            {
                File.Move(destination, source, overwrite: true);
            }
            if (_backupPath != null && File.Exists(_backupPath))
            {
                File.Move(_backupPath, destination, overwrite: true);
            }
        }

        public void Commit()
        {
            if (_backupPath != null && File.Exists(_backupPath))
            {
                try { File.Delete(_backupPath); } catch { }
            }
        }
    }

    private sealed class MoveCaseOnlyFileAction(string source, string destination) : IJournalAction
    {
        private string? _tempPath;

        public void Execute()
        {
            _tempPath = source + "_git_mv_temp_" + Guid.NewGuid().ToString("N");
            File.Move(source, _tempPath);
            File.Move(_tempPath, destination);
        }

        public void Rollback()
        {
            if (_tempPath != null)
            {
                if (File.Exists(destination))
                {
                    File.Move(destination, _tempPath);
                    File.Move(_tempPath, source);
                }
                else if (File.Exists(_tempPath))
                {
                    File.Move(_tempPath, source);
                }
            }
        }

        public void Commit() { }
    }

    private sealed class MoveDirectoryAction(string source, string destination) : IJournalAction
    {
        public void Execute()
        {
            Directory.Move(source, destination);
        }

        public void Rollback()
        {
            if (Directory.Exists(destination) && !Directory.Exists(source))
            {
                Directory.Move(destination, source);
            }
        }

        public void Commit() { }
    }

    private sealed class MoveCaseOnlyDirectoryAction(string source, string destination) : IJournalAction
    {
        private string? _tempPath;

        public void Execute()
        {
            _tempPath = source + "_git_mv_temp_" + Guid.NewGuid().ToString("N");
            Directory.Move(source, _tempPath);
            Directory.Move(_tempPath, destination);
        }

        public void Rollback()
        {
            if (_tempPath != null)
            {
                if (Directory.Exists(destination))
                {
                    Directory.Move(destination, _tempPath);
                    Directory.Move(_tempPath, source);
                }
                else if (Directory.Exists(_tempPath))
                {
                    Directory.Move(_tempPath, source);
                }
            }
        }

        public void Commit() { }
    }

    private sealed class MoveDirectoryLinkAction(string source, string destination, bool force) : IJournalAction
    {
        private string? _backupPath;

        public void Execute()
        {
            if (Directory.Exists(destination) || File.Exists(destination))
            {
                if (!force)
                {
                    throw new IOException($"Destination '{destination}' already exists.");
                }
                _backupPath = destination + "_git_mv_bak_" + Guid.NewGuid().ToString("N");
                if (Directory.Exists(destination))
                {
                    Directory.Move(destination, _backupPath);
                }
                else
                {
                    File.Move(destination, _backupPath);
                }
            }
            Directory.Move(source, destination);
        }

        public void Rollback()
        {
            if (Directory.Exists(destination))
            {
                Directory.Move(destination, source);
            }
            if (_backupPath != null)
            {
                if (Directory.Exists(_backupPath))
                {
                    Directory.Move(_backupPath, destination);
                }
                else if (File.Exists(_backupPath))
                {
                    File.Move(_backupPath, destination);
                }
            }
        }

        public void Commit()
        {
            if (_backupPath != null)
            {
                if (Directory.Exists(_backupPath))
                {
                    try { Directory.Delete(_backupPath, recursive: true); } catch { }
                }
                else if (File.Exists(_backupPath))
                {
                    try { File.Delete(_backupPath); } catch { }
                }
            }
        }
    }

    private sealed class EnsureDirectoryAction(string path) : IJournalAction
    {
        private bool _created;

        public void Execute()
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
                _created = true;
            }
        }

        public void Rollback()
        {
            if (_created && Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                try { Directory.Delete(path); } catch { }
            }
        }

        public void Commit() { }
    }

    private sealed class DeleteEmptyDirectoryAction(string path) : IJournalAction
    {
        public void Execute()
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }

        public void Rollback()
        {
            if (!Directory.Exists(path))
            {
                try { Directory.CreateDirectory(path); } catch { }
            }
        }

        public void Commit() { }
    }

    private static readonly ConcurrentDictionary<string, bool> _caseSensitivityCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, bool> _caseSensitivityOverrides = new(StringComparer.Ordinal);

    internal static void SetFileSystemCaseSensitivityForTest(string directory, bool? isCaseInsensitive)
    {
        var normalized = Path.GetFullPath(directory);
        if (isCaseInsensitive.HasValue)
        {
            _caseSensitivityOverrides[normalized] = isCaseInsensitive.Value;
        }
        else
        {
            _caseSensitivityOverrides.TryRemove(normalized, out _);
        }
    }

    /// <summary>
    /// Determines whether the filesystem hosting the specified directory is case-insensitive.
    /// </summary>
    /// <param name="directory">Directory path to probe.</param>
    /// <returns><see langword="true"/> if the filesystem is case-insensitive; otherwise, <see langword="false"/>.</returns>
    public static bool IsFileSystemCaseInsensitive(string directory)
    {
        var normalized = Path.GetFullPath(directory);
        if (_caseSensitivityOverrides.TryGetValue(normalized, out var overridden))
        {
            return overridden;
        }

        return _caseSensitivityCache.GetOrAdd(normalized, dir =>
        {
            try
            {
                var probeDir = Directory.Exists(Path.Combine(dir, ".git"))
                    ? Path.Combine(dir, ".git")
                    : dir;

                if (Directory.Exists(probeDir))
                {
                    var probeName = "case_probe_" + Guid.NewGuid().ToString("N").ToLowerInvariant();
                    var probePath = Path.Combine(probeDir, probeName);
                    File.WriteAllText(probePath, "");
                    try
                    {
                        return File.Exists(Path.Combine(probeDir, probeName.ToUpperInvariant()));
                    }
                    finally
                    {
                        try { File.Delete(probePath); } catch { }
                    }
                }
            }
            catch
            {
                // Fallback if probe fails
            }
            return OperatingSystem.IsWindows();
        });
    }

    /// <summary>
    /// Determines whether two filesystem paths identify the same entry on disk.
    /// </summary>
    /// <param name="full1">First full path.</param>
    /// <param name="full2">Second full path.</param>
    /// <param name="workingDir">Repository working directory used to evaluate filesystem case-sensitivity.</param>
    /// <returns><see langword="true"/> if both paths identify the same entry; otherwise, <see langword="false"/>.</returns>
    public static bool AreSameFileSystemEntry(string full1, string full2, string workingDir)
    {
        if (string.Equals(full1, full2, StringComparison.Ordinal))
        {
            return true;
        }
        if (!string.Equals(full1, full2, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (!IsFileSystemCaseInsensitive(workingDir))
        {
            return false;
        }
        var p1 = Path.GetFullPath(full1);
        var p2 = Path.GetFullPath(full2);
        if (File.Exists(p1) && File.Exists(p2))
        {
            return true;
        }
        if (Directory.Exists(p1) && Directory.Exists(p2))
        {
            return true;
        }
        if (!File.Exists(p1) && !File.Exists(p2) && !Directory.Exists(p1) && !Directory.Exists(p2))
        {
            return true;
        }
        return false;
    }

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
        var isCaseInsensitiveFs = IsFileSystemCaseInsensitive(WorkingDirectory);
        var pathComparer = isCaseInsensitiveFs ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var pathComparison = isCaseInsensitiveFs ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
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

        // Normalize source paths and check for overlapping or duplicate sources
        var normalizedSources = new List<(string Original, string Normalized)>();
        foreach (var source in sourcesList)
        {
            string norm;
            try
            {
                norm = NormalizeAndValidateRelativePath(source);
            }
            catch
            {
                if (opt.SkipErrors)
                {
                    continue;
                }
                throw;
            }
            normalizedSources.Add((source, norm));
        }

        var skippedSources = new HashSet<string>(pathComparer);
        for (var i = 0; i < normalizedSources.Count; i++)
        {
            for (var j = i + 1; j < normalizedSources.Count; j++)
            {
                var s1 = normalizedSources[i].Normalized;
                var s2 = normalizedSources[j].Normalized;
                if (s1.Equals(s2, pathComparison) ||
                    s2.StartsWith(s1 + "/", pathComparison) ||
                    s1.StartsWith(s2 + "/", pathComparison))
                {
                    if (!opt.SkipErrors)
                    {
                        throw new InvalidOperationException($"cannot move '{normalizedSources[j].Original}': overlapping source");
                    }
                    skippedSources.Add(normalizedSources[j].Normalized);
                    skippedSources.Add(normalizedSources[i].Normalized);
                }
            }
        }

        var operations = new List<PlannedOp>();

        foreach (var (origSource, normalizedSrc) in normalizedSources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (skippedSources.Contains(normalizedSrc))
            {
                continue;
            }

            var fullSrc = Path.Combine(WorkingDirectory, normalizedSrc);
            var exactEntry = index.Entries.FirstOrDefault(e => e.Path.Equals(normalizedSrc, StringComparison.Ordinal));
            FileSystemInfo? fullSrcInfo = Directory.Exists(fullSrc) ? new DirectoryInfo(fullSrc) : (File.Exists(fullSrc) ? new FileInfo(fullSrc) : null);
            var isSymlinkOnDisk = fullSrcInfo != null && ((fullSrcInfo.Attributes & FileAttributes.ReparsePoint) != 0 || fullSrcInfo.LinkTarget != null);

            bool srcIsFile;

            if (exactEntry != null)
            {
                // Tracked leaf entry in index (file or symlink)
                srcIsFile = true;
            }
            else if (isSymlinkOnDisk && !Directory.Exists(fullSrc))
            {
                srcIsFile = true;
            }
            else if (File.Exists(fullSrc))
            {
                srcIsFile = true;
            }
            else if (Directory.Exists(fullSrc))
            {
                srcIsFile = false;
            }
            else if (index.Entries.Any(e => e.Path.Equals(normalizedSrc, StringComparison.Ordinal)))
            {
                srcIsFile = true;
            }
            else if (index.Entries.Any(e => e.Path.StartsWith(normalizedSrc + "/", StringComparison.Ordinal)))
            {
                srcIsFile = false;
            }
            else
            {
                if (opt.SkipErrors)
                {
                    continue;
                }
                throw new FileNotFoundException($"bad source, source={origSource}, destination={destinationPath}");
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
                    throw new InvalidOperationException($"not under version control, source={origSource}, destination={destinationPath}");
                }

                if (fileEntries.Any(e => e.Stage != 0))
                {
                    if (opt.SkipErrors)
                    {
                        continue;
                    }
                    throw new InvalidOperationException($"conflicted, source={origSource}, destination={destinationPath}");
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
                    throw new InvalidOperationException($"destination exists, source={origSource}, destination={destinationPath}");
                }

                // Reject if target has indexed descendants (file/directory conflict in index)
                if (index.Entries.Any(e => e.Path.StartsWith(destFilePath + "/", StringComparison.Ordinal)))
                {
                    if (opt.SkipErrors)
                    {
                        continue;
                    }
                    throw new InvalidOperationException($"destination exists, source={origSource}, destination={destFilePath}");
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
                    throw new InvalidOperationException($"destination exists, source={origSource}, destination={destFilePath}");
                }

                var destEntries = index.Entries.Where(e => e.Path.Equals(destFilePath, StringComparison.Ordinal)).ToList();
                if (destEntries.Any(e => e.Stage != 0))
                {
                    if (opt.SkipErrors)
                    {
                        continue;
                    }
                    throw new InvalidOperationException($"conflicted, source={origSource}, destination={destFilePath}");
                }

                var fullDestFilePath = Path.Combine(WorkingDirectory, destFilePath);
                var destExistsOnDisk = File.Exists(fullDestFilePath) || Directory.Exists(fullDestFilePath);
                var destExistsInIndex = destEntries.Count > 0;
                var isCaseOnlyRename = isCaseInsensitiveFs &&
                    normalizedSrc.Equals(destFilePath, StringComparison.OrdinalIgnoreCase) &&
                    !normalizedSrc.Equals(destFilePath, StringComparison.Ordinal) &&
                    AreSameFileSystemEntry(fullSrc, fullDestFilePath, WorkingDirectory);

                if (!isCaseOnlyRename && (destExistsOnDisk || destExistsInIndex) && !opt.Force)
                {
                    if (opt.SkipErrors)
                    {
                        continue;
                    }
                    throw new InvalidOperationException($"destination exists, source={origSource}, destination={destFilePath}");
                }

                operations.Add(new PlannedFileOp(new PlannedMove(normalizedSrc, destFilePath, fileEntries, isCaseOnlyRename)));
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
                    throw new InvalidOperationException($"not under version control, source={origSource}, destination={destinationPath}");
                }

                if (dirEntries.Any(e => e.Stage != 0))
                {
                    if (opt.SkipErrors)
                    {
                        continue;
                    }
                    throw new InvalidOperationException($"conflicted, source={origSource}, destination={destinationPath}");
                }

                var fullDestDir = Path.Combine(WorkingDirectory, normalizedDest);
                var isCaseOnlyDirRename = isCaseInsensitiveFs &&
                    normalizedSrc.Equals(normalizedDest, StringComparison.OrdinalIgnoreCase) &&
                    !normalizedSrc.Equals(normalizedDest, StringComparison.Ordinal) &&
                    AreSameFileSystemEntry(fullSrc, fullDestDir, WorkingDirectory);

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
                    throw new InvalidOperationException($"can not move directory into itself, source={origSource}, destination={destinationPath}");
                }

                if (finalDestDir.StartsWith(normalizedSrc + "/", StringComparison.Ordinal) ||
                    (isCaseInsensitiveFs && !isCaseOnlyDirRename && finalDestDir.StartsWith(normalizedSrc + "/", StringComparison.OrdinalIgnoreCase)))
                {
                    if (opt.SkipErrors)
                    {
                        continue;
                    }
                    throw new InvalidOperationException($"can not move directory into itself, source={origSource}, destination={destinationPath}");
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

                    // Reject if any ancestor of target is an indexed file
                    var segs = targetFilePath.Split('/');
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
                    var isCaseOnly = isCaseInsensitiveFs &&
                        g.Key.Equals(targetFilePath, StringComparison.OrdinalIgnoreCase) &&
                        !g.Key.Equals(targetFilePath, StringComparison.Ordinal) &&
                        AreSameFileSystemEntry(Path.Combine(WorkingDirectory, g.Key), fullTarget, WorkingDirectory);

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
                    operations.Add(new PlannedDirOp(normalizedSrc, finalDestDir, isCaseOnlyDirRename, dirPlannedMoves));
                }
            }
        }

        // Filter every duplicate-destination group across all operations
        var allPlannedMoves = operations.SelectMany(op => op.FileMoves).ToList();
        var duplicateDestGroups = allPlannedMoves.GroupBy(p => p.DestinationPath, pathComparer)
            .Where(g => g.Count() > 1)
            .ToList();
        if (duplicateDestGroups.Count > 0)
        {
            if (!opt.SkipErrors)
            {
                throw new InvalidOperationException($"destination exists, source={duplicateDestGroups[0].Last().SourcePath}, destination={duplicateDestGroups[0].Key}");
            }
            var duplicateDestKeys = new HashSet<string>(duplicateDestGroups.Select(g => g.Key), pathComparer);
            operations.RemoveAll(op => op.FileMoves.Any(m => duplicateDestKeys.Contains(m.DestinationPath)));
        }

        // Filter every duplicate-source group across all operations
        var duplicateSourceGroups = operations.SelectMany(op => op.FileMoves)
            .GroupBy(p => p.SourcePath, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .ToList();
        if (duplicateSourceGroups.Count > 0)
        {
            if (!opt.SkipErrors)
            {
                throw new InvalidOperationException($"cannot move '{duplicateSourceGroups[0].Key}': multiple move destinations specified");
            }
            var duplicateSourceKeys = new HashSet<string>(duplicateSourceGroups.Select(g => g.Key), StringComparer.Ordinal);
            operations.RemoveAll(op => op.FileMoves.Any(m => duplicateSourceKeys.Contains(m.SourcePath)));
        }

        // Preflight destination types: File.Move cannot overwrite a directory with a file
        var rootFull = Path.GetFullPath(WorkingDirectory);
        var invalidOperations = new HashSet<PlannedOp>();
        foreach (var op in operations)
        {
            if (op is PlannedDirOp dirOp)
            {
                var dstDirFull = Path.Combine(WorkingDirectory, dirOp.DestinationDir);
                var current = dstDirFull;
                while (!string.IsNullOrEmpty(current) && !current.Equals(rootFull, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(current))
                    {
                        if (!opt.SkipErrors)
                        {
                            throw new InvalidOperationException($"cannot create directory '{current}' because a file exists with that name");
                        }
                        invalidOperations.Add(op);
                        break;
                    }
                    current = Path.GetDirectoryName(current);
                }
            }

            foreach (var planned in op.FileMoves)
            {
                var dstFull = Path.Combine(WorkingDirectory, planned.DestinationPath);
                if (Directory.Exists(dstFull))
                {
                    if (!opt.SkipErrors)
                    {
                        throw new InvalidOperationException($"destination exists, source={planned.SourcePath}, destination={planned.DestinationPath}");
                    }
                    invalidOperations.Add(op);
                    break;
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
                        invalidOperations.Add(op);
                        break;
                    }
                    parent = Path.GetDirectoryName(parent);
                }
            }
        }

        if (invalidOperations.Count > 0)
        {
            operations.RemoveAll(invalidOperations.Contains);
        }

        // Preflight directory merges, link validation, and collection of overwritten files
        var overwrittenFilesList = new List<string>();
        foreach (var op in operations)
        {
            if (op is PlannedDirOp dirOp)
            {
                var srcDirFull = Path.Combine(WorkingDirectory, dirOp.SourceDir);
                var dstDirFull = Path.Combine(WorkingDirectory, dirOp.DestinationDir);
                if (Directory.Exists(srcDirFull) && Directory.Exists(dstDirFull) && !dirOp.IsCaseOnly)
                {
                    ValidateDirectoryMerge(srcDirFull, dstDirFull, opt.Force, rootFull, overwrittenFilesList);
                }
            }
            foreach (var move in op.FileMoves)
            {
                var dstFull = Path.Combine(WorkingDirectory, move.DestinationPath);
                if (!move.IsCaseOnly && (File.Exists(dstFull) || Directory.Exists(dstFull) || index.Entries.Any(e => e.Path.Equals(move.DestinationPath, StringComparison.Ordinal))))
                {
                    overwrittenFilesList.Add(move.DestinationPath);
                }
            }
        }

        var finalPlannedFileMoves = operations.SelectMany(op => op.FileMoves).ToList();

        if (opt.DryRun)
        {
            return new GitMoveResult
            {
                MovedItems = finalPlannedFileMoves.Select(p => new GitMoveItem(p.SourcePath, p.DestinationPath)).ToList(),
                OverwrittenFiles = overwrittenFilesList.Distinct(pathComparer).ToList()
            };
        }

        // Execute filesystem directory and file moves with journaling rollback
        using var journal = new FilesystemJournal();
        try
        {
            foreach (var op in operations)
            {
                if (op is PlannedDirOp dirOp)
                {
                    var srcDirFull = Path.Combine(WorkingDirectory, dirOp.SourceDir);
                    var dstDirFull = Path.Combine(WorkingDirectory, dirOp.DestinationDir);

                    if (Directory.Exists(srcDirFull))
                    {
                        if (dirOp.IsCaseOnly && isCaseInsensitiveFs)
                        {
                            journal.MoveCaseOnlyDirectory(srcDirFull, dstDirFull);
                        }
                        else if (!Directory.Exists(dstDirFull))
                        {
                            var parentDst = Path.GetDirectoryName(dstDirFull);
                            if (!string.IsNullOrEmpty(parentDst))
                            {
                                journal.EnsureDirectory(parentDst);
                            }
                            journal.MoveDirectory(srcDirFull, dstDirFull);
                        }
                        else
                        {
                            // Target directory already exists: merge contents with link safety
                            MoveDirectoryContents(journal, srcDirFull, dstDirFull, opt.Force, rootFull);
                        }
                    }
                }
                else if (op is PlannedFileOp fileOp)
                {
                    var srcFull = Path.Combine(WorkingDirectory, fileOp.Move.SourcePath);
                    var dstFull = Path.Combine(WorkingDirectory, fileOp.Move.DestinationPath);
                    var srcIsSymlinkDir = Directory.Exists(srcFull) &&
                        ((File.GetAttributes(srcFull) & FileAttributes.ReparsePoint) != 0 || new DirectoryInfo(srcFull).LinkTarget != null);

                    var dstDir = Path.GetDirectoryName(dstFull);
                    if (!string.IsNullOrEmpty(dstDir))
                    {
                        journal.EnsureDirectory(dstDir);
                    }

                    if (srcIsSymlinkDir)
                    {
                        journal.MoveDirectoryLink(srcFull, dstFull, opt.Force);
                    }
                    else if (File.Exists(srcFull))
                    {
                        if (fileOp.Move.IsCaseOnly && isCaseInsensitiveFs)
                        {
                            journal.MoveCaseOnlyFile(srcFull, dstFull);
                        }
                        else
                        {
                            journal.MoveFile(srcFull, dstFull, opt.Force);
                        }
                    }
                }
            }

            // Update index entries
            foreach (var planned in finalPlannedFileMoves)
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
                        // File absent, symlink, or has unstaged modifications: preserve original staged stat cache
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
            foreach (var planned in finalPlannedFileMoves)
            {
                var srcDir = Path.GetDirectoryName(Path.Combine(WorkingDirectory, planned.SourcePath));
                while (!string.IsNullOrEmpty(srcDir) && !srcDir.Equals(rootFull, StringComparison.OrdinalIgnoreCase))
                {
                    if (Directory.Exists(srcDir) && !Directory.EnumerateFileSystemEntries(srcDir).Any())
                    {
                        try
                        {
                            journal.DeleteEmptyDirectory(srcDir);
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
            journal.Commit();

            return new GitMoveResult
            {
                MovedItems = finalPlannedFileMoves.Select(p => new GitMoveItem(p.SourcePath, p.DestinationPath)).ToList(),
                OverwrittenFiles = overwrittenFilesList.Distinct(pathComparer).ToList()
            };
        }
        catch
        {
            journal.Rollback();
            throw;
        }
    }

    private static void ValidateDirectoryMerge(
        string sourceDir,
        string targetDir,
        bool force,
        string workingDirFull,
        List<string> overwrittenFiles)
    {
        var targetFull = Path.GetFullPath(targetDir);
        if (!targetFull.StartsWith(workingDirFull, StringComparison.OrdinalIgnoreCase) &&
            !targetFull.Equals(workingDirFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Destination directory escapes working directory: '{targetDir}'");
        }

        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            var fileName = Path.GetFileName(file);
            var destFile = Path.Combine(targetDir, fileName);
            var destFileFull = Path.GetFullPath(destFile);
            if (!destFileFull.StartsWith(workingDirFull, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Destination file escapes working directory: '{destFile}'");
            }

            if (File.Exists(destFile))
            {
                var relPath = Path.GetRelativePath(workingDirFull, destFileFull).Replace('\\', '/');
                if (!force)
                {
                    throw new InvalidOperationException($"destination exists, source={Path.GetRelativePath(workingDirFull, file).Replace('\\', '/')}, destination={relPath}");
                }
                overwrittenFiles.Add(relPath);
            }
        }

        foreach (var subDir in Directory.EnumerateDirectories(sourceDir))
        {
            var dirName = Path.GetFileName(subDir);
            var destSub = Path.Combine(targetDir, dirName);
            var destSubFull = Path.GetFullPath(destSub);
            if (!destSubFull.StartsWith(workingDirFull, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Destination directory escapes working directory: '{destSub}'");
            }

            var subDirInfo = new DirectoryInfo(subDir);
            var isLink = (subDirInfo.Attributes & FileAttributes.ReparsePoint) != 0 || subDirInfo.LinkTarget != null;

            if (isLink)
            {
                // Subdirectory link: do not traverse! Check destination exists
                if (Directory.Exists(destSub) || File.Exists(destSub))
                {
                    var relPath = Path.GetRelativePath(workingDirFull, destSubFull).Replace('\\', '/');
                    if (!force)
                    {
                        throw new InvalidOperationException($"destination exists, source={Path.GetRelativePath(workingDirFull, subDir).Replace('\\', '/')}, destination={relPath}");
                    }
                    overwrittenFiles.Add(relPath);
                }
            }
            else
            {
                if (Directory.Exists(destSub))
                {
                    var destSubInfo = new DirectoryInfo(destSub);
                    var destIsLink = (destSubInfo.Attributes & FileAttributes.ReparsePoint) != 0 || destSubInfo.LinkTarget != null;
                    if (destIsLink)
                    {
                        var relPath = Path.GetRelativePath(workingDirFull, destSubFull).Replace('\\', '/');
                        if (!force)
                        {
                            throw new InvalidOperationException($"destination exists, source={Path.GetRelativePath(workingDirFull, subDir).Replace('\\', '/')}, destination={relPath}");
                        }
                        overwrittenFiles.Add(relPath);
                    }
                    else
                    {
                        ValidateDirectoryMerge(subDir, destSub, force, workingDirFull, overwrittenFiles);
                    }
                }
            }
        }
    }

    private static void MoveDirectoryContents(
        FilesystemJournal journal,
        string sourceDir,
        string targetDir,
        bool force,
        string workingDirFull)
    {
        journal.EnsureDirectory(targetDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            var fileName = Path.GetFileName(file);
            var destFile = Path.Combine(targetDir, fileName);
            journal.MoveFile(file, destFile, force);
        }

        foreach (var subDir in Directory.EnumerateDirectories(sourceDir))
        {
            var dirName = Path.GetFileName(subDir);
            var destSub = Path.Combine(targetDir, dirName);

            var subDirInfo = new DirectoryInfo(subDir);
            var isLink = (subDirInfo.Attributes & FileAttributes.ReparsePoint) != 0 || subDirInfo.LinkTarget != null;

            if (isLink)
            {
                // Move the directory link itself without traversing target
                journal.MoveDirectoryLink(subDir, destSub, force);
            }
            else
            {
                if (Directory.Exists(destSub))
                {
                    var destSubInfo = new DirectoryInfo(destSub);
                    var destIsLink = (destSubInfo.Attributes & FileAttributes.ReparsePoint) != 0 || destSubInfo.LinkTarget != null;
                    if (destIsLink)
                    {
                        journal.MoveDirectoryLink(subDir, destSub, force);
                    }
                    else
                    {
                        MoveDirectoryContents(journal, subDir, destSub, force, workingDirFull);
                    }
                }
                else
                {
                    journal.MoveDirectory(subDir, destSub);
                }
            }
        }

        if (!Directory.EnumerateFileSystemEntries(sourceDir).Any())
        {
            journal.DeleteEmptyDirectory(sourceDir);
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
        var autocrlf = await _repository.GetConfigAsync("core.autocrlf", cancellationToken: cancellationToken).ConfigureAwait(false);
        bool shouldNormalizeCrlf = string.Equals(autocrlf, "true", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(autocrlf, "input", StringComparison.OrdinalIgnoreCase);

        if (shouldNormalizeCrlf && fileInfo.Length <= 50 * 1024 * 1024)
        {
            var fileBytes = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
            if (!IsBinary(fileBytes))
            {
                fileBytes = NormalizeCrlfToLf(fileBytes);
            }
            var algo = GitHashHelper.GetAlgorithmName(_repository.HashLengthBytes);
            using var algoHash = IncrementalHash.CreateHash(algo);
            var hdr = Encoding.ASCII.GetBytes($"blob {fileBytes.Length}\0");
            algoHash.AppendData(hdr);
            algoHash.AppendData(fileBytes);
            return GitHash.FromBytes(algoHash.GetHashAndReset());
        }

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

    private static bool IsBinary(ReadOnlySpan<byte> data)
    {
        var checkLength = Math.Min(data.Length, 8000);
        return data.Slice(0, checkLength).IndexOf((byte)0) >= 0;
    }

    private static byte[] NormalizeCrlfToLf(byte[] bytes)
    {
        int crlfIndex = -1;
        for (int i = 0; i < bytes.Length - 1; i++)
        {
            if (bytes[i] == (byte)'\r' && bytes[i + 1] == (byte)'\n')
            {
                crlfIndex = i;
                break;
            }
        }
        if (crlfIndex == -1)
        {
            return bytes;
        }

        var result = new byte[bytes.Length];
        int destIndex = 0;
        for (int i = 0; i < bytes.Length; i++)
        {
            if (i < bytes.Length - 1 && bytes[i] == (byte)'\r' && bytes[i + 1] == (byte)'\n')
            {
                continue;
            }
            result[destIndex++] = bytes[i];
        }
        Array.Resize(ref result, destIndex);
        return result;
    }
}
