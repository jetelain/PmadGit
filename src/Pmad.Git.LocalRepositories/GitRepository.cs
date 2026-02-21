using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace Pmad.Git.LocalRepositories;

/// <summary>
/// High-level entry point for querying commits, trees, and blobs from a local git repository.
/// </summary>
public sealed class GitRepository : IGitRepository
{
    private readonly GitObjectStore _objectStore;
    private readonly Dictionary<GitHash, GitCommit> _commitCache = new();
    private readonly Dictionary<GitHash, GitTree> _treeCache = new();
    private readonly object _commitLock = new();
    private readonly object _treeLock = new();
    private readonly GitRepositoryLockManager _lockManager = new();
    private Lazy<Task<Dictionary<string, GitHash>>> _references;
    private const int RegularFileMode = 33188; // 100644 in octal
    private const int DirectoryMode = 16384;   // 040000 in octal

    private GitRepository(string rootPath, string gitDirectory)
    {
        RootPath = rootPath;
        GitDirectory = gitDirectory;
        _objectStore = new GitObjectStore(gitDirectory);
        _references = CreateReferenceCache();
    }

    /// <summary>
    /// Absolute path to the repository working tree root.
    /// </summary>
    public string RootPath { get; }

    /// <summary>
    /// Absolute path to the repository .git directory.
    /// </summary>
    public string GitDirectory { get; }

    /// <summary>
    /// Gets the number of bytes used for object identifiers in this repository.
    /// </summary>
    public int HashLengthBytes => _objectStore.HashLengthBytes;

    /// <inheritdoc />
    public IGitObjectStore ObjectStore => _objectStore;

