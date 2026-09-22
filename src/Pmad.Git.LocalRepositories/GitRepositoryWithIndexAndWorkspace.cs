using System.IO;
using System.Text;
using Pmad.Git.LocalRepositories.Diff;

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

    /// <inheritdoc />
    public Task<GitHash?> FindMergeBaseAsync(GitHash commit1, GitHash commit2, CancellationToken cancellationToken = default) =>
        _repo.FindMergeBaseAsync(commit1, commit2, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<GitHash>> FindMergeBasesAsync(GitHash commit1, GitHash commit2, CancellationToken cancellationToken = default) =>
        _repo.FindMergeBasesAsync(commit1, commit2, cancellationToken);

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

    /// <inheritdoc />
    public Task<string> GetDiffAsync(string? fromCommit = null, string? toCommit = null, string? path = null, CancellationToken cancellationToken = default) =>
        _repo.GetDiffAsync(fromCommit, toCommit, path, cancellationToken);

    /// <inheritdoc />
    public Task<string> GetCommitDiffAsync(string commitIsh, string? path = null, CancellationToken cancellationToken = default) =>
        _repo.GetCommitDiffAsync(commitIsh, path, cancellationToken);

    /// <inheritdoc />
    public Task<GitDiffStat> GetCommitStatAsync(string commitIsh, CancellationToken cancellationToken = default) =>
        _repo.GetCommitStatAsync(commitIsh, cancellationToken);

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

    /// <inheritdoc />
    public async Task<string> GetUnstagedDiffAsync(string? path = null, CancellationToken cancellationToken = default)
    {
        var index = await GitIndex.ReadAsync(_indexManager.IndexPath, HashLengthBytes, cancellationToken).ConfigureAwait(false);
        var entries = index.Entries.Where(e => e.Stage == 0)
            .OrderBy(e => e.Path, StringComparer.Ordinal)
            .ToList();

        var sb = new StringBuilder();

        foreach (var entry in entries)
        {
            if (!GitRepository.MatchesPathFilter(entry.Path, path))
            {
                continue;
            }

            var fullPath = Path.Combine(RootPath, entry.Path.Replace('/', Path.DirectorySeparatorChar));

            if (entry.FileMode == GitRepository.SubmoduleMode)
            {
                if (!Directory.Exists(fullPath))
                {
                    // Deleted submodule
                    var oldContent = Encoding.UTF8.GetBytes($"Subproject commit {entry.Hash.Value}\n");
                    var (diffText, _, _) = UnifiedDiffFormatter.FormatFileDiff(
                        oldPath: entry.Path,
                        newPath: null,
                        oldHash: entry.Hash,
                        newHash: null,
                        oldContent: oldContent,
                        newContent: null,
                        oldMode: GitRepository.FormatFileMode(entry.FileMode),
                        newMode: null);
                    sb.Append(diffText);
                }
                else
                {
                    var workingHead = await TryGetSubmoduleHeadAsync(fullPath, cancellationToken).ConfigureAwait(false);
                    if (workingHead.HasValue && !workingHead.Value.Equals(entry.Hash))
                    {
                        var oldContent = Encoding.UTF8.GetBytes($"Subproject commit {entry.Hash.Value}\n");
                        var newContent = Encoding.UTF8.GetBytes($"Subproject commit {workingHead.Value.Value}\n");
                        var (diffText, _, _) = UnifiedDiffFormatter.FormatFileDiff(
                            oldPath: entry.Path,
                            newPath: entry.Path,
                            oldHash: entry.Hash,
                            newHash: workingHead.Value,
                            oldContent: oldContent,
                            newContent: newContent,
                            oldMode: GitRepository.FormatFileMode(entry.FileMode),
                            newMode: GitRepository.FormatFileMode(entry.FileMode));
                        sb.Append(diffText);
                    }
                }
                continue;
            }

            if (!File.Exists(fullPath))
            {
                // Deleted in working tree
                var oldBlob = await _repo.ObjectStore.ReadObjectAsync(entry.Hash, cancellationToken).ConfigureAwait(false);
                var (diffText, _, _) = UnifiedDiffFormatter.FormatFileDiff(
                    oldPath: entry.Path,
                    newPath: null,
                    oldHash: entry.Hash,
                    newHash: null,
                    oldContent: oldBlob.Content,
                    newContent: null,
                    oldMode: GitRepository.FormatFileMode(entry.FileMode),
                    newMode: null);

                sb.Append(diffText);
            }
            else
            {
                var fileInfo = new FileInfo(fullPath);
                var workingBytes = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
                var workingHash = GitHashHelper.ComputeBlobHash(workingBytes, HashLengthBytes);
                int currentMode = GitIndexEntry.GetFileMode(fileInfo);

                if (workingHash == entry.Hash && currentMode == entry.FileMode)
                {
                    continue;
                }

                var oldBlob = await _repo.ObjectStore.ReadObjectAsync(entry.Hash, cancellationToken).ConfigureAwait(false);
                var (diffText, _, _) = UnifiedDiffFormatter.FormatFileDiff(
                    oldPath: entry.Path,
                    newPath: entry.Path,
                    oldHash: entry.Hash,
                    newHash: workingHash,
                    oldContent: oldBlob.Content,
                    newContent: workingBytes,
                    oldMode: GitRepository.FormatFileMode(entry.FileMode),
                    newMode: GitRepository.FormatFileMode(currentMode));

                sb.Append(diffText);
            }
        }

        return sb.ToString();
    }

    /// <inheritdoc />
    public async Task<string> GetStagedDiffAsync(string? path = null, CancellationToken cancellationToken = default)
    {
        var index = await GitIndex.ReadAsync(_indexManager.IndexPath, HashLengthBytes, cancellationToken).ConfigureAwait(false);
        var indexLeaves = index.Entries.Where(e => e.Stage == 0)
            .ToDictionary(e => e.Path, e => new TreeLeaf(e.FileMode, e.Hash), StringComparer.Ordinal);

        Dictionary<string, TreeLeaf> headLeaves;
        try
        {
            var headCommit = await _repo.GetCommitAsync(null, cancellationToken).ConfigureAwait(false);
            headLeaves = await _repo.LoadLeafEntriesAsync(headCommit.Tree, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Initial commit does not exist yet; diff against empty tree
            headLeaves = new Dictionary<string, TreeLeaf>(StringComparer.Ordinal);
        }

        var (diffText, _) = await _repo.ComputeLeavesDiffAsync(headLeaves, indexLeaves, path, cancellationToken).ConfigureAwait(false);
        return diffText;
    }

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

    #region Merge and Conflict Resolution

    /// <inheritdoc />
    public Task<bool> IsMergeInProgressAsync(CancellationToken cancellationToken = default)
    {
        var mergeHeadPath = Path.Combine(GitDirectory, "MERGE_HEAD");
        return Task.FromResult(File.Exists(mergeHeadPath));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetConflictedFilesAsync(CancellationToken cancellationToken = default)
    {
        var index = await GitIndex.ReadAsync(_indexManager.IndexPath, HashLengthBytes, cancellationToken).ConfigureAwait(false);
        return index.Entries.Where(e => e.Stage > 0)
            .Select(e => e.Path)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc />
    public Task ResolveConflictAsync(string relativeFilePath, CancellationToken cancellationToken = default)
    {
        return StageAsync(relativeFilePath, cancellationToken);
    }

    /// <inheritdoc />
    public async Task AbortMergeAsync(CancellationToken cancellationToken = default)
    {
        var targetRef = await GetTargetReferenceAsync(cancellationToken).ConfigureAwait(false);
        using (await _indexManager.AcquireIndexMutationLockAsync(targetRef, cancellationToken).ConfigureAwait(false))
        {
            if (!await IsMergeInProgressAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("No merge in progress to abort.");
            }

            RemoveMergeStateFiles();

            var headHash = await ReferenceStore.ResolveHeadAsync(cancellationToken).ConfigureAwait(false);
            var headCommit = await GetCommitAsync(headHash.Value, cancellationToken).ConfigureAwait(false);
            await SyncWorkspaceToCommitAsync(headCommit, cancellationToken).ConfigureAwait(false);
            InvalidateCaches();
        }
    }

    /// <inheritdoc />
    public async Task<GitHash> ContinueMergeAsync(
        string? commitMessage = null,
        GitCommitMetadata? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var targetRef = await GetTargetReferenceAsync(cancellationToken).ConfigureAwait(false);
        using (await _indexManager.AcquireIndexMutationLockAsync(targetRef, cancellationToken).ConfigureAwait(false))
        {
            var mergeHeadPath = Path.Combine(GitDirectory, "MERGE_HEAD");
            if (!File.Exists(mergeHeadPath))
            {
                throw new InvalidOperationException("No merge in progress to continue.");
            }

            var conflicted = await GetConflictedFilesAsync(cancellationToken).ConfigureAwait(false);
            if (conflicted.Count > 0)
            {
                throw new InvalidOperationException($"Cannot continue merge: {conflicted.Count} files still have conflicts.");
            }

            var mergeHeadRaw = (await File.ReadAllTextAsync(mergeHeadPath, cancellationToken).ConfigureAwait(false)).Trim();
            var mergeHead = new GitHash(mergeHeadRaw);

            var message = commitMessage;
            if (string.IsNullOrWhiteSpace(message))
            {
                var mergeMsgPath = Path.Combine(GitDirectory, "MERGE_MSG");
                if (File.Exists(mergeMsgPath))
                {
                    var lines = (await File.ReadAllLinesAsync(mergeMsgPath, cancellationToken).ConfigureAwait(false))
                        .Where(l => !l.TrimStart().StartsWith('#'))
                        .ToList();
                    message = string.Join("\n", lines).Trim();
                }

                if (string.IsNullOrWhiteSpace(message))
                {
                    message = $"Merge commit '{mergeHead.Value}'";
                }
            }

            var headHash = await ReferenceStore.ResolveHeadAsync(cancellationToken).ConfigureAwait(false);
            var parents = new[] { headHash, mergeHead };

            var index = await GitIndex.ReadAsync(_indexManager.IndexPath, HashLengthBytes, cancellationToken).ConfigureAwait(false);
            var treeHash = await _repo.WriteTreeAsync(index, cancellationToken).ConfigureAwait(false);

            var commitMetadata = metadata ?? GetDefaultMetadata(message);
            var payload = GitRepository.BuildCommitPayload(treeHash, parents, commitMetadata);
            var commitHash = await ObjectStore.WriteObjectAsync(GitObjectType.Commit, payload, cancellationToken).ConfigureAwait(false);

            await UpdateHeadOrBranchAsync(commitHash, targetRef, cancellationToken).ConfigureAwait(false);

            UpdateIndexStatCache(index);
            await index.WriteAsync(_indexManager.IndexPath, HashLengthBytes, cancellationToken).ConfigureAwait(false);

            RemoveMergeStateFiles();
            InvalidateCaches();
            return commitHash;
        }
    }

    /// <inheritdoc />
    public async Task<GitMergeResult> MergeAsync(
        string branchOrCommit,
        GitMergeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var targetRef = await GetTargetReferenceAsync(cancellationToken).ConfigureAwait(false);
        using (await _indexManager.AcquireIndexMutationLockAsync(targetRef, cancellationToken).ConfigureAwait(false))
        {
            if (await IsMergeInProgressAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("A merge is already in progress.");
            }

            var status = await GetStatusAsync(includeUntracked: false, includeClean: false, cancellationToken).ConfigureAwait(false);
            if (status.Entries.Any(e => e.IsStaged || e.HasWorkingTreeChanges || e.IsConflicted))
            {
                throw new InvalidOperationException("Cannot merge with uncommitted changes in the working directory.");
            }

            var theirCommit = await _repo.GetCommitAsync(branchOrCommit, cancellationToken).ConfigureAwait(false);
            var theirHash = theirCommit.Id;
            var theirLabel = branchOrCommit.StartsWith("refs/heads/", StringComparison.Ordinal)
                ? branchOrCommit[11..]
                : branchOrCommit;

            var headHash = await ReferenceStore.ResolveHeadAsync(cancellationToken).ConfigureAwait(false);
            if (headHash.Equals(theirHash))
            {
                return new GitMergeResult(true, Array.Empty<string>(), headHash, GitMergeStatus.AlreadyUpToDate, "Already up to date.");
            }

            var headCommit = await _repo.GetCommitAsync(headHash.Value, cancellationToken).ConfigureAwait(false);
            var ourLeaves = await _repo.LoadLeafEntriesAsync(headCommit.Tree, cancellationToken).ConfigureAwait(false);
            var theirLeaves = await _repo.LoadLeafEntriesAsync(theirCommit.Tree, cancellationToken).ConfigureAwait(false);

            var mergeBaseHash = await _repo.FindMergeBaseAsync(headHash, theirHash, cancellationToken).ConfigureAwait(false);

            if (mergeBaseHash.HasValue && mergeBaseHash.Value.Equals(theirHash))
            {
                return new GitMergeResult(true, Array.Empty<string>(), headHash, GitMergeStatus.AlreadyUpToDate, "Already up to date.");
            }

            if (mergeBaseHash.HasValue && mergeBaseHash.Value.Equals(headHash))
            {
                // Fast forward
                if (options?.NoFastForward != true)
                {
                    // Preflight target tree paths and prevent overwriting untracked working tree files
                    foreach (var (path, _) in theirLeaves)
                    {
                        var normalizedPath = _indexManager.NormalizeAndValidateRelativePath(path);
                        if (!ourLeaves.ContainsKey(normalizedPath))
                        {
                            var fullPath = Path.Combine(RootPath, normalizedPath.Replace('/', Path.DirectorySeparatorChar));
                            if (File.Exists(fullPath) || Directory.Exists(fullPath))
                            {
                                throw new InvalidOperationException($"The untracked working tree file '{normalizedPath}' would be overwritten by merge.");
                            }
                        }
                    }

                    await SyncWorkspaceToCommitAsync(theirCommit, cancellationToken).ConfigureAwait(false);
                    await UpdateHeadOrBranchAsync(theirHash, targetRef, cancellationToken).ConfigureAwait(false);
                    InvalidateCaches();
                    return new GitMergeResult(true, Array.Empty<string>(), theirHash, GitMergeStatus.FastForward, "Fast-forward");
                }
            }

            if (options?.FastForwardOnly == true)
            {
                throw new InvalidOperationException("Not possible to fast-forward, aborting.");
            }

            // 3-way merge
            var baseLeaves = mergeBaseHash.HasValue
                ? await _repo.LoadLeafEntriesAsync((await _repo.GetCommitAsync(mergeBaseHash.Value.Value, cancellationToken).ConfigureAwait(false)).Tree, cancellationToken).ConfigureAwait(false)
                : new Dictionary<string, TreeLeaf>(StringComparer.Ordinal);

            var allPaths = baseLeaves.Keys
                .Concat(ourLeaves.Keys)
                .Concat(theirLeaves.Keys)
                .Distinct(StringComparer.Ordinal)
                .Select(p => _indexManager.NormalizeAndValidateRelativePath(p))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

            // Check if untracked files in working tree would be overwritten
            foreach (var path in allPaths)
            {
                var ourLeaf = ourLeaves.TryGetValue(path, out var ol) ? ol : (TreeLeaf?)null;
                var theirLeaf = theirLeaves.TryGetValue(path, out var tl) ? tl : (TreeLeaf?)null;
                if (!ourLeaf.HasValue && theirLeaf.HasValue)
                {
                    var fullPath = Path.Combine(RootPath, path.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(fullPath) || Directory.Exists(fullPath))
                    {
                        throw new InvalidOperationException($"The untracked working tree file '{path}' would be overwritten by merge.");
                    }
                }
            }

            var newIndex = new GitIndex();
            var conflictedFiles = new List<string>();

            foreach (var path in allPaths)
            {
                var baseLeaf = baseLeaves.TryGetValue(path, out var bl) ? bl : (TreeLeaf?)null;
                var ourLeaf = ourLeaves.TryGetValue(path, out var ol) ? ol : (TreeLeaf?)null;
                var theirLeaf = theirLeaves.TryGetValue(path, out var tl) ? tl : (TreeLeaf?)null;

                // Both sides have identical state
                if (ourLeaf.HasValue && theirLeaf.HasValue && ourLeaf.Value.Hash.Equals(theirLeaf.Value.Hash) && ourLeaf.Value.Mode == theirLeaf.Value.Mode)
                {
                    newIndex.AddOrUpdate(new GitIndexEntry(path, ourLeaf.Value.Hash, ourLeaf.Value.Mode, 0));
                    continue;
                }

                // Added in their branch only
                if (!ourLeaf.HasValue && !baseLeaf.HasValue && theirLeaf.HasValue)
                {
                    await WriteLeafToWorkspaceAsync(path, theirLeaf.Value, cancellationToken).ConfigureAwait(false);
                    newIndex.AddOrUpdate(new GitIndexEntry(path, theirLeaf.Value.Hash, theirLeaf.Value.Mode, 0));
                    continue;
                }

                // Added in our branch only
                if (ourLeaf.HasValue && !baseLeaf.HasValue && !theirLeaf.HasValue)
                {
                    newIndex.AddOrUpdate(new GitIndexEntry(path, ourLeaf.Value.Hash, ourLeaf.Value.Mode, 0));
                    continue;
                }

                // Deleted in their branch, unchanged in ours
                if (baseLeaf.HasValue && ourLeaf.HasValue && !theirLeaf.HasValue && baseLeaf.Value.Hash.Equals(ourLeaf.Value.Hash) && baseLeaf.Value.Mode == ourLeaf.Value.Mode)
                {
                    DeleteFileFromWorkspace(path);
                    continue;
                }

                // Deleted in our branch, unchanged in theirs
                if (baseLeaf.HasValue && !ourLeaf.HasValue && theirLeaf.HasValue && baseLeaf.Value.Hash.Equals(theirLeaf.Value.Hash) && baseLeaf.Value.Mode == theirLeaf.Value.Mode)
                {
                    continue;
                }

                // Both branches deleted the file
                if (baseLeaf.HasValue && !ourLeaf.HasValue && !theirLeaf.HasValue)
                {
                    continue;
                }

                // Modified in their branch, unchanged in ours
                if (baseLeaf.HasValue && ourLeaf.HasValue && theirLeaf.HasValue && baseLeaf.Value.Hash.Equals(ourLeaf.Value.Hash) && baseLeaf.Value.Mode == ourLeaf.Value.Mode)
                {
                    await WriteLeafToWorkspaceAsync(path, theirLeaf.Value, cancellationToken).ConfigureAwait(false);
                    newIndex.AddOrUpdate(new GitIndexEntry(path, theirLeaf.Value.Hash, theirLeaf.Value.Mode, 0));
                    continue;
                }

                // Modified in our branch, unchanged in theirs
                if (baseLeaf.HasValue && ourLeaf.HasValue && theirLeaf.HasValue && baseLeaf.Value.Hash.Equals(theirLeaf.Value.Hash) && baseLeaf.Value.Mode == theirLeaf.Value.Mode)
                {
                    newIndex.AddOrUpdate(new GitIndexEntry(path, ourLeaf.Value.Hash, ourLeaf.Value.Mode, 0));
                    continue;
                }

                // Delete/Modify conflict: ours modified, theirs deleted
                if (baseLeaf.HasValue && ourLeaf.HasValue && !theirLeaf.HasValue)
                {
                    newIndex.AddOrUpdate(new GitIndexEntry(path, baseLeaf.Value.Hash, baseLeaf.Value.Mode, 1));
                    newIndex.AddOrUpdate(new GitIndexEntry(path, ourLeaf.Value.Hash, ourLeaf.Value.Mode, 2));
                    conflictedFiles.Add(path);
                    continue;
                }

                // Modify/Delete conflict: theirs modified, ours deleted
                if (baseLeaf.HasValue && !ourLeaf.HasValue && theirLeaf.HasValue)
                {
                    await WriteLeafToWorkspaceAsync(path, theirLeaf.Value, cancellationToken).ConfigureAwait(false);
                    newIndex.AddOrUpdate(new GitIndexEntry(path, baseLeaf.Value.Hash, baseLeaf.Value.Mode, 1));
                    newIndex.AddOrUpdate(new GitIndexEntry(path, theirLeaf.Value.Hash, theirLeaf.Value.Mode, 3));
                    conflictedFiles.Add(path);
                    continue;
                }

                // Both modified (or added with different content)
                if (ourLeaf.HasValue && theirLeaf.HasValue)
                {
                    var isSubmodule = ourLeaf.Value.Mode == GitRepository.SubmoduleMode || theirLeaf.Value.Mode == GitRepository.SubmoduleMode;
                    if (!isSubmodule && ourLeaf.Value.Mode == theirLeaf.Value.Mode)
                    {
                        var baseBlob = baseLeaf.HasValue ? await ObjectStore.ReadObjectAsync(baseLeaf.Value.Hash, cancellationToken).ConfigureAwait(false) : null;
                        var ourBlob = await ObjectStore.ReadObjectAsync(ourLeaf.Value.Hash, cancellationToken).ConfigureAwait(false);
                        var theirBlob = await ObjectStore.ReadObjectAsync(theirLeaf.Value.Hash, cancellationToken).ConfigureAwait(false);

                        var isBinary = UnifiedDiffFormatter.IsBinary(ourBlob.Content) ||
                                       UnifiedDiffFormatter.IsBinary(theirBlob.Content) ||
                                       (baseBlob != null && UnifiedDiffFormatter.IsBinary(baseBlob.Content)) ||
                                       !System.Text.Unicode.Utf8.IsValid(ourBlob.Content) ||
                                       !System.Text.Unicode.Utf8.IsValid(theirBlob.Content) ||
                                       (baseBlob != null && !System.Text.Unicode.Utf8.IsValid(baseBlob.Content));

                        if (!isBinary)
                        {
                            var mergeResult = Diff3Merge.Merge(
                                baseBlob?.Content,
                                ourBlob.Content,
                                theirBlob.Content,
                                "HEAD",
                                theirLabel);

                            if (!mergeResult.HasConflict)
                            {
                                await WriteContentToWorkspaceAsync(path, mergeResult.MergedBytes, ourLeaf.Value.Mode, cancellationToken).ConfigureAwait(false);
                                var mergedHash = await ObjectStore.WriteObjectAsync(GitObjectType.Blob, mergeResult.MergedBytes, cancellationToken).ConfigureAwait(false);
                                newIndex.AddOrUpdate(new GitIndexEntry(path, mergedHash, ourLeaf.Value.Mode, 0));
                                continue;
                            }

                            // Text merge conflict with markers
                            await WriteContentToWorkspaceAsync(path, mergeResult.MergedBytes, ourLeaf.Value.Mode, cancellationToken).ConfigureAwait(false);
                            if (baseLeaf.HasValue)
                            {
                                newIndex.AddOrUpdate(new GitIndexEntry(path, baseLeaf.Value.Hash, baseLeaf.Value.Mode, 1));
                            }
                            newIndex.AddOrUpdate(new GitIndexEntry(path, ourLeaf.Value.Hash, ourLeaf.Value.Mode, 2));
                            newIndex.AddOrUpdate(new GitIndexEntry(path, theirLeaf.Value.Hash, theirLeaf.Value.Mode, 3));
                            conflictedFiles.Add(path);
                            continue;
                        }
                    }

                    // Binary, submodule, or mode conflict
                    if (baseLeaf.HasValue)
                    {
                        newIndex.AddOrUpdate(new GitIndexEntry(path, baseLeaf.Value.Hash, baseLeaf.Value.Mode, 1));
                    }
                    newIndex.AddOrUpdate(new GitIndexEntry(path, ourLeaf.Value.Hash, ourLeaf.Value.Mode, 2));
                    newIndex.AddOrUpdate(new GitIndexEntry(path, theirLeaf.Value.Hash, theirLeaf.Value.Mode, 3));
                    conflictedFiles.Add(path);
                }
            }

            await newIndex.WriteAsync(_indexManager.IndexPath, HashLengthBytes, cancellationToken).ConfigureAwait(false);

            if (conflictedFiles.Count > 0)
            {
                var mergeHeadPath = Path.Combine(GitDirectory, "MERGE_HEAD");
                await File.WriteAllTextAsync(mergeHeadPath, theirHash.Value + "\n", cancellationToken).ConfigureAwait(false);

                var mergeMsgPath = Path.Combine(GitDirectory, "MERGE_MSG");
                var msgBuilder = new StringBuilder();
                msgBuilder.AppendLine($"Merge branch '{theirLabel}'");
                msgBuilder.AppendLine();
                msgBuilder.AppendLine("# Conflicts:");
                foreach (var cf in conflictedFiles)
                {
                    msgBuilder.AppendLine($"#\t{cf}");
                }
                await File.WriteAllTextAsync(mergeMsgPath, msgBuilder.ToString(), cancellationToken).ConfigureAwait(false);

                InvalidateCaches();
                return new GitMergeResult(false, conflictedFiles, null, GitMergeStatus.Conflicted, "Automatic merge failed; fix conflicts and then commit the result.");
            }

            // All merged cleanly! Create merge commit with two parents
            var treeHash = await _repo.WriteTreeAsync(newIndex, cancellationToken).ConfigureAwait(false);
            var defaultMsg = options?.CommitMessage ?? $"Merge branch '{theirLabel}'";
            var commitMetadata = options?.Metadata ?? GetDefaultMetadata(defaultMsg);
            var payload = GitRepository.BuildCommitPayload(treeHash, new[] { headHash, theirHash }, commitMetadata);
            var commitHash = await ObjectStore.WriteObjectAsync(GitObjectType.Commit, payload, cancellationToken).ConfigureAwait(false);

            await UpdateHeadOrBranchAsync(commitHash, targetRef, cancellationToken).ConfigureAwait(false);

            UpdateIndexStatCache(newIndex);
            await newIndex.WriteAsync(_indexManager.IndexPath, HashLengthBytes, cancellationToken).ConfigureAwait(false);

            InvalidateCaches();
            return new GitMergeResult(true, Array.Empty<string>(), commitHash, GitMergeStatus.Merged, "Merge made by 3-way merge.");
        }
    }

    #endregion

    #region Helpers

    private void RemoveMergeStateFiles()
    {
        var mergeHead = Path.Combine(GitDirectory, "MERGE_HEAD");
        var mergeMsg = Path.Combine(GitDirectory, "MERGE_MSG");
        var mergeMode = Path.Combine(GitDirectory, "MERGE_MODE");

        if (File.Exists(mergeHead)) File.Delete(mergeHead);
        if (File.Exists(mergeMsg)) File.Delete(mergeMsg);
        if (File.Exists(mergeMode)) File.Delete(mergeMode);
    }

    private async Task WriteLeafToWorkspaceAsync(string path, TreeLeaf leaf, CancellationToken cancellationToken)
    {
        var normalizedPath = _indexManager.NormalizeAndValidateRelativePath(path);
        var fullPath = Path.Combine(RootPath, normalizedPath.Replace('/', Path.DirectorySeparatorChar));
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        if (File.Exists(fullPath))
        {
            File.SetAttributes(fullPath, FileAttributes.Normal);
        }

        var obj = await ObjectStore.ReadObjectAsync(leaf.Hash, cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(fullPath, obj.Content, cancellationToken).ConfigureAwait(false);

        if (!OperatingSystem.IsWindows())
        {
            try
            {
                var currentUnixMode = File.GetUnixFileMode(fullPath);
                if (leaf.Mode == 33261)
                {
                    File.SetUnixFileMode(fullPath, currentUnixMode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
                }
                else if (leaf.Mode == 33188)
                {
                    File.SetUnixFileMode(fullPath, currentUnixMode & ~(UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute));
                }
            }
            catch
            {
            }
        }
    }

    private async Task WriteContentToWorkspaceAsync(string path, byte[] content, int mode, CancellationToken cancellationToken)
    {
        var normalizedPath = _indexManager.NormalizeAndValidateRelativePath(path);
        var fullPath = Path.Combine(RootPath, normalizedPath.Replace('/', Path.DirectorySeparatorChar));
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        if (File.Exists(fullPath))
        {
            File.SetAttributes(fullPath, FileAttributes.Normal);
        }

        await File.WriteAllBytesAsync(fullPath, content, cancellationToken).ConfigureAwait(false);

        if (!OperatingSystem.IsWindows())
        {
            try
            {
                var currentUnixMode = File.GetUnixFileMode(fullPath);
                if (mode == 33261)
                {
                    File.SetUnixFileMode(fullPath, currentUnixMode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
                }
                else if (mode == 33188)
                {
                    File.SetUnixFileMode(fullPath, currentUnixMode & ~(UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute));
                }
            }
            catch
            {
            }
        }
    }

    private void DeleteFileFromWorkspace(string path)
    {
        var normalizedPath = _indexManager.NormalizeAndValidateRelativePath(path);
        var fullPath = Path.Combine(RootPath, normalizedPath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(fullPath))
        {
            File.SetAttributes(fullPath, FileAttributes.Normal);
            File.Delete(fullPath);

            var parent = Path.GetDirectoryName(fullPath);
            while (!string.IsNullOrEmpty(parent) &&
                   !parent.Equals(RootPath, StringComparison.OrdinalIgnoreCase) &&
                   Directory.Exists(parent) &&
                   !Directory.EnumerateFileSystemEntries(parent).Any())
            {
                try
                {
                    Directory.Delete(parent);
                    parent = Path.GetDirectoryName(parent);
                }
                catch
                {
                    break;
                }
            }
        }
    }

    private async Task SyncWorkspaceToCommitAsync(GitCommit targetCommit, CancellationToken cancellationToken)
    {
        var oldIndex = await GitIndex.ReadAsync(_indexManager.IndexPath, HashLengthBytes, cancellationToken).ConfigureAwait(false);
        var targetFiles = new HashSet<string>(StringComparer.Ordinal);
        var newIndex = new GitIndex();

        await foreach (var item in EnumerateCommitTreeAsync(targetCommit.Id.Value, null, SearchOption.AllDirectories, cancellationToken).ConfigureAwait(false))
        {
            if (item.Entry.Kind == GitTreeEntryKind.Blob)
            {
                var normalizedPath = _indexManager.NormalizeAndValidateRelativePath(item.Path);
                targetFiles.Add(normalizedPath);
                var fullPath = Path.Combine(RootPath, normalizedPath.Replace('/', Path.DirectorySeparatorChar));
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
                var entry = GitIndexEntry.FromFileInfo(normalizedPath, fileInfo, item.Entry.Hash);
                entry.FileMode = item.Entry.Mode;
                newIndex.AddOrUpdate(entry);
            }
        }

        foreach (var oldEntry in oldIndex.Entries)
        {
            var normalizedPath = _indexManager.NormalizeAndValidateRelativePath(oldEntry.Path);
            if (!targetFiles.Contains(normalizedPath))
            {
                var fullPath = Path.Combine(RootPath, normalizedPath.Replace('/', Path.DirectorySeparatorChar));
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

    private static async Task<GitHash?> TryGetSubmoduleHeadAsync(string fullPath, CancellationToken cancellationToken)
    {
        try
        {
            string? gitDir = null;
            var gitItemPath = Path.Combine(fullPath, ".git");

            if (Directory.Exists(gitItemPath))
            {
                gitDir = gitItemPath;
            }
            else if (File.Exists(gitItemPath))
            {
                var content = (await File.ReadAllTextAsync(gitItemPath, cancellationToken).ConfigureAwait(false)).Trim();
                if (content.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase))
                {
                    var target = content.Substring(7).Trim();
                    gitDir = Path.GetFullPath(Path.Combine(fullPath, target));
                }
            }
            else if (File.Exists(Path.Combine(fullPath, "HEAD")))
            {
                gitDir = fullPath;
            }

            if (gitDir == null || !Directory.Exists(gitDir))
            {
                return null;
            }

            var refStore = new GitReferenceStore(gitDir);
            return await refStore.ResolveHeadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    #endregion
}
