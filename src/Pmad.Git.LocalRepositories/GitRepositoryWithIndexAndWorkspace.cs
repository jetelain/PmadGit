using System.IO;
using System.Text;

namespace Pmad.Git.LocalRepositories;

/// <summary>
/// High-level Git repository implementation with active working tree and index (.git/index) management.
/// Integrates object database operations, staging, committing, amending, resetting, and undo/reverting in 100% managed C#.
/// </summary>
public sealed class GitRepositoryWithIndexAndWorkspace : IGitWorkspaceRepository, IGitRepositoryCacheInvalidator
{
    private readonly GitRepository _repo;
    private readonly GitIndexManager _indexManager;

    /// <summary>
    /// Initializes a new instance of <see cref="GitRepositoryWithIndexAndWorkspace"/>.
    /// </summary>
    /// <param name="repository">The underlying non-bare repository.</param>
    /// <param name="indexManager">Optional custom index manager.</param>
    public GitRepositoryWithIndexAndWorkspace(GitRepository repository, GitIndexManager? indexManager = null)
    {
        _repo = repository ?? throw new ArgumentNullException(nameof(repository));
        if (_repo.IsBare)
        {
            throw new ArgumentException("Cannot create a workspace repository on a bare git repository.", nameof(repository));
        }

        _indexManager = indexManager ?? new GitIndexManager(repository, repository.RootPath);
    }

    /// <summary>
    /// Opens an existing git repository with working tree at the specified path.
    /// </summary>
    /// <param name="path">Path to the repository root or .git directory.</param>
    /// <param name="lockManager">Optional shared lock manager.</param>
    /// <returns>An initialized <see cref="GitRepositoryWithIndexAndWorkspace"/>.</returns>
    public static GitRepositoryWithIndexAndWorkspace Open(string path, IGitRepositoryLockManager? lockManager = null)
    {
        var repo = GitRepository.Open(path, lockManager);
        return new GitRepositoryWithIndexAndWorkspace(repo);
    }