    /// <summary>
    /// Creates a new empty git repository at the specified path.
    /// </summary>
    /// <param name="path">Path where the repository should be created.</param>
    /// <param name="bare">Whether to create a bare repository (no working directory).</param>
    /// <param name="initialBranch">Name of the initial branch; defaults to "main".</param>
    /// <returns>An initialized <see cref="GitRepository"/>.</returns>
    public static GitRepository Init(string path, bool bare = false, string initialBranch = "main")
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be empty", nameof(path));
        }

        if (string.IsNullOrWhiteSpace(initialBranch))
        {
            throw new ArgumentException("Initial branch name cannot be empty", nameof(initialBranch));
        }

        var fullPath = Path.GetFullPath(path);
        string gitDirectory;
        string rootPath;

        if (bare)
        {
            gitDirectory = fullPath;
            rootPath = fullPath;
        }
        else
        {
            gitDirectory = Path.Combine(fullPath, ".git");
            rootPath = fullPath;
        }

        Directory.CreateDirectory(gitDirectory);

        if (Directory.GetFiles(gitDirectory).Length > 0 || Directory.GetDirectories(gitDirectory).Length > 0)
        {
            throw new InvalidOperationException($"Directory '{gitDirectory}' already exists and is not empty");
        }

        Directory.CreateDirectory(gitDirectory);
        Directory.CreateDirectory(Path.Combine(gitDirectory, "objects"));
        Directory.CreateDirectory(Path.Combine(gitDirectory, "objects", "info"));
        Directory.CreateDirectory(Path.Combine(gitDirectory, "objects", "pack"));
        Directory.CreateDirectory(Path.Combine(gitDirectory, "refs"));
        Directory.CreateDirectory(Path.Combine(gitDirectory, "refs", "heads"));
        Directory.CreateDirectory(Path.Combine(gitDirectory, "refs", "tags"));

        var headRef = $"ref: refs/heads/{initialBranch}";
        File.WriteAllText(Path.Combine(gitDirectory, "HEAD"), headRef + "\n");

        var configBuilder = new StringBuilder();
        configBuilder.AppendLine("[core]");
        configBuilder.AppendLine("\trepositoryformatversion = 0");
        configBuilder.AppendLine("\tfilemode = false");
        if (bare)
        {
            configBuilder.AppendLine("\tbare = true");
        }
        else
        {
            configBuilder.AppendLine("\tbare = false");
        }
        File.WriteAllText(Path.Combine(gitDirectory, "config"), configBuilder.ToString());

        File.WriteAllText(Path.Combine(gitDirectory, "description"), "Unnamed repository; edit this file 'description' to name the repository.\n");

        var hooksDir = Path.Combine(gitDirectory, "hooks");
        Directory.CreateDirectory(hooksDir);

        var infoDir = Path.Combine(gitDirectory, "info");
        Directory.CreateDirectory(infoDir);
        File.WriteAllText(Path.Combine(infoDir, "exclude"), "# git ls-files --others --exclude-from=.git/info/exclude\n# Lines that start with '#' are comments.\n");

        return new GitRepository(rootPath, gitDirectory);
    }

    /// <summary>
    /// Opens a git repository located at <paramref name="path"/> or its parent folders.
    /// </summary>
    /// <param name="path">Path pointing to a working tree or .git directory.</param>
    /// <returns>An initialized <see cref="GitRepository"/>.</returns>
    public static GitRepository Open(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (Path.GetFileName(fullPath).Equals(".git", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Directory.GetParent(fullPath)?.FullName ?? throw new DirectoryNotFoundException("Unable to determine repository root");
            return new GitRepository(parent, fullPath);
        }

        var gitDir = Path.Combine(fullPath, ".git");
        if (Directory.Exists(gitDir))
        {
            return new GitRepository(fullPath, gitDir);
        }

        if (File.Exists(Path.Combine(fullPath, "HEAD")) && File.Exists(Path.Combine(fullPath, "config")))
        {
            return new GitRepository(fullPath, fullPath);
        }

        throw new DirectoryNotFoundException($"Unable to locate a git repository starting from '{path}'");
    }

    /// <summary>
    /// Resolves <paramref name="reference"/> (defaults to HEAD) and returns the corresponding commit.
    /// </summary>
    /// <param name="reference">Commit hash or reference name; HEAD if omitted.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>The resolved <see cref="GitCommit"/>.</returns>
    public async Task<GitCommit> GetCommitAsync(string? reference = null, CancellationToken cancellationToken = default)
    {
        var hash = await ResolveReferenceAsync(reference, cancellationToken).ConfigureAwait(false);
        return await GetCommitAsync(hash, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Enumerates commits reachable from <paramref name="reference"/> in depth-first order.
    /// </summary>
    /// <param name="reference">Starting reference or commit hash; defaults to HEAD.</param>
    /// <param name="cancellationToken">Token used to cancel the async iteration.</param>
    /// <returns>An async stream of commits, newest first.</returns>
    public async IAsyncEnumerable<GitCommit> EnumerateCommitsAsync(string? reference = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var start = await ResolveReferenceAsync(reference, cancellationToken).ConfigureAwait(false);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<GitHash>();
        stack.Push(start);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = stack.Pop();
            if (!visited.Add(current.Value))
            {
                continue;
            }

            var commit = await GetCommitAsync(current, cancellationToken).ConfigureAwait(false);
            yield return commit;

            for (var i = commit.Parents.Count - 1; i >= 0; i--)
            {
                stack.Push(commit.Parents[i]);
            }
        }
    }

    /// <summary>
    /// Enumerates the tree contents of a commit, optionally scoped to a sub-path.
    /// </summary>
    /// <param name="reference">Commit hash or ref to inspect; defaults to HEAD.</param>
    /// <param name="path">Optional directory path inside the tree to enumerate.</param>
    /// <param name="cancellationToken">Token used to cancel the async iteration.</param>
    /// <returns>An async stream of tree items rooted at the specified path.</returns>
    public async IAsyncEnumerable<GitTreeItem> EnumerateCommitTreeAsync(
        string? reference = null,
        string? path = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var commit = await GetCommitAsync(reference, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(path))
        {
            await foreach (var item in EnumerateTreeAsync(commit.Tree, string.Empty, cancellationToken).ConfigureAwait(false))
            {
                yield return item;
            }

            yield break;
        }

        var normalized = NormalizePath(path);
        if (string.IsNullOrEmpty(normalized))
        {
            await foreach (var item in EnumerateTreeAsync(commit.Tree, string.Empty, cancellationToken).ConfigureAwait(false))
            {
                yield return item;
            }

            yield break;
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var currentTreeHash = commit.Tree;
        GitTreeEntry? entry = null;

        for (var i = 0; i < segments.Length; i++)
        {
            var currentTree = await GetTreeAsync(currentTreeHash, cancellationToken).ConfigureAwait(false);
            entry = currentTree.Entries.FirstOrDefault(e => e.Name.Equals(segments[i], StringComparison.Ordinal));
            if (entry is null)
            {
                throw new DirectoryNotFoundException($"Path '{normalized}' not found in commit {commit.Id}");
            }

            var isLast = i == segments.Length - 1;
            if (isLast)
            {
                if (entry.Kind == GitTreeEntryKind.Tree)
                {
                    await foreach (var item in EnumerateTreeAsync(entry.Hash, normalized, cancellationToken).ConfigureAwait(false))
                    {
                        yield return item;
                    }
                }
                else
                {
                    yield return new GitTreeItem(normalized, entry);
                }

                yield break;
            }

            if (entry.Kind != GitTreeEntryKind.Tree)
            {
                throw new InvalidOperationException($"Segment '{segments[i]}' is not a directory");
            }

            currentTreeHash = entry.Hash;
        }
    }

    /// <summary>
    /// Checks if a path exists in the specified commit and returns its type.
    /// </summary>
    /// <param name="path">Repository-relative path using / separators.</param>
    /// <param name="reference">Commit hash or ref to check; defaults to HEAD.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>The type of the path if it exists, or null if it does not exist.</returns>
    public async Task<GitTreeEntryKind?> GetPathTypeAsync(string path, string? reference = null, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizePathAllowEmpty(path);

        if (string.IsNullOrEmpty(normalized))
        {
            return GitTreeEntryKind.Tree;
        }

        var commit = await GetCommitAsync(reference, cancellationToken).ConfigureAwait(false);

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var currentTreeHash = commit.Tree;

        for (var i = 0; i < segments.Length; i++)
        {
            var currentTree = await GetTreeAsync(currentTreeHash, cancellationToken).ConfigureAwait(false);
            var entry = currentTree.Entries.FirstOrDefault(e => e.Name.Equals(segments[i], StringComparison.Ordinal));
            if (entry is null)
            {
                return null;
            }

            var isLast = i == segments.Length - 1;
            if (isLast)
            {
                return entry.Kind;
            }

            if (entry.Kind != GitTreeEntryKind.Tree)
            {
                return null;
            }

            currentTreeHash = entry.Hash;
        }

        return null;
    }

    /// <summary>
    /// Checks if a path exists in the specified commit.
    /// </summary>
    /// <param name="path">Repository-relative path using / separators.</param>
    /// <param name="reference">Commit hash or ref to check; defaults to HEAD.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>True if the path exists, false otherwise.</returns>
    public async Task<bool> PathExistsAsync(string path, string? reference = null, CancellationToken cancellationToken = default)
    {
        var type = await GetPathTypeAsync(path, reference, cancellationToken).ConfigureAwait(false);
        return type.HasValue;
    }

    /// <summary>
    /// Checks if a file exists at the specified path in the specified commit.
    /// </summary>
    /// <param name="filePath">Repository-relative file path using / separators.</param>
    /// <param name="reference">Commit hash or ref to check; defaults to HEAD.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>True if a file (blob) exists at the path, false otherwise.</returns>
    public async Task<bool> FileExistsAsync(string filePath, string? reference = null, CancellationToken cancellationToken = default)
    {
        var type = await GetPathTypeAsync(filePath, reference, cancellationToken).ConfigureAwait(false);
        return type == GitTreeEntryKind.Blob;
    }

    /// <summary>
    /// Checks if a directory exists at the specified path in the specified commit.
    /// </summary>
    /// <param name="directoryPath">Repository-relative directory path using / separators.</param>
    /// <param name="reference">Commit hash or ref to check; defaults to HEAD.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>True if a directory (tree) exists at the path, false otherwise.</returns>
    public async Task<bool> DirectoryExistsAsync(string directoryPath, string? reference = null, CancellationToken cancellationToken = default)
    {
        var type = await GetPathTypeAsync(directoryPath, reference, cancellationToken).ConfigureAwait(false);
        return type == GitTreeEntryKind.Tree;
    }

    /// <summary>
    /// Reads the blob content at <paramref name="filePath"/> from the specified <paramref name="reference"/>.
    /// </summary>
    /// <param name="filePath">Repository-relative file path using / separators.</param>
    /// <param name="reference">Commit hash or ref to read from; defaults to HEAD.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>The blob payload as a byte array.</returns>
    public async Task<byte[]> ReadFileAsync(string filePath, string? reference = null, CancellationToken cancellationToken = default)
    {
        return (await ReadFileAndHashAsync(filePath, reference, cancellationToken)).Content;
    }

    /// <summary>
    /// Reads the blob content and hash at <paramref name="filePath"/> from the specified <paramref name="reference"/>.
    /// </summary>
    /// <param name="filePath">Repository-relative file path using / separators.</param>
    /// <param name="reference">Commit hash or ref to read from; defaults to HEAD.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>A <see cref="GitFileContentAndHash"/> containing the blob payload and its hash.</returns>
    public async Task<GitFileContentAndHash> ReadFileAndHashAsync(string filePath, string? reference = null, CancellationToken cancellationToken = default)
    {
        var commit = await GetCommitAsync(reference, cancellationToken).ConfigureAwait(false);
        var normalized = NormalizePath(filePath);
        if (string.IsNullOrEmpty(normalized))
        {
            throw new ArgumentException("File path must reference a file", nameof(filePath));
        }

        var blobHash = await GetBlobHashAsync(commit.Tree, normalized, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException($"File '{filePath}' not found in commit {commit.Id}");

        var blob = await _objectStore.ReadObjectAsync(blobHash, cancellationToken).ConfigureAwait(false);
        if (blob.Type != GitObjectType.Blob)
        {
            throw new InvalidOperationException($"Object {blobHash} is not a blob");
        }

        return new GitFileContentAndHash(blob.Content, blobHash);
    }

    /// <summary>
    /// Reads the blob content at <paramref name="filePath"/> from the specified <paramref name="reference"/> as a stream.
    /// For loose objects, the stream reads directly from disk without buffering the entire content.
    /// For pack objects, the stream is backed by a <see cref="MemoryStream"/>.
    /// The caller is responsible for disposing the returned <see cref="GitObjectStream"/>.
    /// </summary>
    /// <param name="filePath">Repository-relative file path using / separators.</param>
    /// <param name="reference">Commit hash or ref to read from; defaults to HEAD.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>A <see cref="GitObjectStream"/> whose <see cref="GitObjectStream.Content"/> exposes the blob payload.</returns>
    public async Task<GitObjectStream> ReadFileStreamAsync(string filePath, string? reference = null, CancellationToken cancellationToken = default)
    {
        var commit = await GetCommitAsync(reference, cancellationToken).ConfigureAwait(false);
        var normalized = NormalizePath(filePath);
        if (string.IsNullOrEmpty(normalized))
        {
            throw new ArgumentException("File path must reference a file", nameof(filePath));
        }

        var blobHash = await GetBlobHashAsync(commit.Tree, normalized, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException($"File '{filePath}' not found in commit {commit.Id}");

        var stream = await _objectStore.ReadObjectStreamAsync(blobHash, cancellationToken).ConfigureAwait(false);
        if (stream.Type != GitObjectType.Blob)
        {
            stream.Dispose();
            throw new InvalidOperationException($"Object {blobHash} is not a blob");
        }

        return stream;
    }

    /// <summary>
    /// Streams commits where <paramref name="filePath"/> changed, newest first.
    /// </summary>
    /// <param name="filePath">Repository-relative file path to inspect.</param>
    /// <param name="reference">Starting reference or commit hash; defaults to HEAD.</param>
    /// <param name="cancellationToken">Token used to cancel the async iteration.</param>
    /// <returns>An async stream of commits affecting the file.</returns>
    public async IAsyncEnumerable<GitCommit> GetFileHistoryAsync(
        string filePath,
        string? reference = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var normalizedPath = NormalizePath(filePath);
        if (string.IsNullOrEmpty(normalizedPath))
        {
            throw new ArgumentException("File path must reference a file", nameof(filePath));
        }

        GitHash? lastBlob = null;
        GitCommit? lastCommitWithFile = null;
        var hasSeenFile = false;

        await foreach (var commit in EnumerateCommitsAsync(reference, cancellationToken).ConfigureAwait(false))
        {
            var blobHash = await GetBlobHashAsync(commit.Tree, normalizedPath, cancellationToken).ConfigureAwait(false);

            if (blobHash is null)
            {
                // File doesn't exist in this commit - if we were tracking it, yield the last commit where it existed
                if (hasSeenFile && lastCommitWithFile != null)
                {
                    yield return lastCommitWithFile;
                    lastCommitWithFile = null;
                    hasSeenFile = false;
                }
                lastBlob = null;
                continue;
            }

            // File exists in this commit
            if (!hasSeenFile)
            {
                // First time seeing this file in history (going backwards)
                hasSeenFile = true;
                lastCommitWithFile = commit;
                lastBlob = blobHash;
            }
            else if (!lastBlob!.Value.Equals(blobHash.Value))
            {
                // Content changed - yield the previous commit and track this new version
                if (lastCommitWithFile != null)
                {
                    yield return lastCommitWithFile;
                }
                lastCommitWithFile = commit;
                lastBlob = blobHash;
            }
            // else: same content as previous commit, just update lastCommitWithFile to track further back
            else
            {
                lastCommitWithFile = commit;
            }
        }

        // Yield the final commit where the file was created/last changed
        if (lastCommitWithFile != null)
        {
            yield return lastCommitWithFile;
        }
    }

    /// <summary>
    /// Creates a new commit on the specified branch by applying the provided operations.
    /// This method is thread-safe and prevents concurrent commits to the same branch.
    /// </summary>
    /// <param name="branchName">Branch to update (short name or fully qualified ref).</param>
    /// <param name="operations">Sequence of file-system operations to apply.</param>
    /// <param name="metadata">Commit metadata (message, author, committer).</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>The hash of the newly created commit.</returns>
    public async Task<GitHash> CreateCommitAsync(
        string branchName,
        IEnumerable<GitCommitOperation> operations,
        GitCommitMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(branchName))
        {
            throw new ArgumentException("Branch name cannot be empty", nameof(branchName));
        }

        if (operations is null)
        {
            throw new ArgumentNullException(nameof(operations));
        }

        if (metadata is null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        var referencePath = NormalizeReference(branchName);

        // Acquire lock for this branch to prevent concurrent commits
        using (await _lockManager.AcquireReferenceLockAsync(referencePath, cancellationToken).ConfigureAwait(false))
        {
            var parentHash = await TryResolveReferencePathAsync(referencePath, cancellationToken).ConfigureAwait(false);
            GitCommit? parentCommit = null;

            Dictionary<string, TreeLeaf> entries;
            if (parentHash.HasValue)
            {
                parentCommit = await GetCommitAsync(parentHash.Value, cancellationToken).ConfigureAwait(false);
                entries = await LoadLeafEntriesAsync(parentCommit.Tree, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                entries = new Dictionary<string, TreeLeaf>(StringComparer.Ordinal);
            }
            var changed = false;

            foreach (var operation in operations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (operation is null)
                {
                    throw new ArgumentException("Operations cannot contain null entries", nameof(operations));
                }

                var normalizedPath = NormalizePath(operation.Path);
                switch (operation)
                {
                    case AddFileOperation add:
                        changed |= await ApplyAddFileAsync(entries, normalizedPath, add.Content, cancellationToken).ConfigureAwait(false);
                        break;
                    case UpdateFileOperation update:
                        changed |= await ApplyUpdateFileAsync(entries, normalizedPath, update.Content, update.ExpectedPreviousHash, cancellationToken).ConfigureAwait(false);
                        break;
                    case RemoveFileOperation _:
                        changed |= ApplyRemoveFile(entries, normalizedPath);
                        break;
                    case MoveFileOperation move:
                        changed |= ApplyMoveFile(entries, normalizedPath, NormalizePath(move.DestinationPath));
                        break;
                    case AddFileStreamOperation addStream:
                        changed |= await ApplyAddFileStreamAsync(entries, normalizedPath, addStream.Content, cancellationToken).ConfigureAwait(false);
                        break;
                    case UpdateFileStreamOperation updateStream:
                        changed |= await ApplyUpdateFileStreamAsync(entries, normalizedPath, updateStream.Content, updateStream.ExpectedPreviousHash, cancellationToken).ConfigureAwait(false);
                        break;
                    default:
                        throw new NotSupportedException($"Unsupported operation type '{operation.GetType().Name}'.");
                }
            }

            if (!changed)
            {
                throw new InvalidOperationException("The requested operations do not change the repository state.");
            }

            var newTreeHash = await BuildTreeAsync(entries, cancellationToken).ConfigureAwait(false);
            if (parentCommit != null && newTreeHash.Equals(parentCommit.Tree))
            {
                throw new InvalidOperationException("The resulting tree matches the parent commit.");
            }

            var commitPayload = BuildCommitPayload(newTreeHash, parentHash, metadata);
            var commitHash = await _objectStore.WriteObjectAsync(GitObjectType.Commit, commitPayload, cancellationToken).ConfigureAwait(false);

            var parsedCommit = GitCommit.Parse(commitHash, commitPayload);
            lock (_commitLock)
            {
                _commitCache[commitHash] = parsedCommit;
            }

            await WriteReferenceWithValidationInternalAsync(referencePath, parentHash, commitHash, cancellationToken).ConfigureAwait(false);

            return commitHash;
        }
    }

    /// <summary>
    /// Clears cached git metadata so subsequent operations reflect the current repository state.
    /// </summary>
    /// <param name="clearAllData">Clears all cached data, including data that should not change on normal git operations</param>
    public void InvalidateCaches(bool clearAllData = false)
    {
        _objectStore.InvalidateCaches();

        if (clearAllData)
        {
            lock (_commitLock)
            {
                _commitCache.Clear();
            }

            lock (_treeLock)
            {
                _treeCache.Clear();
            }
        }

        Interlocked.Exchange(ref _references, CreateReferenceCache());
    }

    /// <summary>
    /// Returns a snapshot of all references stored in the repository.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>A dictionary keyed by fully qualified reference names.</returns>
    public async Task<IReadOnlyDictionary<string, GitHash>> GetReferencesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = await _references.Value.ConfigureAwait(false);
        return new Dictionary<string, GitHash>(snapshot, StringComparer.Ordinal);
    }

    /// <summary>
    /// Writes or overwrites the value of a reference file with validation.
    /// This method validates that the expected old value matches the current value before updating.
    /// </summary>
    /// <param name="referencePath">Fully qualified reference path (for example refs/heads/main).</param>
    /// <param name="expectedOldValue">Expected current hash of the reference, or null if reference should not exist.</param>
    /// <param name="newValue">New hash to persist, or null to delete the reference.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <exception cref="InvalidOperationException">Thrown when the expected old value doesn't match the current value.</exception>
    public async Task WriteReferenceWithValidationAsync(
        string referencePath,
        GitHash? expectedOldValue,
        GitHash? newValue,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeAbsoluteReferencePath(referencePath);
        using (await _lockManager.AcquireReferenceLockAsync(normalized, cancellationToken).ConfigureAwait(false))
        {
            await WriteReferenceWithValidationInternalAsync(normalized, expectedOldValue, newValue, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Internal method to write a reference with validation without acquiring locks.
    /// Locks must be acquired by the caller.
    /// </summary>
    /// <param name="normalized">The normalized form of the reference path used for resolution.</param>
    /// <param name="expectedOldValue">Expected current hash of the reference, or null if reference should not exist.</param>
    /// <param name="newValue">New hash to persist, or null to delete the reference.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <exception cref="InvalidOperationException">Thrown when the expected old value doesn't match the current value.</exception>
    internal async Task WriteReferenceWithValidationInternalAsync(
        string normalized,
        GitHash? expectedOldValue,
        GitHash? newValue,
        CancellationToken cancellationToken)
    {
        await ValidateReferenceOldValue(normalized, expectedOldValue, cancellationToken).ConfigureAwait(false);

        // Apply update
        if (newValue.HasValue)
        {
            await UpdateReferenceAsync(normalized, newValue.Value, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await DeleteReferenceInternalAsync(normalized, cancellationToken).ConfigureAwait(false);
        }

        // Invalidate reference cache to ensure subsequent reads see the updated value
        Interlocked.Exchange(ref _references, CreateReferenceCache());
    }

    /// <summary>
    /// Validates that the reference at the specified path matches the expected old value or does not exist, depending
    /// on the provided expectation.
    /// </summary>
    /// <param name="normalized">The normalized form of the reference path used for resolution.</param>
    /// <param name="expectedOldValue">The expected value of the reference. If specified, the reference must exist and match this value; if null, the
    /// reference must not exist.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task that represents the asynchronous validation operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the reference does not exist when an expected value is provided, if the reference exists but its value
    /// does not match the expected value, or if the reference exists when no value is expected.</exception>
    private async Task ValidateReferenceOldValue(string normalized, GitHash? expectedOldValue, CancellationToken cancellationToken)
    {
        var currentValue = await TryResolveReferencePathAsync(normalized, cancellationToken).ConfigureAwait(false);

        // Validate expected state
        if (expectedOldValue.HasValue)
        {
            if (!currentValue.HasValue)
            {
                throw new InvalidOperationException($"Reference '{normalized}' does not exist, but was expected to have value {expectedOldValue.Value.Value}");
            }
            if (!currentValue.Value.Equals(expectedOldValue.Value))
            {
                throw new InvalidOperationException($"Reference '{normalized}' has value {currentValue.Value.Value}, but was expected to have value {expectedOldValue.Value.Value}");
            }
        }
        else
        {
            if (currentValue.HasValue)
            {
                throw new InvalidOperationException($"Reference '{normalized}' already exists with value {currentValue.Value.Value}");
            }
        }
    }

    /// <summary>
    /// Acquires locks for multiple references in a consistent order to prevent deadlocks.
    /// This is used for batch reference updates like git push.
    /// </summary>
    /// <param name="referencePaths">Fully qualified reference paths to lock.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>A disposable lock that must be released after all operations complete.</returns>
    public async Task<IGitMultipleReferenceLocks> AcquireMultipleReferenceLocksAsync(IEnumerable<string> referencePaths, CancellationToken cancellationToken = default)
    {
        if (referencePaths is null)
        {
            throw new ArgumentNullException(nameof(referencePaths));
        }

        // Normalize all reference paths to ensure consistency
        var normalizedPaths = referencePaths.Select(NormalizeAbsoluteReferencePath).ToList();

        var lockDisposable = await _lockManager.AcquireMultipleReferenceLocksAsync(normalizedPaths, cancellationToken);

        return new GitMultipleReferenceLocks(this, normalizedPaths, lockDisposable);
    }

    /// <summary>
    /// Checks if a commit is reachable from another commit (for fast-forward validation).
    /// </summary>
    /// <param name="from">The commit to start from.</param>
    /// <param name="to">The target commit to check reachability.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>True if 'to' is reachable from 'from', false otherwise.</returns>
    public async Task<bool> IsCommitReachableAsync(GitHash from, GitHash to, CancellationToken cancellationToken = default)
    {
        if (from.Equals(to))
        {
            return true;
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<GitHash>();
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = queue.Dequeue();
            if (!visited.Add(current.Value))
            {
                continue;
            }

            if (current.Equals(to))
            {
                return true;
            }

            var commit = await GetCommitAsync(current, cancellationToken).ConfigureAwait(false);
            foreach (var parent in commit.Parents)
            {
                queue.Enqueue(parent);
            }
        }

        return false;
    }

    private Task DeleteReferenceInternalAsync(string normalizedReferencePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var refPath = Path.Combine(GitDirectory, normalizedReferencePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(refPath))
        {
            File.Delete(refPath);
        }

        Interlocked.Exchange(ref _references, CreateReferenceCache());
        return Task.CompletedTask;
    }

    private async Task<GitCommit> GetCommitAsync(GitHash hash, CancellationToken cancellationToken)
    {
        lock (_commitLock)
        {
            if (_commitCache.TryGetValue(hash, out var cached))
            {
                return cached;
            }
        }

        var data = await _objectStore.ReadObjectAsync(hash, cancellationToken).ConfigureAwait(false);
        if (data.Type != GitObjectType.Commit)
        {
            throw new InvalidOperationException($"Object {hash} is not a commit");
        }

        var commit = GitCommit.Parse(hash, data.Content);
        lock (_commitLock)
        {
            _commitCache[hash] = commit;
        }

        return commit;
    }

    private async Task<GitTree> GetTreeAsync(GitHash hash, CancellationToken cancellationToken)
    {
        lock (_treeLock)
        {
            if (_treeCache.TryGetValue(hash, out var cached))
            {
                return cached;
            }
        }

        var data = await _objectStore.ReadObjectAsync(hash, cancellationToken).ConfigureAwait(false);
        if (data.Type != GitObjectType.Tree)
        {
            throw new InvalidOperationException($"Object {hash} is not a tree");
        }

        var tree = GitTree.Parse(hash, data.Content, _objectStore.HashLengthBytes);
        lock (_treeLock)
        {
            _treeCache[hash] = tree;
        }

        return tree;
    }

    private async Task<GitHash> ResolveReferenceAsync(string? reference, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reference) || reference!.Equals("HEAD", StringComparison.OrdinalIgnoreCase))
        {
            return await ResolveHeadAsync(cancellationToken).ConfigureAwait(false);
        }

        var nonEmptyReference = reference!;
        if (GitHash.TryParse(nonEmptyReference, out var hash))
        {
            return hash;
        }

        var candidates = new[]
        {
            nonEmptyReference,
            $"refs/heads/{nonEmptyReference}",
            $"refs/tags/{nonEmptyReference}",
            $"refs/remotes/{nonEmptyReference}"
        };

        foreach (var candidate in candidates)
        {
            var resolved = await TryResolveReferencePathAsync(candidate, cancellationToken).ConfigureAwait(false);
            if (resolved.HasValue)
            {
                return resolved.Value;
            }
        }

        throw new InvalidOperationException($"Unknown reference '{reference}'");
    }

    private async Task<GitHash> ResolveHeadAsync(CancellationToken cancellationToken)
    {
        var headPath = Path.Combine(GitDirectory, "HEAD");
        if (!File.Exists(headPath))
        {
            throw new FileNotFoundException("HEAD reference not found", headPath);
        }

        var content = (await File.ReadAllTextAsync(headPath, cancellationToken).ConfigureAwait(false)).Trim();
        if (content.StartsWith("ref: ", StringComparison.Ordinal))
        {
            var target = content[5..].Trim();
            var resolved = await TryResolveReferencePathAsync(target, cancellationToken).ConfigureAwait(false);
            if (resolved.HasValue)
            {
                return resolved.Value;
            }

            throw new InvalidOperationException($"Unable to resolve ref '{target}' pointed by HEAD");
        }

        if (GitHash.TryParse(content, out var direct))
        {
            return direct;
        }

        throw new InvalidDataException("HEAD does not contain a valid reference");
    }

    private async Task<GitHash?> TryResolveReferencePathAsync(string referencePath, CancellationToken cancellationToken)
    {
        var normalized = referencePath.Replace('\\', '/');
        var refs = await _references.Value.ConfigureAwait(false);
        if (refs.TryGetValue(normalized, out var hash))
        {
            return hash;
        }

        var filePath = Path.Combine(GitDirectory, normalized.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(filePath))
        {
            var content = (await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false)).Trim();
            if (GitHash.TryParse(content, out hash))
            {
                return hash;
            }
        }

        return null;
    }

    private static string NormalizeReference(string branchName)
    {
        var trimmed = branchName.Replace('\\', '/').Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException("Branch name cannot be empty", nameof(branchName));
        }

        if (trimmed.StartsWith("refs/", StringComparison.Ordinal))
        {
            return trimmed;
        }

        if (trimmed.Equals("HEAD", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Specify a branch name instead of HEAD", nameof(branchName));
        }

        return $"refs/heads/{trimmed}";
    }

    internal static string NormalizeAbsoluteReferencePath(string referencePath)
    {
        if (string.IsNullOrWhiteSpace(referencePath))
        {
            throw new ArgumentException("Reference path cannot be empty", nameof(referencePath));
        }

        var normalized = referencePath.Replace('\\', '/').Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            throw new ArgumentException("Reference path cannot be empty", nameof(referencePath));
        }

        if (!normalized.StartsWith("refs/", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Absolute reference path must start with 'refs/', got '{referencePath}'", nameof(referencePath));
        }

        return normalized;
    }

    private async Task<Dictionary<string, TreeLeaf>> LoadLeafEntriesAsync(GitHash treeHash, CancellationToken cancellationToken)
    {
        var entries = new Dictionary<string, TreeLeaf>(StringComparer.Ordinal);
        await foreach (var item in EnumerateTreeAsync(treeHash, string.Empty, cancellationToken).ConfigureAwait(false))
        {
            if (item.Entry.Kind == GitTreeEntryKind.Tree)
            {
                continue;
            }

            entries[item.Path] = new TreeLeaf(item.Entry.Mode, item.Entry.Hash);
        }

        return entries;
    }

    private async Task<bool> ApplyAddFileStreamAsync(Dictionary<string, TreeLeaf> entries, string path, Stream content, CancellationToken cancellationToken)
    {
        if (entries.ContainsKey(path))
        {
            throw new InvalidOperationException($"File '{path}' already exists.");
        }

        ValidatePathDoesNotConflictWithDirectories(entries, path);

        var blobHash = await _objectStore.WriteObjectAsync(GitObjectType.Blob, content, cancellationToken);
        entries[path] = new TreeLeaf(RegularFileMode, blobHash);
        return true;
    }

    private async Task<bool> ApplyUpdateFileStreamAsync(Dictionary<string, TreeLeaf> entries, string path, Stream content, GitHash? expectedPreviousHash, CancellationToken cancellationToken)
    {
        if (!entries.TryGetValue(path, out var existing))
        {
            throw new FileNotFoundException($"File '{path}' does not exist.");
        }

        if (expectedPreviousHash != null && !existing.Hash.Equals(expectedPreviousHash.Value))
        {
            throw new GitFileConflictException($"File '{path}' has hash '{existing.Hash.Value}' but expected '{expectedPreviousHash.Value.Value}'.", path);
        }

        var blobHash = await _objectStore.WriteObjectAsync(GitObjectType.Blob, content, cancellationToken);
        if (existing.Hash.Equals(blobHash))
        {
            return false;
        }

        entries[path] = existing with { Hash = blobHash };
        return true;
    }

    private async Task<bool> ApplyAddFileAsync(Dictionary<string, TreeLeaf> entries, string path, byte[] content, CancellationToken cancellationToken)
    {
        if (entries.ContainsKey(path))
        {
            throw new InvalidOperationException($"File '{path}' already exists.");
        }

        ValidatePathDoesNotConflictWithDirectories(entries, path);

        var blobHash = await _objectStore.WriteObjectAsync(GitObjectType.Blob, content, cancellationToken).ConfigureAwait(false);
        entries[path] = new TreeLeaf(RegularFileMode, blobHash);
        return true;
    }

    private async Task<bool> ApplyUpdateFileAsync(Dictionary<string, TreeLeaf> entries, string path, byte[] content, GitHash? expectedPreviousHash, CancellationToken cancellationToken)
    {
        if (!entries.TryGetValue(path, out var existing))
        {
            throw new FileNotFoundException($"File '{path}' does not exist.");
        }

        if (expectedPreviousHash != null && !existing.Hash.Equals(expectedPreviousHash.Value))
        {
            throw new GitFileConflictException($"File '{path}' has hash '{existing.Hash.Value}' but expected '{expectedPreviousHash.Value.Value}'.", path);
        }

        var blobHash = await _objectStore.WriteObjectAsync(GitObjectType.Blob, content, cancellationToken).ConfigureAwait(false);
        if (existing.Hash.Equals(blobHash))
        {
            return false;
        }

        entries[path] = existing with { Hash = blobHash };
        return true;
    }

    private static bool ApplyRemoveFile(Dictionary<string, TreeLeaf> entries, string path)
    {
        if (!entries.Remove(path))
        {
            throw new FileNotFoundException($"File '{path}' does not exist.");
        }

        return true;
    }

    private static bool ApplyMoveFile(Dictionary<string, TreeLeaf> entries, string sourcePath, string destinationPath)
    {
        if (sourcePath.Equals(destinationPath, StringComparison.Ordinal))
        {
            return false;
        }

        if (!entries.TryGetValue(sourcePath, out var leaf))
        {
            throw new FileNotFoundException($"File '{sourcePath}' does not exist.");
        }

        if (entries.ContainsKey(destinationPath))
        {
            throw new InvalidOperationException($"File '{destinationPath}' already exists.");
        }

        ValidatePathDoesNotConflictWithDirectories(entries, destinationPath);

        entries.Remove(sourcePath);
        entries[destinationPath] = leaf;
        return true;
    }

    private static void ValidatePathDoesNotConflictWithDirectories(Dictionary<string, TreeLeaf> entries, string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return;
        }

        var pathBuilder = new StringBuilder();
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (i > 0)
            {
                pathBuilder.Append('/');
            }
            pathBuilder.Append(segments[i]);
            var parentPath = pathBuilder.ToString();

            if (entries.ContainsKey(parentPath))
            {
                throw new InvalidOperationException($"Cannot create file at '{path}' because '{parentPath}' is a file, not a directory.");
            }
        }

        var pathPrefix = path + "/";
        foreach (var existingPath in entries.Keys)
        {
            if (existingPath.StartsWith(pathPrefix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Cannot create file at '{path}' because a file exists under it at '{existingPath}'.");
            }
        }
    }

    private async Task<GitHash> BuildTreeAsync(IReadOnlyDictionary<string, TreeLeaf> leaves, CancellationToken cancellationToken)
    {
        var root = new TreeBuilderNode();
        foreach (var (path, leaf) in leaves)
        {
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                continue;
            }

            var current = root;
            for (var i = 0; i < segments.Length - 1; i++)
            {
                if (!current.Directories.TryGetValue(segments[i], out var child))
                {
                    child = new TreeBuilderNode();
                    current.Directories[segments[i]] = child;
                }

                current = child;
            }

            current.Leaves[segments[^1]] = leaf;
        }

        return await WriteTreeNodeAsync(root, cancellationToken).ConfigureAwait(false);
    }

    private async Task<GitHash> WriteTreeNodeAsync(TreeBuilderNode node, CancellationToken cancellationToken)
    {
        var entries = new List<TreeEntryData>();

        foreach (var (name, child) in node.Directories)
        {
            var hash = await WriteTreeNodeAsync(child, cancellationToken).ConfigureAwait(false);
            entries.Add(new TreeEntryData(name, DirectoryMode, hash));
        }

        foreach (var (name, leaf) in node.Leaves)
        {
            entries.Add(new TreeEntryData(name, leaf.Mode, leaf.Hash));
        }

        entries.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));

        using var buffer = new MemoryStream();
        foreach (var entry in entries)
        {
            var modeString = Convert.ToString(entry.Mode, 8) ?? string.Empty;
            var modeBytes = Encoding.ASCII.GetBytes(modeString);
            buffer.Write(modeBytes, 0, modeBytes.Length);
            buffer.WriteByte((byte)' ');
            var nameBytes = Encoding.UTF8.GetBytes(entry.Name);
            buffer.Write(nameBytes, 0, nameBytes.Length);
            buffer.WriteByte(0);
            var hashBytes = entry.Hash.ToByteArray();
            buffer.Write(hashBytes, 0, hashBytes.Length);
        }

        return await _objectStore.WriteObjectAsync(GitObjectType.Tree, buffer.ToArray(), cancellationToken).ConfigureAwait(false);
    }

    private static byte[] BuildCommitPayload(GitHash treeHash, GitHash? parentHash, GitCommitMetadata metadata)
    {
        var builder = new StringBuilder();
        builder.Append("tree ").Append(treeHash.Value).Append('\n');
        if (parentHash.HasValue)
        {
            builder.Append("parent ").Append(parentHash.Value.Value).Append('\n');
        }
        builder.Append("author ")
            .Append(metadata.Author.ToHeaderValue())
            .Append('\n');
        builder.Append("committer ")
            .Append(metadata.Committer.ToHeaderValue())
            .Append('\n');
        builder.Append('\n');
        builder.Append(metadata.Message);
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private async Task UpdateReferenceAsync(string referencePath, GitHash commitHash, CancellationToken cancellationToken)
    {
        var refPath = Path.Combine(GitDirectory, referencePath.Replace('/', Path.DirectorySeparatorChar));
        var directory = Path.GetDirectoryName(refPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempDirectory = directory ?? GitDirectory;
        var tempPath = Path.Combine(tempDirectory, $"{Path.GetFileName(refPath)}.{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(tempPath, commitHash.Value + "\n", cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, refPath, overwrite: true);
    }

    private async Task<Dictionary<string, GitHash>> LoadReferencesAsync()
    {
        var refs = new Dictionary<string, GitHash>(StringComparer.Ordinal);
        var refsRoot = Path.Combine(GitDirectory, "refs");
        if (Directory.Exists(refsRoot))
        {
            foreach (var file in Directory.EnumerateFiles(refsRoot, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(GitDirectory, file).Replace('\\', '/');
                var content = (await File.ReadAllTextAsync(file).ConfigureAwait(false)).Trim();
                if (GitHash.TryParse(content, out var hash))
                {
                    refs[relative] = hash;
                }
            }
        }

        var packedRefs = Path.Combine(GitDirectory, "packed-refs");
        if (File.Exists(packedRefs))
        {
            var lines = await File.ReadAllLinesAsync(packedRefs).ConfigureAwait(false);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#') || trimmed.StartsWith('^'))
                {
                    continue;
                }

                var separator = trimmed.IndexOf(' ');
                if (separator <= 0)
                {
                    continue;
                }

                var hashString = trimmed[..separator];
                var name = trimmed[(separator + 1)..];
                if (GitHash.TryParse(hashString, out var hash))
                {
                    refs[name] = hash;
                }
            }
        }

        return refs;
    }

    private Lazy<Task<Dictionary<string, GitHash>>> CreateReferenceCache()
        => new Lazy<Task<Dictionary<string, GitHash>>>(LoadReferencesAsync, LazyThreadSafetyMode.ExecutionAndPublication);

    private static string NormalizePath(string path)
    {
        var normalized = NormalizePathAllowEmpty(path);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Path cannot be empty", nameof(path));
        }

        return normalized;
    }

    private static string NormalizePathAllowEmpty(string path)
    {
        var normalized = path.Replace('\\', '/');
        normalized = normalized.Trim();
        normalized = normalized.Trim('/');
        return normalized;
    }

    private async IAsyncEnumerable<GitTreeItem> EnumerateTreeAsync(
        GitHash treeHash,
        string prefix,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var tree = await GetTreeAsync(treeHash, cancellationToken).ConfigureAwait(false);
        foreach (var entry in tree.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = string.IsNullOrEmpty(prefix) ? entry.Name : $"{prefix}/{entry.Name}";
            yield return new GitTreeItem(path, entry);
            if (entry.Kind == GitTreeEntryKind.Tree)
            {
                await foreach (var child in EnumerateTreeAsync(entry.Hash, path, cancellationToken).ConfigureAwait(false))
                {
                    yield return child;
                }
            }
        }
    }

    private async Task<GitHash?> GetBlobHashAsync(GitHash treeHash, string normalizedPath, CancellationToken cancellationToken)
    {
        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        var currentTree = await GetTreeAsync(treeHash, cancellationToken).ConfigureAwait(false);
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            var entry = currentTree.Entries.FirstOrDefault(e => e.Name.Equals(segment, StringComparison.Ordinal));
            if (entry is null)
            {
                return null;
            }

            var isLast = i == segments.Length - 1;
            if (isLast)
            {
                return entry.Kind == GitTreeEntryKind.Tree ? null : entry.Hash;
            }

            if (entry.Kind != GitTreeEntryKind.Tree)
            {
                return null;
            }

            currentTree = await GetTreeAsync(entry.Hash, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }
}
