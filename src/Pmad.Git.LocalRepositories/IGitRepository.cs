namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Interface for querying commits, trees, and blobs from a git repository.
/// </summary>
public interface IGitRepository : IGitRepositoryCacheInvalidator
{
    /// <summary>
    /// Absolute path to the repository working tree root.
    /// </summary>
    string RootPath { get; }

    /// <summary>
    /// Absolute path to the repository .git directory.
    /// </summary>
    string GitDirectory { get; }

    /// <summary>
    /// Gets the number of bytes used for object identifiers in this repository.
    /// </summary>
    int HashLengthBytes { get; }

    /// <summary>
    /// Gets a value indicating whether this repository is bare (no working directory).
    /// </summary>
    bool IsBare { get; }

    /// <summary>
    /// Gets the index manager for working tree and staging operations, or null if the repository is bare.
    /// </summary>
    GitIndexManager? IndexManager { get; }

    /// <summary>
    /// Gets the underlying object store used to access Git objects.
    /// </summary>
    /// <remarks>The object store provides low-level access to Git objects such as commits, trees, blobs, and
    /// tags. Use this property to perform advanced operations that require direct interaction with the Git object
    /// database.</remarks>
    IGitObjectStore ObjectStore { get; }

    /// <summary>
    /// Gets the underlying reference store used to access and update Git references.
    /// </summary>
    IGitReferenceStore ReferenceStore { get; }

    /// <summary>
    /// Gets the lock manager used to synchronize reference/object writes for this repository.
    /// </summary>
    /// <remarks>
    /// Share this instance with other components (e.g. a CLI-based wrapper) operating on the same
    /// repository directory within the same process to synchronize concurrent writes.
    /// </remarks>
    IGitRepositoryLockManager LockManager { get; }