    /// <summary>
    /// Creates a new git repository with working tree at the specified path.
    /// </summary>
    /// <param name="path">Path where the repository should be initialized.</param>
    /// <param name="initialBranch">Name of the initial branch; defaults to "main".</param>
    /// <param name="lockManager">Optional shared lock manager.</param>
    /// <returns>An initialized <see cref="GitRepositoryWithIndexAndWorkspace"/>.</returns>
    public static GitRepositoryWithIndexAndWorkspace Init(string path, string initialBranch = "main", IGitRepositoryLockManager? lockManager = null)
    {
        var repo = GitRepository.Init(path, bare: false, initialBranch: initialBranch, lockManager: lockManager);
        return new GitRepositoryWithIndexAndWorkspace(repo);
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    #region IGitRepository Forwarding

    /// <inheritdoc />
    public string RootPath => _repo.RootPath;

    /// <inheritdoc />
    public string GitDirectory => _repo.GitDirectory;

    /// <inheritdoc />
    public int HashLengthBytes => _repo.HashLengthBytes;

    /// <inheritdoc />
    public bool IsBare => false;

    /// <inheritdoc />
    public GitIndexManager IndexManager => _indexManager;

    GitIndexManager? IGitRepository.IndexManager => _indexManager;

    /// <inheritdoc />
    public IGitObjectStore ObjectStore => _repo.ObjectStore;

    /// <inheritdoc />
    public IGitReferenceStore ReferenceStore => _repo.ReferenceStore;

    /// <inheritdoc />
    public IGitRepositoryLockManager LockManager => _repo.LockManager;

    /// <inheritdoc />
    public event EventHandler? Changed
    {
        add => _repo.Changed += value;
        remove => _repo.Changed -= value;
    }

    /// <inheritdoc />
    public void InvalidateCaches(bool clearAllData = false, bool raiseChanged = true) =>
        _repo.InvalidateCaches(clearAllData, raiseChanged);

    /// <inheritdoc />
    public Task<GitCommit> GetCommitAsync(string? reference = null, CancellationToken cancellationToken = default) =>
        _repo.GetCommitAsync(reference, cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<GitCommit> EnumerateCommitsAsync(string? reference = null, CancellationToken cancellationToken = default) =>
        _repo.EnumerateCommitsAsync(reference, cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<GitTreeItem> EnumerateCommitTreeAsync(string? reference = null, string? path = null, SearchOption searchOption = SearchOption.AllDirectories, CancellationToken cancellationToken = default) =>
        _repo.EnumerateCommitTreeAsync(reference, path, searchOption, cancellationToken);

    /// <inheritdoc />
    public Task<GitTreeEntryKind?> GetPathTypeAsync(string path, string? reference = null, CancellationToken cancellationToken = default) =>
        _repo.GetPathTypeAsync(path, reference, cancellationToken);

    /// <inheritdoc />
    public Task<bool> PathExistsAsync(string path, string? reference = null, CancellationToken cancellationToken = default) =>
        _repo.PathExistsAsync(path, reference, cancellationToken);

    /// <inheritdoc />
    public Task<bool> FileExistsAsync(string filePath, string? reference = null, CancellationToken cancellationToken = default) =>
        _repo.FileExistsAsync(filePath, reference, cancellationToken);

    /// <inheritdoc />
    public Task<bool> DirectoryExistsAsync(string directoryPath, string? reference = null, CancellationToken cancellationToken = default) =>
        _repo.DirectoryExistsAsync(directoryPath, reference, cancellationToken);

    /// <inheritdoc />
    public Task<byte[]> ReadFileAsync(string filePath, string? reference = null, CancellationToken cancellationToken = default) =>
        _repo.ReadFileAsync(filePath, reference, cancellationToken);

    /// <inheritdoc />
    public Task<GitFileContentAndHash> ReadFileAndHashAsync(string filePath, string? reference = null, CancellationToken cancellationToken = default) =>
        _repo.ReadFileAndHashAsync(filePath, reference, cancellationToken);

    /// <inheritdoc />
    public Task<GitObjectStream> ReadFileStreamAsync(string filePath, string? reference = null, CancellationToken cancellationToken = default) =>
        _repo.ReadFileStreamAsync(filePath, reference, cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<GitCommit> EnumerateFileHistoryAsync(string filePath, string? reference = null, CancellationToken cancellationToken = default) =>
        _repo.EnumerateFileHistoryAsync(filePath, reference, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<GitFileLastChange>> GetFilesWithLastChangeAsync(string? reference = null, string? path = null, SearchOption searchOption = SearchOption.AllDirectories, Func<string, bool>? predicate = null, CancellationToken cancellationToken = default) =>
        _repo.GetFilesWithLastChangeAsync(reference, path, searchOption, predicate, cancellationToken);

    /// <inheritdoc />
    public Task<bool> IsCommitReachableAsync(GitHash from, GitHash to, CancellationToken cancellationToken = default) =>
        _repo.IsCommitReachableAsync(from, to, cancellationToken);

    /// <summary>
    /// Resolves the HEAD reference to the target commit hash.
    /// </summary>
    public Task<GitHash> ResolveHeadAsync(CancellationToken cancellationToken = default) =>
        _repo.ReferenceStore.ResolveHeadAsync(cancellationToken);

    /// <inheritdoc />
    public Task<string?> GetCurrentBranchNameAsync(CancellationToken cancellationToken = default) =>
        _repo.GetCurrentBranchNameAsync(cancellationToken);

    /// <inheritdoc />
    public Task<bool> IsHeadDetachedAsync(CancellationToken cancellationToken = default) =>
        _repo.IsHeadDetachedAsync(cancellationToken);

    /// <inheritdoc />
    public Task CreateReferenceAsync(string referencePath, GitHash targetCommit, bool overwrite = false, CancellationToken cancellationToken = default) =>
        _repo.CreateReferenceAsync(referencePath, targetCommit, overwrite, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, GitHash>> GetReferencesByPrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
        _repo.GetReferencesByPrefixAsync(prefix, cancellationToken);

    /// <inheritdoc />
    public Task DeleteReferenceAsync(string referencePath, CancellationToken cancellationToken = default) =>
        _repo.DeleteReferenceAsync(referencePath, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetBranchesAsync(bool includeRemote = false, CancellationToken cancellationToken = default) =>
        _repo.GetBranchesAsync(includeRemote, cancellationToken);

    /// <inheritdoc />
    public Task CreateBranchAsync(string branchName, string? startPoint = null, CancellationToken cancellationToken = default) =>
        _repo.CreateBranchAsync(branchName, startPoint, cancellationToken);

    /// <inheritdoc />
    public Task RenameBranchAsync(string oldName, string newName, CancellationToken cancellationToken = default) =>
        _repo.RenameBranchAsync(oldName, newName, cancellationToken);

    /// <inheritdoc />
    public Task DeleteBranchAsync(string branchName, bool force = false, CancellationToken cancellationToken = default) =>
        _repo.DeleteBranchAsync(branchName, force, cancellationToken);

    /// <inheritdoc />
    public Task<GitTrackingStatus> GetTrackingStatusAsync(string? branch = null, CancellationToken cancellationToken = default) =>
        _repo.GetTrackingStatusAsync(branch, cancellationToken);

    /// <inheritdoc />
    public Task<bool> IsCommitPushedAsync(GitHash commitHash, string? remoteBranch = null, CancellationToken cancellationToken = default) =>
        _repo.IsCommitPushedAsync(commitHash, remoteBranch, cancellationToken);

    /// <inheritdoc />
    public Task<string?> GetConfigAsync(string key, bool global = false, CancellationToken cancellationToken = default) =>
        _repo.GetConfigAsync(key, global, cancellationToken);

    /// <inheritdoc />
    public Task SetConfigAsync(string key, string value, bool global = false, CancellationToken cancellationToken = default) =>
        _repo.SetConfigAsync(key, value, global, cancellationToken);

    /// <inheritdoc />
    public Task UnsetConfigAsync(string key, bool global = false, CancellationToken cancellationToken = default) =>
        _repo.UnsetConfigAsync(key, global, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<GitTreeChange>> CompareTreesAsync(GitHash oldTreeHash, GitHash newTreeHash, CancellationToken cancellationToken = default) =>
        _repo.CompareTreesAsync(oldTreeHash, newTreeHash, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<GitTreeChange>> GetCommitChangesAsync(GitHash commitHash, CancellationToken cancellationToken = default) =>
        _repo.GetCommitChangesAsync(commitHash, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<GitTreeChange>> GetCommitChangesAsync(string? reference = null, CancellationToken cancellationToken = default) =>
        _repo.GetCommitChangesAsync(reference, cancellationToken);

    /// <inheritdoc />
    public async Task<GitHash> CreateCommitAsync(
        string branchName,
        IEnumerable<GitCommitOperation> operations,
        GitCommitMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var commitHash = await _repo.CreateCommitAsync(branchName, operations, metadata, cancellationToken).ConfigureAwait(false);
        if (await IsCurrentBranchAsync(branchName, cancellationToken).ConfigureAwait(false))
        {
            var commit = await GetCommitAsync(commitHash.Value, cancellationToken).ConfigureAwait(false);
            await SyncWorkspaceToCommitAsync(commit, cancellationToken).ConfigureAwait(false);
        }
        return commitHash;
    }

    /// <inheritdoc />
    public async Task<GitHash> AmendCommitAsync(
        string branchName,
        IEnumerable<GitCommitOperation> operations,
        GitCommitMetadata? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var commitHash = await _repo.AmendCommitAsync(branchName, operations, metadata, cancellationToken).ConfigureAwait(false);
        if (await IsCurrentBranchAsync(branchName, cancellationToken).ConfigureAwait(false))
        {
            var commit = await GetCommitAsync(commitHash.Value, cancellationToken).ConfigureAwait(false);
            await SyncWorkspaceToCommitAsync(commit, cancellationToken).ConfigureAwait(false);
        }
        return commitHash;
    }

    /// <inheritdoc />
    public Task<GitHash> SquashCommitsAsync(string branchName, GitHash baseCommitHash, GitCommitMetadata metadata, CancellationToken cancellationToken = default) =>
        _repo.SquashCommitsAsync(branchName, baseCommitHash, metadata, cancellationToken);

    /// <inheritdoc />
    public Task<GitHash> WriteTreeAsync(GitIndex index, CancellationToken cancellationToken = default) =>
        _repo.WriteTreeAsync(index, cancellationToken);

    #endregion

    #region Workspace & Staging Operations

    /// <inheritdoc />
    public Task<GitStatusResult> GetStatusAsync(bool includeUntracked = true, bool includeClean = false, CancellationToken cancellationToken = default) =>
        _indexManager.GetStatusAsync(includeUntracked, includeClean, cancellationToken);

    /// <inheritdoc />
    public Task<bool> IsWorkingTreeCleanAsync(CancellationToken cancellationToken = default) =>
        _indexManager.IsWorkingTreeCleanAsync(cancellationToken);

    /// <inheritdoc />
    public Task StageAsync(string relativePath, CancellationToken cancellationToken = default) =>
        _indexManager.StageAsync(relativePath, cancellationToken);

    /// <inheritdoc />
    public Task StageAsync(IEnumerable<string> relativePaths, CancellationToken cancellationToken = default) =>
        _indexManager.StageAsync(relativePaths, cancellationToken);

    /// <inheritdoc />
    public Task StageAllAsync(CancellationToken cancellationToken = default) =>
        _indexManager.StageAllAsync(cancellationToken);

    /// <inheritdoc />
    public Task UnstageAsync(string relativePath, CancellationToken cancellationToken = default) =>
        _indexManager.UnstageAsync(relativePath, cancellationToken);

    /// <inheritdoc />
    public Task UnstageAsync(IEnumerable<string> relativePaths, CancellationToken cancellationToken = default) =>
        _indexManager.UnstageAsync(relativePaths, cancellationToken);

    /// <inheritdoc />
    public Task UnstageAllAsync(CancellationToken cancellationToken = default) =>
        _indexManager.UnstageAllAsync(cancellationToken);

    /// <inheritdoc />
    public Task RestoreFileAsync(string relativePath, CancellationToken cancellationToken = default) =>
        _indexManager.RestoreFileAsync(relativePath, cancellationToken);

    /// <inheritdoc />
    public Task RestoreAllAsync(bool removeUntracked = false, CancellationToken cancellationToken = default) =>
        _indexManager.RestoreAllAsync(removeUntracked, cancellationToken);

    #endregion

    #region Commit, Amend, Reset, Squash, and Revert

    /// <inheritdoc />
    public async Task<GitHash> CommitAsync(
        string message,
        GitCommitMetadata? metadata = null,
        bool stageAll = false,
        CancellationToken cancellationToken = default)
    {
        var targetRef = await GetTargetReferenceAsync(cancellationToken).ConfigureAwait(false);
        using (await _indexManager.AcquireIndexMutationLockAsync(targetRef, cancellationToken).ConfigureAwait(false))
        {
            if (stageAll)
            {
                await _indexManager.StageAllCoreAsync(cancellationToken).ConfigureAwait(false);
            }

            var index = await GitIndex.ReadAsync(_indexManager.IndexPath, HashLengthBytes, cancellationToken).ConfigureAwait(false);
            if (index.Entries.Any(e => e.Stage > 0))
            {
                throw new InvalidOperationException("Cannot commit with unmerged (conflicted) entries in the index.");
            }

            var treeHash = await _repo.WriteTreeAsync(index, cancellationToken).ConfigureAwait(false);

            var headHash = await ReferenceStore.TryResolveReferenceAsync("HEAD", cancellationToken).ConfigureAwait(false);
            var parents = headHash.HasValue ? new[] { headHash.Value } : Array.Empty<GitHash>();

            var commitMetadata = metadata ?? GetDefaultMetadata(message);
            var payload = GitRepository.BuildCommitPayload(treeHash, parents, commitMetadata);
            var commitHash = await ObjectStore.WriteObjectAsync(GitObjectType.Commit, payload, cancellationToken).ConfigureAwait(false);

            await UpdateHeadOrBranchAsync(commitHash, targetRef, cancellationToken).ConfigureAwait(false);

            // Update index stat cache for committed files
            UpdateIndexStatCache(index);
            await index.WriteAsync(_indexManager.IndexPath, HashLengthBytes, cancellationToken).ConfigureAwait(false);

            InvalidateCaches();
            return commitHash;
        }
    }

    /// <inheritdoc />
    public async Task<GitHash> CommitAmendAsync(
        string? message = null,
        GitCommitMetadata? metadata = null,
        bool stageAll = false,
        CancellationToken cancellationToken = default)
    {
        var targetRef = await GetTargetReferenceAsync(cancellationToken).ConfigureAwait(false);
        using (await _indexManager.AcquireIndexMutationLockAsync(targetRef, cancellationToken).ConfigureAwait(false))
        {
            if (stageAll)
            {
                await _indexManager.StageAllCoreAsync(cancellationToken).ConfigureAwait(false);
            }

            var headHash = await ReferenceStore.ResolveHeadAsync(cancellationToken).ConfigureAwait(false);
            var headCommit = await GetCommitAsync(headHash.Value, cancellationToken).ConfigureAwait(false);

            var index = await GitIndex.ReadAsync(_indexManager.IndexPath, HashLengthBytes, cancellationToken).ConfigureAwait(false);
            if (index.Entries.Any(e => e.Stage > 0))
            {
                throw new InvalidOperationException("Cannot amend commit with unmerged (conflicted) entries in the index.");
            }

            var treeHash = await _repo.WriteTreeAsync(index, cancellationToken).ConfigureAwait(false);

            var commitMessage = (message ?? metadata?.Message ?? headCommit.Message).TrimEnd('\r', '\n');
            var commitMetadata = metadata ?? new GitCommitMetadata(
                commitMessage,
                headCommit.Metadata.Author,
                new GitCommitSignature(headCommit.Metadata.Committer.Name, headCommit.Metadata.Committer.Email, DateTimeOffset.UtcNow));

            var payload = GitRepository.BuildCommitPayload(treeHash, headCommit.Parents, commitMetadata);
            var commitHash = await ObjectStore.WriteObjectAsync(GitObjectType.Commit, payload, cancellationToken).ConfigureAwait(false);

            await UpdateHeadOrBranchAsync(commitHash, targetRef, cancellationToken).ConfigureAwait(false);

            UpdateIndexStatCache(index);
            await index.WriteAsync(_indexManager.IndexPath, HashLengthBytes, cancellationToken).ConfigureAwait(false);

            InvalidateCaches();
            return commitHash;
        }
    }

    /// <inheritdoc />
    public async Task ResetAsync(
        GitHash targetCommitHash,
        GitResetMode mode = GitResetMode.Mixed,
        CancellationToken cancellationToken = default)
    {
        var targetRef = await GetTargetReferenceAsync(cancellationToken).ConfigureAwait(false);
        using (await _indexManager.AcquireIndexMutationLockAsync(targetRef, cancellationToken).ConfigureAwait(false))
        {
            var targetCommit = await GetCommitAsync(targetCommitHash.Value, cancellationToken).ConfigureAwait(false);

            await UpdateHeadOrBranchAsync(targetCommitHash, targetRef, cancellationToken).ConfigureAwait(false);
            InvalidateCaches();

            if (mode == GitResetMode.Soft)
            {
                return;
            }

            if (mode == GitResetMode.Hard)
            {
                await SyncWorkspaceToCommitAsync(targetCommit, cancellationToken).ConfigureAwait(false);
            }
            else // Mixed
            {
                var newIndex = new GitIndex();
                await foreach (var item in EnumerateCommitTreeAsync(targetCommit.Id.Value, null, SearchOption.AllDirectories, cancellationToken).ConfigureAwait(false))
                {
                    if (item.Entry.Kind == GitTreeEntryKind.Blob)
                    {
                        var entry = new GitIndexEntry(item.Path, item.Entry.Hash, item.Entry.Mode);
                        newIndex.AddOrUpdate(entry);
                    }
                }
                await newIndex.WriteAsync(_indexManager.IndexPath, HashLengthBytes, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public async Task<GitHash> SquashRangeAsync(
        GitHash baseCommitHash,
        string message,
        GitCommitMetadata? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var currentBranch = await ReferenceStore.GetCurrentBranchNameAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Cannot squash on detached HEAD.");
        var branchRef = $"refs/heads/{currentBranch}";

        using (await _indexManager.AcquireIndexMutationLockAsync(branchRef, cancellationToken).ConfigureAwait(false))
        {
            var headHash = await ReferenceStore.ResolveHeadAsync(cancellationToken).ConfigureAwait(false);
            if (headHash.Equals(baseCommitHash))
            {
                throw new InvalidOperationException("Cannot squash commits: branch HEAD is already at base commit.");
            }

            var isReachable = await IsCommitReachableAsync(headHash, baseCommitHash, cancellationToken).ConfigureAwait(false);
            if (!isReachable)
            {
                throw new ArgumentException($"Base commit '{baseCommitHash.Value}' is not an ancestor of branch HEAD '{headHash.Value}'.", nameof(baseCommitHash));
            }

            var headCommit = await GetCommitAsync(headHash.Value, cancellationToken).ConfigureAwait(false);

            var finalMetadata = metadata ?? GetDefaultMetadata(message, headCommit.Metadata);
            var payload = GitRepository.BuildCommitPayload(headCommit.Tree, new[] { baseCommitHash }, finalMetadata);
            var squashedHash = await ObjectStore.WriteObjectAsync(GitObjectType.Commit, payload, cancellationToken).ConfigureAwait(false);

            if (_repo.ReferenceStore is GitReferenceStore localRefStore)
            {
                await localRefStore.WriteReferenceWithoutLockAsync(branchRef, squashedHash, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await ReferenceStore.CreateReferenceAsync(branchRef, squashedHash, overwrite: true, cancellationToken).ConfigureAwait(false);
            }
            InvalidateCaches();
            return squashedHash;
        }
    }

    /// <inheritdoc />
    public async Task<GitHash> RevertAsync(
        GitHash commitHash,
        GitCommitMetadata? metadata = null,
        CancellationToken cancellationToken = default)
    {
        if (!await _indexManager.IsWorkingTreeCleanAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Cannot revert commit because the working tree or index has uncommitted changes.");
        }

        var commitToRevert = await GetCommitAsync(commitHash.Value, cancellationToken).ConfigureAwait(false);
        if (commitToRevert.Parents.Count == 0)
        {
            throw new InvalidOperationException("Cannot revert a root commit with no parents.");
        }

        var parentFiles = new Dictionary<string, GitTreeEntry>(StringComparer.Ordinal);
        await foreach (var item in EnumerateCommitTreeAsync(commitToRevert.Parents[0].Value, null, SearchOption.AllDirectories, cancellationToken).ConfigureAwait(false))
        {
            if (item.Entry.Kind == GitTreeEntryKind.Blob)
            {
                parentFiles[item.Path] = item.Entry;
            }
        }

        var changes = await GetCommitChangesAsync(commitToRevert.Id, cancellationToken).ConfigureAwait(false);
        var index = await GitIndex.ReadAsync(_indexManager.IndexPath, HashLengthBytes, cancellationToken).ConfigureAwait(false);

        foreach (var change in changes)
        {
            var fullPath = Path.Combine(RootPath, change.Path);
            switch (change.Kind)
            {
                case GitChangeKind.Added:
                    if (File.Exists(fullPath))
                    {
                        File.Delete(fullPath);
                    }
                    index.Remove(change.Path);
                    break;

                case GitChangeKind.Modified:
                case GitChangeKind.Deleted:
                    if (!parentFiles.TryGetValue(change.Path, out var parentEntry))
                    {
                        break;
                    }

                    var dir = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    if (File.Exists(fullPath))
                    {
                        File.SetAttributes(fullPath, FileAttributes.Normal);
                    }

                    await using (var objStream = await ObjectStore.ReadObjectStreamAsync(parentEntry.Hash, cancellationToken).ConfigureAwait(false))
                    {
                        var options = new FileStreamOptions
                        {
                            Mode = FileMode.Create,
                            Access = FileAccess.Write,
                            Share = FileShare.None,
                            Options = FileOptions.Asynchronous
                        };
                        await using var fileStream = new FileStream(fullPath, options);
                        await objStream.Content.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
                    }

                    if (!OperatingSystem.IsWindows())
                    {
                        try
                        {
                            var currentUnixMode = File.GetUnixFileMode(fullPath);
                            if (parentEntry.Mode == 33261)
                            {
                                File.SetUnixFileMode(fullPath, currentUnixMode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
                            }
                            else if (parentEntry.Mode == 33188)
                            {
                                File.SetUnixFileMode(fullPath, currentUnixMode & ~(UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute));
                            }
                        }
                        catch
                        {
                        }
                    }

                    var fileInfo = new FileInfo(fullPath);
                    var entry = GitIndexEntry.FromFileInfo(change.Path, fileInfo, parentEntry.Hash);
                    entry.FileMode = parentEntry.Mode;
                    index.AddOrUpdate(entry);
                    break;
            }
        }

        await index.WriteAsync(_indexManager.IndexPath, HashLengthBytes, cancellationToken).ConfigureAwait(false);

        var revertMessage = metadata?.Message ?? $"Revert \"{commitToRevert.Message.Trim()}\"";
        return await CommitAsync(revertMessage, metadata, stageAll: false, cancellationToken).ConfigureAwait(false);
    }


    #endregion

    #region Helpers

    private async Task SyncWorkspaceToCommitAsync(GitCommit targetCommit, CancellationToken cancellationToken)
    {
        var oldIndex = await GitIndex.ReadAsync(_indexManager.IndexPath, HashLengthBytes, cancellationToken).ConfigureAwait(false);
        var targetFiles = new HashSet<string>(StringComparer.Ordinal);
        var newIndex = new GitIndex();

        await foreach (var item in EnumerateCommitTreeAsync(targetCommit.Id.Value, null, SearchOption.AllDirectories, cancellationToken).ConfigureAwait(false))
        {
            if (item.Entry.Kind == GitTreeEntryKind.Blob)
            {
                targetFiles.Add(item.Path);
                var fullPath = Path.Combine(RootPath, item.Path);
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                if (File.Exists(fullPath))
                {
                    File.SetAttributes(fullPath, FileAttributes.Normal);
                }

                var fileStreamOptions = new FileStreamOptions
                {
                    Mode = FileMode.Create,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.Asynchronous
                };
                await using (var objStream = await ObjectStore.ReadObjectStreamAsync(item.Entry.Hash, cancellationToken).ConfigureAwait(false))
                await using (var fileStream = new FileStream(fullPath, fileStreamOptions))
                {
                    await objStream.Content.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
                }

                if (!OperatingSystem.IsWindows())
                {
                    try
                    {
                        var currentUnixMode = File.GetUnixFileMode(fullPath);
                        if (item.Entry.Mode == 33261)
                        {
                            File.SetUnixFileMode(fullPath, currentUnixMode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
                        }
                        else if (item.Entry.Mode == 33188)
                        {
                            File.SetUnixFileMode(fullPath, currentUnixMode & ~(UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute));
                        }
                    }
                    catch
                    {
                    }
                }

                var fileInfo = new FileInfo(fullPath);
                var entry = GitIndexEntry.FromFileInfo(item.Path, fileInfo, item.Entry.Hash);
                entry.FileMode = item.Entry.Mode;
                newIndex.AddOrUpdate(entry);
            }
        }

        foreach (var oldEntry in oldIndex.Entries)
        {
            if (!targetFiles.Contains(oldEntry.Path))
            {
                var fullPath = Path.Combine(RootPath, oldEntry.Path);
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }
            }
        }

        await newIndex.WriteAsync(_indexManager.IndexPath, HashLengthBytes, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> IsCurrentBranchAsync(string branchName, CancellationToken cancellationToken)
    {
        var currentBranch = await ReferenceStore.GetCurrentBranchNameAsync(cancellationToken).ConfigureAwait(false);
        if (currentBranch == null)
        {
            return false;
        }

        var normalized = branchName.Trim().Replace('\\', '/');
        if (normalized.StartsWith("refs/heads/", StringComparison.Ordinal))
        {
            normalized = normalized[11..];
        }

        return string.Equals(currentBranch, normalized, StringComparison.Ordinal);
    }

    private async Task<string> GetTargetReferenceAsync(CancellationToken cancellationToken)
    {
        if (await ReferenceStore.IsHeadDetachedAsync(cancellationToken).ConfigureAwait(false))
        {
            return "HEAD";
        }
        var currentBranch = await ReferenceStore.GetCurrentBranchNameAsync(cancellationToken).ConfigureAwait(false);
        return currentBranch != null ? $"refs/heads/{currentBranch}" : await GetHeadTargetRefAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task UpdateHeadOrBranchAsync(GitHash commitHash, string targetRef, CancellationToken cancellationToken)
    {
        if (_repo.ReferenceStore is GitReferenceStore localRefStore)
        {
            await localRefStore.WriteReferenceWithoutLockAsync(targetRef, commitHash, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            if (string.Equals(targetRef, "HEAD", StringComparison.OrdinalIgnoreCase))
            {
                var headPath = Path.Combine(GitDirectory, "HEAD");
                var tempPath = Path.Combine(GitDirectory, $"HEAD.{Guid.NewGuid():N}.tmp");
                await File.WriteAllTextAsync(tempPath, commitHash.ToString() + "\n", cancellationToken).ConfigureAwait(false);
                File.Move(tempPath, headPath, overwrite: true);
            }
            else
            {
                await ReferenceStore.CreateReferenceAsync(targetRef, commitHash, overwrite: true, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void UpdateIndexStatCache(GitIndex index)
    {
        for (var i = 0; i < index.Entries.Count; i++)
        {
            var entry = index.Entries[i];
            var fullPath = Path.Combine(RootPath, entry.Path);
            if (File.Exists(fullPath))
            {
                var fileInfo = new FileInfo(fullPath);
                var updated = GitIndexEntry.FromFileInfo(entry.Path, fileInfo, entry.Hash, entry.Stage);
                updated.FileMode = entry.FileMode;
                index.Entries[i] = updated;
            }
        }
    }

    private async Task<string> GetHeadTargetRefAsync(CancellationToken cancellationToken)
    {
        var headPath = Path.Combine(GitDirectory, "HEAD");
        if (File.Exists(headPath))
        {
            var content = (await File.ReadAllTextAsync(headPath, cancellationToken).ConfigureAwait(false)).Trim();
            if (content.StartsWith("ref: ", StringComparison.Ordinal))
            {
                return content[5..].Trim();
            }
        }
        return "refs/heads/main";
    }

    private GitCommitMetadata GetDefaultMetadata(string message, GitCommitMetadata? template = null)
    {
        if (template is not null)
        {
            return new GitCommitMetadata(
                message,
                template.Author,
                new GitCommitSignature(template.Committer.Name, template.Committer.Email, DateTimeOffset.UtcNow));
        }

        var configPath = Path.Combine(GitDirectory, "config");
        var name = ReadConfigValue(configPath, "user", "name") ?? Environment.UserName;
        var email = ReadConfigValue(configPath, "user", "email") ?? $"{Environment.UserName.ToLowerInvariant()}@{Environment.MachineName.ToLowerInvariant()}";

        var signature = new GitCommitSignature(name, email, DateTimeOffset.UtcNow);
        return new GitCommitMetadata(message, signature);
    }

    private static string? ReadConfigValue(string configPath, string section, string key)
    {
        if (!File.Exists(configPath))
        {
            return null;
        }

        string? currentSection = null;
        foreach (var rawLine in File.ReadLines(configPath))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#') || line.StartsWith(';'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line[1..^1].Trim();
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator < 0)
            {
                continue;
            }

            var currentKey = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (string.Equals(currentSection, section, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(currentKey, key, StringComparison.OrdinalIgnoreCase))
            {
                return value.Trim('"');
            }
        }

        return null;
    }

    #endregion
}
