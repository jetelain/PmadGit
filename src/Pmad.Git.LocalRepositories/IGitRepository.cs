namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Interface for querying commits, trees, and blobs from a git repository.
/// </summary>
public interface IGitRepository
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
    /// Gets the underlying object store used to access Git objects.
    /// </summary>
    /// <remarks>The object store provides low-level access to Git objects such as commits, trees, blobs, and
    /// tags. Use this property to perform advanced operations that require direct interaction with the Git object
    /// database.</remarks>
    IGitObjectStore ObjectStore { get; }

    /// <summary>
    /// Resolves <paramref name="reference"/> (defaults to HEAD) and returns the corresponding commit.
    /// </summary>
    /// <param name="reference">Commit hash or reference name; HEAD if omitted.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>The resolved <see cref="GitCommit"/>.</returns>
    Task<GitCommit> GetCommitAsync(string? reference = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enumerates commits reachable from <paramref name="reference"/> in depth-first order.
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
    /// <param name="cancellationToken">Token used to cancel the async iteration.</param>
    /// <returns>An async stream of tree items rooted at the specified path.</returns>
    IAsyncEnumerable<GitTreeItem> EnumerateCommitTreeAsync(
        string? reference = null,
        string? path = null,
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
    /// The caller is responsible for disposing the returned <see cref="GitObjectStream"/>.
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
    IAsyncEnumerable<GitCommit> GetFileHistoryAsync(
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
    /// Clears cached git metadata so subsequent operations reflect the current repository state.
    /// </summary>
    /// <param name="clearAllData">Clears all cached data, including data that should not change on normal git operations</param>
    void InvalidateCaches(bool clearAllData = false);

    /// <summary>
    /// Returns a snapshot of all references stored in the repository.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>A dictionary keyed by fully qualified reference names.</returns>
    Task<IReadOnlyDictionary<string, GitHash>> GetReferencesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes or overwrites the value of a reference file with validation.
    /// This method validates that the expected old value matches the current value before updating.
    /// </summary>
    /// <param name="referencePath">Fully qualified reference path (for example refs/heads/main).</param>
    /// <param name="expectedOldValue">Expected current hash of the reference, or null if reference should not exist.</param>
    /// <param name="newValue">New hash to persist, or null to delete the reference.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <exception cref="InvalidOperationException">Thrown when the expected old value doesn't match the current value.</exception>
    Task WriteReferenceWithValidationAsync(
        string referencePath,
        GitHash? expectedOldValue,
        GitHash? newValue,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Acquires locks for multiple references in a consistent order to prevent deadlocks.
    /// This is used for batch reference updates like git push.
    /// </summary>
    /// <param name="referencePaths">Fully qualified reference paths to lock.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>A disposable lock that must be released after all operations complete.</returns>
    Task<IGitMultipleReferenceLocks> AcquireMultipleReferenceLocksAsync(IEnumerable<string> referencePaths, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a commit is reachable from another commit (for fast-forward validation).
    /// </summary>
    /// <param name="from">The commit to start from.</param>
    /// <param name="to">The target commit to check reachability.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>True if 'to' is reachable from 'from', false otherwise.</returns>
    Task<bool> IsCommitReachableAsync(GitHash from, GitHash to, CancellationToken cancellationToken = default);
}