    /// <summary>
    /// Resolves <paramref name="reference"/> (defaults to HEAD) and returns the corresponding commit.
    /// </summary>
    /// <param name="reference">Commit hash or reference name; HEAD if omitted.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>The resolved <see cref="GitCommit"/>.</returns>
    Task<GitCommit> GetCommitAsync(string? reference = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enumerates commits reachable from <paramref name="reference"/> in reverse chronological (newest-first) order.
    /// </summary>
    /// <param name="reference">Starting reference or commit hash; defaults to HEAD.</param>
    /// <param name="cancellationToken">Token used to cancel the async iteration.</param>
    /// <returns>An async stream of commits, newest first.</returns>
    IAsyncEnumerable<GitCommit> EnumerateCommitsAsync(string? reference = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enumerates the tree contents of a commit, optionally scoped to a sub-path.
    /// </summary>
    /// <param name="reference">Commit hash or ref to inspect; defaults to HEAD.</param>
    /// <param name="path">Optional directory path inside the tree to enumerate.</param>
    /// <param name="searchOption">Whether to include items in all subdirectories or only the specified directory; defaults to <see cref="SearchOption.AllDirectories"/>.</param>
    /// <param name="cancellationToken">Token used to cancel the async iteration.</param>
    /// <returns>An async stream of tree items rooted at the specified path.</returns>
    IAsyncEnumerable<GitTreeItem> EnumerateCommitTreeAsync(
        string? reference = null,
        string? path = null,
        SearchOption searchOption = SearchOption.AllDirectories,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a path exists in the specified commit and returns its type.
    /// </summary>
    /// <param name="path">Repository-relative path using / separators.</param>
    /// <param name="reference">Commit hash or ref to check; defaults to HEAD.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>The type of the path if it exists, or null if it does not exist.</returns>
    Task<GitTreeEntryKind?> GetPathTypeAsync(string path, string? reference = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a path exists in the specified commit.
    /// </summary>
    /// <param name="path">Repository-relative path using / separators.</param>
    /// <param name="reference">Commit hash or ref to check; defaults to HEAD.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>True if the path exists, false otherwise.</returns>
    Task<bool> PathExistsAsync(string path, string? reference = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a file exists at the specified path in the specified commit.
    /// </summary>
    /// <param name="filePath">Repository-relative file path using / separators.</param>
    /// <param name="reference">Commit hash or ref to check; defaults to HEAD.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>True if a file (blob) exists at the path, false otherwise.</returns>
    Task<bool> FileExistsAsync(string filePath, string? reference = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a directory exists at the specified path in the specified commit.
    /// </summary>
    /// <param name="directoryPath">Repository-relative directory path using / separators.</param>
    /// <param name="reference">Commit hash or ref to check; defaults to HEAD.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>True if a directory (tree) exists at the path, false otherwise.</returns>
    Task<bool> DirectoryExistsAsync(string directoryPath, string? reference = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the blob content at <paramref name="filePath"/> from the specified <paramref name="reference"/>.
    /// </summary>
    /// <param name="filePath">Repository-relative file path using / separators.</param>
    /// <param name="reference">Commit hash or ref to read from; defaults to HEAD.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>The blob payload as a byte array.</returns>
    Task<byte[]> ReadFileAsync(string filePath, string? reference = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the blob content and hash at <paramref name="filePath"/> from the specified <paramref name="reference"/>.
    /// </summary>
    /// <param name="filePath">Repository-relative file path using / separators.</param>
    /// <param name="reference">Commit hash or ref to read from; defaults to HEAD.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>A <see cref="GitFileContentAndHash"/> containing the blob payload and its hash.</returns>
    Task<GitFileContentAndHash> ReadFileAndHashAsync(string filePath, string? reference = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the blob content at <paramref name="filePath"/> from the specified <paramref name="reference"/> as a stream.
    /// For loose objects, the stream reads directly from disk without buffering the entire content.
    /// For pack objects, the stream is backed by a <see cref="System.IO.MemoryStream"/>.
    /// The caller is responsible for disposing the returned <see cref="GitObjectStream"/> (supports both <see langword="using"/> and <see langword="await using"/>).
    /// </summary>
    /// <param name="filePath">Repository-relative file path using / separators.</param>
    /// <param name="reference">Commit hash or ref to read from; defaults to HEAD.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>A <see cref="GitObjectStream"/> whose <see cref="GitObjectStream.Content"/> exposes the blob payload.</returns>
    Task<GitObjectStream> ReadFileStreamAsync(string filePath, string? reference = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams commits where <paramref name="filePath"/> changed, newest first.
    /// </summary>
    /// <param name="filePath">Repository-relative file path to inspect.</param>
    /// <param name="reference">Starting reference or commit hash; defaults to HEAD.</param>
    /// <param name="cancellationToken">Token used to cancel the async iteration.</param>
    /// <returns>An async stream of commits affecting the file.</returns>
    IAsyncEnumerable<GitCommit> EnumerateFileHistoryAsync(
        string filePath,
        string? reference = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new commit on the specified branch by applying the provided operations.
    /// This method is thread-safe and prevents concurrent commits to the same branch.
    /// </summary>
    /// <param name="branchName">Branch to update (short name or fully qualified ref).</param>
    /// <param name="operations">Sequence of file-system operations to apply.</param>
    /// <param name="metadata">Commit metadata (message, author, committer).</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>The hash of the newly created commit.</returns>
    Task<GitHash> CreateCommitAsync(
        string branchName,
        IEnumerable<GitCommitOperation> operations,
        GitCommitMetadata metadata,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Amends the current HEAD commit on the specified branch by creating a new commit
    /// that has the same parent(s) as the current HEAD commit, and updating the branch reference.
    /// </summary>
    /// <param name="branchName">Branch to update (short name or fully qualified ref).</param>
    /// <param name="operations">Sequence of file-system operations to apply to the commit tree.</param>
    /// <param name="metadata">Optional commit metadata. If null, the existing commit's message and author are preserved, with an updated committer timestamp.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>The hash of the newly created amended commit.</returns>
    Task<GitHash> AmendCommitAsync(
        string branchName,
        IEnumerable<GitCommitOperation> operations,
        GitCommitMetadata? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a single squashed commit containing the current HEAD tree of the specified branch,
    /// setting its parent to <paramref name="baseCommitHash"/>, and advances the branch reference.
    /// </summary>
    /// <param name="branchName">Branch to update (short name or fully qualified ref).</param>
    /// <param name="baseCommitHash">The ancestor commit to set as the single parent of the squashed commit.</param>
    /// <param name="metadata">Commit metadata (message, author, committer) for the squashed commit.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>The hash of the newly created squashed commit.</returns>
    Task<GitHash> SquashCommitsAsync(
        string branchName,
        GitHash baseCommitHash,
        GitCommitMetadata metadata,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists files under the optional <paramref name="path"/> in the specified <paramref name="reference"/> along with the last commit that changed each file.
    /// This is more efficient than calling <see cref="EnumerateCommitTreeAsync"/> followed by <see cref="EnumerateFileHistoryAsync"/> for each file
    /// because the commit graph is traversed only once.
    /// </summary>
    /// <param name="reference">Starting reference or commit hash; defaults to HEAD.</param>
    /// <param name="path">Optional directory path to scope the result; all files when omitted. Returns an empty list if the path does not exist in the start commit.</param>
    /// <param name="searchOption">Whether to include files in all subdirectories or only the specified directory; defaults to <see cref="SearchOption.AllDirectories"/>.</param>
    /// <param name="predicate">Optional predicate applied to each file path; only files for which it returns <see langword="true"/> are included. All files are included when omitted.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>A list of <see cref="GitFileLastChange"/> entries, one per file, sorted by path in ordinal order, each pairing the file path with its most recent modifying commit.</returns>
    Task<IReadOnlyList<GitFileLastChange>> GetFilesWithLastChangeAsync(
        string? reference = null,
        string? path = null,
        SearchOption searchOption = SearchOption.AllDirectories,
        Func<string, bool>? predicate = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a commit is reachable from another commit (for fast-forward validation).
    /// </summary>
    /// <param name="from">The commit to start from.</param>
    /// <param name="to">The target commit to check reachability.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>True if 'to' is reachable from 'from', false otherwise.</returns>
    Task<bool> IsCommitReachableAsync(GitHash from, GitHash to, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the best common ancestor (merge base) between two commits.
    /// </summary>
    /// <param name="commit1">The first commit hash.</param>
    /// <param name="commit2">The second commit hash.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>The merge base commit hash, or null if no common ancestor exists.</returns>
    Task<GitHash?> FindMergeBaseAsync(GitHash commit1, GitHash commit2, CancellationToken cancellationToken = default);

    /// <summary>
    /// Compares two trees and returns the list of added, modified, and deleted files.
    /// </summary>
    /// <param name="oldTreeHash">The hash of the baseline tree.</param>
    /// <param name="newTreeHash">The hash of the target tree.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>A list of <see cref="GitTreeChange"/> entries sorted by path in ordinal order.</returns>
    Task<IReadOnlyList<GitTreeChange>> CompareTreesAsync(
        GitHash oldTreeHash,
        GitHash newTreeHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the file-level changes introduced by a commit compared to its primary parent.
    /// If the commit has no parent (root commit), all files in the tree are reported as added.
    /// </summary>
    /// <param name="commitHash">The commit hash to inspect.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>A list of <see cref="GitTreeChange"/> entries sorted by path in ordinal order.</returns>
    Task<IReadOnlyList<GitTreeChange>> GetCommitChangesAsync(
        GitHash commitHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the file-level changes introduced by the commit referenced by <paramref name="reference"/> (defaults to HEAD) compared to its primary parent.
    /// If the commit has no parent (root commit), all files in the tree are reported as added.
    /// </summary>
    /// <param name="reference">Commit hash or reference name; defaults to HEAD.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>A list of <see cref="GitTreeChange"/> entries sorted by path in ordinal order.</returns>
    Task<IReadOnlyList<GitTreeChange>> GetCommitChangesAsync(
        string? reference = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the name of the currently checked out branch (e.g. "main"), 
    /// or null if HEAD is detached or points to an invalid ref.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>The active branch name, or null if HEAD is detached.</returns>
    Task<string?> GetCurrentBranchNameAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether HEAD is detached (points directly to a commit hash rather than a symbolic ref).
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>True if HEAD is detached, false otherwise.</returns>
    Task<bool> IsHeadDetachedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates a reference in any namespace (e.g. refs/backups/draft-1).
    /// </summary>
    /// <param name="referencePath">Fully qualified reference path starting with 'refs/'.</param>
    /// <param name="targetCommit">Commit hash the reference will point to.</param>
    /// <param name="overwrite">When false, throws if the reference already exists; when true, overwrites.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    Task CreateReferenceAsync(
        string referencePath,
        GitHash targetCommit,
        bool overwrite = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all references matching a specific prefix (e.g. 'refs/backups/').
    /// </summary>
    /// <param name="prefix">Prefix string to filter references.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>A dictionary of matching references keyed by fully qualified reference name.</returns>
    Task<IReadOnlyDictionary<string, GitHash>> GetReferencesByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a reference in any namespace.
    /// </summary>
    /// <param name="referencePath">Fully qualified reference path starting with 'refs/'.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    Task DeleteReferenceAsync(
        string referencePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a Git tree object representing the stage 0 entries in the specified index.
    /// </summary>
    /// <param name="index">The Git index containing entries to write.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>The root tree hash.</returns>
    Task<GitHash> WriteTreeAsync(
        GitIndex index,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the branch names of the repository.
    /// </summary>
    /// <param name="includeRemote">When <see langword="true"/>, includes remote-tracking branches.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of branch names.</returns>
    Task<IReadOnlyList<string>> GetBranchesAsync(
        bool includeRemote = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new branch pointing to the specified start point (or HEAD).
    /// </summary>
    /// <param name="branchName">The name of the branch to create.</param>
    /// <param name="startPoint">Optional commit hash or reference to start from (defaults to HEAD).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CreateBranchAsync(
        string branchName,
        string? startPoint = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a local branch.
    /// </summary>
    /// <param name="oldName">Current branch name.</param>
    /// <param name="newName">New branch name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RenameBranchAsync(
        string oldName,
        string newName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a local branch.
    /// </summary>
    /// <param name="branchName">Name of the branch to delete.</param>
    /// <param name="force">When <see langword="true"/>, deletes the branch even if not merged into HEAD.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteBranchAsync(
        string branchName,
        bool force = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the upstream tracking status (ahead/behind commit counts) for the specified branch (or current branch).
    /// </summary>
    /// <param name="branch">Branch name, or <see langword="null"/> for current branch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="GitTrackingStatus"/> record.</returns>
    Task<GitTrackingStatus> GetTrackingStatusAsync(
        string? branch = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the specified commit is reachable from a remote tracking branch.
    /// </summary>
    /// <param name="commitHash">The commit hash to check.</param>
    /// <param name="remoteBranch">Specific remote branch (e.g. "origin/main"), or <see langword="null"/> for any remote branch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> if reachable from a remote branch; otherwise, <see langword="false"/>.</returns>
    Task<bool> IsCommitPushedAsync(
        GitHash commitHash,
        string? remoteBranch = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the value of a Git configuration key.
    /// </summary>
    /// <param name="key">The configuration key name (e.g. "user.name", "core.bare").</param>
    /// <param name="global">When <see langword="true"/>, reads from global config; otherwise reads effective repository config.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The configuration value, or <see langword="null"/> if unset.</returns>
    Task<string?> GetConfigAsync(
        string key,
        bool global = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the value of a Git configuration key.
    /// </summary>
    /// <param name="key">The configuration key name.</param>
    /// <param name="value">The value to set.</param>
    /// <param name="global">When <see langword="true"/>, writes to global config; otherwise writes to repository config.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SetConfigAsync(
        string key,
        string value,
        bool global = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a Git configuration key.
    /// </summary>
    /// <param name="key">The configuration key name.</param>
    /// <param name="global">When <see langword="true"/>, unsets from global config; otherwise unsets from repository config.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UnsetConfigAsync(
        string key,
        bool global = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes the unified diff between two commits, or between a commit and its first parent, optionally filtered to a sub-path.
    /// </summary>
    /// <param name="fromCommit">The base commit hash or reference; if null, defaults to the first parent of <paramref name="toCommit"/> (or empty tree if root commit).</param>
    /// <param name="toCommit">The target commit hash or reference; defaults to HEAD if null.</param>
    /// <param name="path">Optional path filter within the repository.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The unified diff text.</returns>
    Task<string> GetDiffAsync(
        string? fromCommit = null,
        string? toCommit = null,
        string? path = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes the unified diff introduced by a commit compared to its first parent (or empty tree if root commit).
    /// </summary>
    /// <param name="commitIsh">The commit hash or reference to inspect.</param>
    /// <param name="path">Optional path filter within the repository.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The unified diff text.</returns>
    Task<string> GetCommitDiffAsync(
        string commitIsh,
        string? path = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes the diff statistics (files changed, insertions, deletions) introduced by a commit.
    /// </summary>
    /// <param name="commitIsh">The commit hash or reference to inspect.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The <see cref="Diff.GitDiffStat"/> summary.</returns>
    Task<Diff.GitDiffStat> GetCommitStatAsync(
        string commitIsh,
        CancellationToken cancellationToken = default);
}

