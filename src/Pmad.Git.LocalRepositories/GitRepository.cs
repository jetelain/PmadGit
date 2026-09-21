using System.Runtime.CompilerServices;
using System.Text;
using Pmad.Git.LocalRepositories.Caching;
using Pmad.Git.LocalRepositories.Config;
using Pmad.Git.LocalRepositories.Helpers;

namespace Pmad.Git.LocalRepositories;

/// <summary>
/// High-level entry point for querying commits, trees, and blobs from a local git repository.
/// </summary>
public sealed class GitRepository : IGitRepository, IGitRepositoryCacheInvalidator
{
    private readonly GitObjectStore _objectStore;
    private readonly GitReferenceStore _referenceStore;
    private readonly Dictionary<GitHash, GitCommit> _commitCache = new();
    private readonly Dictionary<GitHash, GitTree> _treeCache = new();
    private readonly object _commitLock = new();
    private readonly object _treeLock = new();
    private readonly GitLastChangeCache _lastChangeCache;
    private const int RegularFileMode = 33188; // 100644 in octal
    private const int DirectoryMode = 16384;   // 040000 in octal

    private GitRepository(string rootPath, string gitDirectory, IGitRepositoryLockManager lockManager)
    {
        RootPath = rootPath;
        GitDirectory = gitDirectory;
        _objectStore = new GitObjectStore(gitDirectory);
        _referenceStore = new GitReferenceStore(gitDirectory, lockManager);
        _lastChangeCache = new GitLastChangeCache(gitDirectory);
    }

    /// <summary>
    /// Gets the lock manager used to synchronize reference/object writes for this repository.
    /// </summary>
    /// <remarks>
    /// Share this instance with other components (e.g. a CLI-based wrapper) operating on the same
    /// repository directory within the same process to synchronize concurrent writes.
    /// </remarks>
    public IGitRepositoryLockManager LockManager => _referenceStore.LockManager;

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
    public bool IsBare => string.Equals(RootPath, GitDirectory, StringComparison.OrdinalIgnoreCase);

    private GitIndexManager? _indexManager;

    /// <inheritdoc />
    public GitIndexManager? IndexManager => !IsBare ? (_indexManager ??= new GitIndexManager(this, RootPath)) : null;

    /// <inheritdoc />
    public IGitObjectStore ObjectStore => _objectStore;

    /// <inheritdoc />
    public IGitReferenceStore ReferenceStore => _referenceStore;

    /// <summary>
    /// Creates a new empty git repository at the specified path.
    /// </summary>
    /// <param name="path">Path where the repository should be created.</param>
    /// <param name="bare">Whether to create a bare repository (no working directory).</param>
    /// <param name="initialBranch">Name of the initial branch; defaults to "main".</param>
    /// <param name="lockManager">
    /// Optional lock manager to use. Share the same instance with other components (e.g. a CLI-based
    /// wrapper) operating on the same repository directory within the same process, to synchronize
    /// their writes. When omitted, a new dedicated lock manager is created.
    /// </param>
    /// <returns>An initialized <see cref="GitRepository"/>.</returns>
    public static GitRepository Init(string path, bool bare = false, string initialBranch = "main", IGitRepositoryLockManager? lockManager = null)
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

        return new GitRepository(rootPath, gitDirectory, lockManager ?? new GitRepositoryLockManager());
    }

    /// <summary>
    /// Opens a git repository located at <paramref name="path"/> or its parent folders.
    /// </summary>
    /// <param name="path">Path pointing to a working tree or .git directory.</param>
    /// <param name="lockManager">
    /// Optional lock manager to use. Share the same instance with other components (e.g. a CLI-based
    /// wrapper) operating on the same repository directory within the same process, to synchronize
    /// their writes. When omitted, a new dedicated lock manager is created.
    /// </param>
    /// <returns>An initialized <see cref="GitRepository"/>.</returns>
    public static GitRepository Open(string path, IGitRepositoryLockManager? lockManager = null)
    {
        lockManager ??= new GitRepositoryLockManager();
        var fullPath = Path.GetFullPath(path);
        if (Path.GetFileName(fullPath).Equals(".git", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Directory.GetParent(fullPath)?.FullName ?? throw new DirectoryNotFoundException("Unable to determine repository root");
            return new GitRepository(parent, fullPath, lockManager);
        }

        var gitDir = Path.Combine(fullPath, ".git");
        if (Directory.Exists(gitDir))
        {
            return new GitRepository(fullPath, gitDir, lockManager);
        }

        if (File.Exists(Path.Combine(fullPath, "HEAD")) && File.Exists(Path.Combine(fullPath, "config")))
        {
            return new GitRepository(fullPath, fullPath, lockManager);
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
    /// Enumerates commits reachable from <paramref name="reference"/> in reverse chronological (newest-first) order.
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
    /// <param name="searchOption">Whether to include items in all subdirectories or only the specified directory; defaults to <see cref="SearchOption.AllDirectories"/>.</param>
    /// <param name="cancellationToken">Token used to cancel the async iteration.</param>
    /// <returns>An async stream of tree items rooted at the specified path.</returns>
    public async IAsyncEnumerable<GitTreeItem> EnumerateCommitTreeAsync(
        string? reference = null,
        string? path = null,
        SearchOption searchOption = SearchOption.AllDirectories,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var commit = await GetCommitAsync(reference, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(path))
        {
            await foreach (var item in EnumerateTreeAsync(commit.Tree, string.Empty, searchOption, cancellationToken).ConfigureAwait(false))
            {
                yield return item;
            }

            yield break;
        }

        var normalized = NormalizePath(path);
        if (string.IsNullOrEmpty(normalized))
        {
            await foreach (var item in EnumerateTreeAsync(commit.Tree, string.Empty, searchOption, cancellationToken).ConfigureAwait(false))
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
                    await foreach (var item in EnumerateTreeAsync(entry.Hash, normalized, searchOption, cancellationToken).ConfigureAwait(false))
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
    /// For pack objects, the stream is backed by a <see cref="System.IO.MemoryStream"/>.
    /// The caller is responsible for disposing the returned <see cref="GitObjectStream"/> (supports both <see langword="using"/> and <see langword="await using"/>).
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
    public async IAsyncEnumerable<GitCommit> EnumerateFileHistoryAsync(
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
    /// Lists files under the optional <paramref name="path"/> in the specified <paramref name="reference"/> along with the last commit that changed each file.
    /// This is more efficient than calling <see cref="EnumerateCommitTreeAsync"/> followed by <see cref="EnumerateFileHistoryAsync"/> for each file
    /// because the commit graph is traversed only once.
    /// Results are cached on disk (one JSON file per commit) so repeated calls for the same commit are served instantly.
    /// </summary>
    /// <param name="reference">Starting reference or commit hash; defaults to HEAD.</param>
    /// <param name="path">Optional directory path to scope the result; all files when omitted. Returns an empty list if the path does not exist in the start commit.</param>
    /// <param name="searchOption">Whether to include files in all subdirectories or only the specified directory; defaults to <see cref="SearchOption.AllDirectories"/>.</param>
    /// <param name="predicate">Optional predicate applied to each file path; only files for which it returns <see langword="true"/> are included. All files are included when omitted.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>A list of <see cref="GitFileLastChange"/> entries, one per file, sorted by path in ordinal order, each pairing the file path with its most recent modifying commit.</returns>
    public async Task<IReadOnlyList<GitFileLastChange>> GetFilesWithLastChangeAsync(
        string? reference = null,
        string? path = null,
        SearchOption searchOption = SearchOption.AllDirectories,
        Func<string, bool>? predicate = null,
        CancellationToken cancellationToken = default)
    {
        var prefix = NormalizePathAllowEmpty(path);
        var startHash = await ResolveReferenceAsync(reference, cancellationToken).ConfigureAwait(false);

        var cached = await _lastChangeCache.TryReadAsync(
            startHash,
            h => GetCommitAsync(h, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        if (cached != null)
        {
            return GitFileLastChangeHelper.ApplyFilters(cached, prefix, searchOption, predicate);
        }

        // Compute the full result without path scope or predicate so the single cache entry
        // is reusable for any path, search depth, or predicate supplied by future callers.
        var fullResult = await ComputeFilesWithLastChangeAsync(startHash, cancellationToken).ConfigureAwait(false);

        await _lastChangeCache.WriteAsync(startHash, fullResult, cancellationToken).ConfigureAwait(false);

        return GitFileLastChangeHelper.ApplyFilters(fullResult, prefix, searchOption, predicate);
    }

    private async Task<IReadOnlyList<GitFileLastChange>> ComputeFilesWithLastChangeAsync(
        GitHash startHash,
        CancellationToken cancellationToken)
    {
        // file path -> blob hash recorded in the current commit (used to detect changes compared to older commits)
        var initialBlobPerFile = new Dictionary<string, GitHash>(StringComparer.Ordinal);

        // file path -> the commit we will ultimately return (the newest commit that changed it)
        var result = new Dictionary<string, GitCommit>(StringComparer.Ordinal);

        // files whose last-commit has already been finalised and no longer need tracking
        var done = new HashSet<string>(StringComparer.Ordinal);

        bool isFirst = true;

        await foreach (var commit in EnumerateCommitsAsync(startHash.Value, cancellationToken).ConfigureAwait(false))
        {
            if (isFirst)
            {
                await foreach (var item in EnumerateCommitTreeAsync(commit.Id.Value, null, SearchOption.AllDirectories, cancellationToken).ConfigureAwait(false))
                {
                    if (item.Entry.Kind != GitTreeEntryKind.Blob)
                    {
                        continue;
                    }
                    initialBlobPerFile[item.Path] = item.Entry.Hash;
                    result[item.Path] = commit;
                }
                isFirst = false;

                if (initialBlobPerFile.Count == 0)
                {
                    // No file, nothing to track or return
                    break;
                }
            }
            else
            {
                // Check if this ancestor commit already has a cached result; if so, files whose
                // blob is unchanged can be resolved directly without walking further back.
                var ancestorCache = await _lastChangeCache.TryReadRawAsync(commit.Id, cancellationToken).ConfigureAwait(false);

                var seen = new HashSet<string>(StringComparer.Ordinal);

                await foreach (var item in EnumerateCommitTreeAsync(commit.Id.Value, null, SearchOption.AllDirectories, cancellationToken).ConfigureAwait(false))
                {
                    if (item.Entry.Kind == GitTreeEntryKind.Blob && result.ContainsKey(item.Path) && !done.Contains(item.Path))
                    {
                        if (initialBlobPerFile.TryGetValue(item.Path, out var previousHash))
                        {
                            // File has interest
                            if (!previousHash.Equals(item.Entry.Hash))
                            {
                                // File has changed content compared to the previous (newer) commit
                                // newer commit is the last one that changed it, so we can finalise the result for this file and stop tracking it
                                done.Add(item.Path);
                            }
                            else if (ancestorCache != null && ancestorCache.TryGetValue(item.Path, out var cachedHash))
                            {
                                // Same blob and this ancestor has a cached result: the cached last-change
                                // commit is valid for the current traversal too, so finalise immediately.
                                result[item.Path] = await GetCommitAsync(new GitHash(cachedHash), cancellationToken).ConfigureAwait(false);
                                done.Add(item.Path);
                                seen.Add(item.Path);
                            }
                            else
                            {
                                // Update the commit for this file to the current (older) commit, as it is still the same blob
                                result[item.Path] = commit;
                                seen.Add(item.Path);
                            }
                        }
                    }
                }

                foreach (var missingPath in initialBlobPerFile.Keys.Where(p => !seen.Contains(p) && !done.Contains(p)))
                {
                    // File that existed in the previous (newer) commit is not present in this older commit, meaning this commit predates the file's introduction
                    // The newer commit is therefore the last one that affected it, so we can finalise the result for this file and stop tracking it
                    done.Add(missingPath);
                }

                // All known files are finalised - no older commit can affect the result.
                if (done.Count == initialBlobPerFile.Count)
                {
                    break;
                }
            }
        }

        return BuildResult(result);

        static IReadOnlyList<GitFileLastChange> BuildResult(Dictionary<string, GitCommit> dict)
            => dict.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => new GitFileLastChange(kv.Key, kv.Value)).ToList();
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
        using (await _referenceStore.AcquireReferenceLockAsync(referencePath, cancellationToken).ConfigureAwait(false))
        {
            var parentHash = await _referenceStore.TryResolveReferenceAsync(referencePath, cancellationToken).ConfigureAwait(false);
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

            await _referenceStore.WriteReferenceWithValidationInternalAsync(referencePath, parentHash, commitHash, cancellationToken).ConfigureAwait(false);

            Changed?.Invoke(this, EventArgs.Empty);

            return commitHash;
        }
    }

    /// <inheritdoc />
    public async Task<GitHash> AmendCommitAsync(
        string branchName,
        IEnumerable<GitCommitOperation> operations,
        GitCommitMetadata? metadata = null,
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

        var referencePath = NormalizeReference(branchName);

        using (await _referenceStore.AcquireReferenceLockAsync(referencePath, cancellationToken).ConfigureAwait(false))
        {
            var headHash = await _referenceStore.TryResolveReferenceAsync(referencePath, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Cannot amend commit on branch '{branchName}' because the branch does not exist or has no commits.");

            var currentCommit = await GetCommitAsync(headHash, cancellationToken).ConfigureAwait(false);
            var entries = await LoadLeafEntriesAsync(currentCommit.Tree, cancellationToken).ConfigureAwait(false);

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
                        await ApplyAddFileAsync(entries, normalizedPath, add.Content, cancellationToken).ConfigureAwait(false);
                        break;
                    case UpdateFileOperation update:
                        await ApplyUpdateFileAsync(entries, normalizedPath, update.Content, update.ExpectedPreviousHash, cancellationToken).ConfigureAwait(false);
                        break;
                    case RemoveFileOperation _:
                        ApplyRemoveFile(entries, normalizedPath);
                        break;
                    case MoveFileOperation move:
                        ApplyMoveFile(entries, normalizedPath, NormalizePath(move.DestinationPath));
                        break;
                    case AddFileStreamOperation addStream:
                        await ApplyAddFileStreamAsync(entries, normalizedPath, addStream.Content, cancellationToken).ConfigureAwait(false);
                        break;
                    case UpdateFileStreamOperation updateStream:
                        await ApplyUpdateFileStreamAsync(entries, normalizedPath, updateStream.Content, updateStream.ExpectedPreviousHash, cancellationToken).ConfigureAwait(false);
                        break;
                    default:
                        throw new NotSupportedException($"Unsupported operation type '{operation.GetType().Name}'.");
                }
            }

            var committer = new GitCommitSignature(
                currentCommit.Metadata.Committer.Name,
                currentCommit.Metadata.Committer.Email,
                DateTimeOffset.UtcNow);

            var effectiveMetadata = metadata ?? new GitCommitMetadata(
                currentCommit.Metadata.Message,
                currentCommit.Metadata.Author,
                committer);

            var newTreeHash = await BuildTreeAsync(entries, cancellationToken).ConfigureAwait(false);

            var commitPayload = BuildCommitPayload(newTreeHash, currentCommit.Parents, effectiveMetadata);
            var commitHash = await _objectStore.WriteObjectAsync(GitObjectType.Commit, commitPayload, cancellationToken).ConfigureAwait(false);

            var parsedCommit = GitCommit.Parse(commitHash, commitPayload);
            lock (_commitLock)
            {
                _commitCache[commitHash] = parsedCommit;
            }

            await _referenceStore.WriteReferenceWithValidationInternalAsync(referencePath, headHash, commitHash, cancellationToken).ConfigureAwait(false);

            Changed?.Invoke(this, EventArgs.Empty);

            return commitHash;
        }
    }

    /// <inheritdoc />
    public async Task<GitHash> SquashCommitsAsync(
        string branchName,
        GitHash baseCommitHash,
        GitCommitMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(branchName))
        {
            throw new ArgumentException("Branch name cannot be empty", nameof(branchName));
        }

        if (metadata is null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        var referencePath = NormalizeReference(branchName);

        using (await _referenceStore.AcquireReferenceLockAsync(referencePath, cancellationToken).ConfigureAwait(false))
        {
            var headHash = await _referenceStore.TryResolveReferenceAsync(referencePath, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Cannot squash commits on branch '{branchName}' because the branch does not exist or has no commits.");

            if (headHash.Equals(baseCommitHash))
            {
                throw new InvalidOperationException("Cannot squash commits: branch HEAD is already at base commit.");
            }

            var isReachable = await IsCommitReachableAsync(headHash, baseCommitHash, cancellationToken).ConfigureAwait(false);
            if (!isReachable)
            {
                throw new ArgumentException($"Base commit '{baseCommitHash.Value}' is not an ancestor of branch HEAD '{headHash.Value}'.", nameof(baseCommitHash));
            }

            var headCommit = await GetCommitAsync(headHash, cancellationToken).ConfigureAwait(false);

            var commitPayload = BuildCommitPayload(headCommit.Tree, new[] { baseCommitHash }, metadata);
            var commitHash = await _objectStore.WriteObjectAsync(GitObjectType.Commit, commitPayload, cancellationToken).ConfigureAwait(false);

            var parsedCommit = GitCommit.Parse(commitHash, commitPayload);
            lock (_commitLock)
            {
                _commitCache[commitHash] = parsedCommit;
            }

            await _referenceStore.WriteReferenceWithValidationInternalAsync(referencePath, headHash, commitHash, cancellationToken).ConfigureAwait(false);

            Changed?.Invoke(this, EventArgs.Empty);

            return commitHash;
        }
    }

    /// <summary>
    /// Clears cached git metadata so subsequent operations reflect the current repository state.
    /// </summary>
    /// <param name="clearAllData">When <see langword="true"/>, clears all cached data including structural metadata
    /// (e.g. pack index). When <see langword="false"/>, only volatile data such as references and loose objects are cleared.</param>
    /// <param name="raiseChanged">When <see langword="true"/> (the default), raises <see cref="Changed"/> after
    /// clearing the caches. Pass <see langword="false"/> when the invalidation is only meant to refresh this
    /// instance's view of the repository (e.g. before a read-only operation) and does not represent an actual
    /// modification, to avoid spurious <see cref="Changed"/> notifications.</param>
    public void InvalidateCaches(bool clearAllData = false, bool raiseChanged = true)
    {
        _objectStore.InvalidateCaches();
        _referenceStore.InvalidateCaches();

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

        if (raiseChanged)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

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

    /// <inheritdoc />
    public async Task<IReadOnlyList<GitTreeChange>> CompareTreesAsync(
        GitHash oldTreeHash,
        GitHash newTreeHash,
        CancellationToken cancellationToken = default)
    {
        if (oldTreeHash.Equals(newTreeHash))
        {
            return Array.Empty<GitTreeChange>();
        }

        var oldLeaves = await LoadLeafEntriesAsync(oldTreeHash, cancellationToken).ConfigureAwait(false);
        var newLeaves = await LoadLeafEntriesAsync(newTreeHash, cancellationToken).ConfigureAwait(false);

        var changes = new List<GitTreeChange>();
        foreach (var (path, newLeaf) in newLeaves)
        {
            if (!oldLeaves.TryGetValue(path, out var oldLeaf))
            {
                changes.Add(new GitTreeChange(path, GitChangeKind.Added, null, newLeaf.Hash));
            }
            else if (!oldLeaf.Hash.Equals(newLeaf.Hash) || oldLeaf.Mode != newLeaf.Mode)
            {
                changes.Add(new GitTreeChange(path, GitChangeKind.Modified, oldLeaf.Hash, newLeaf.Hash));
            }
        }

        foreach (var (path, oldLeaf) in oldLeaves)
        {
            if (!newLeaves.ContainsKey(path))
            {
                changes.Add(new GitTreeChange(path, GitChangeKind.Deleted, oldLeaf.Hash, null));
            }
        }

        changes.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.Ordinal));
        return changes;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GitTreeChange>> GetCommitChangesAsync(
        GitHash commitHash,
        CancellationToken cancellationToken = default)
    {
        var commit = await GetCommitAsync(commitHash, cancellationToken).ConfigureAwait(false);
        if (commit.Parents.Count == 0)
        {
            var leaves = await LoadLeafEntriesAsync(commit.Tree, cancellationToken).ConfigureAwait(false);
            var changes = new List<GitTreeChange>(leaves.Count);
            foreach (var (path, leaf) in leaves)
            {
                changes.Add(new GitTreeChange(path, GitChangeKind.Added, null, leaf.Hash));
            }
            changes.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.Ordinal));
            return changes;
        }

        var parentCommit = await GetCommitAsync(commit.Parents[0], cancellationToken).ConfigureAwait(false);
        return await CompareTreesAsync(parentCommit.Tree, commit.Tree, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GitTreeChange>> GetCommitChangesAsync(
        string? reference = null,
        CancellationToken cancellationToken = default)
    {
        var hash = await ResolveReferenceAsync(reference, cancellationToken).ConfigureAwait(false);
        return await GetCommitChangesAsync(hash, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<string?> GetCurrentBranchNameAsync(CancellationToken cancellationToken = default)
        => _referenceStore.GetCurrentBranchNameAsync(cancellationToken);

    /// <inheritdoc />
    public Task<bool> IsHeadDetachedAsync(CancellationToken cancellationToken = default)
        => _referenceStore.IsHeadDetachedAsync(cancellationToken);

    /// <inheritdoc />
    public async Task CreateReferenceAsync(
        string referencePath,
        GitHash targetCommit,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        await _referenceStore.CreateReferenceAsync(referencePath, targetCommit, overwrite, cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, GitHash>> GetReferencesByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default)
        => _referenceStore.GetReferencesByPrefixAsync(prefix, cancellationToken);

    /// <inheritdoc />
    public async Task DeleteReferenceAsync(
        string referencePath,
        CancellationToken cancellationToken = default)
    {
        await _referenceStore.DeleteReferenceAsync(referencePath, cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
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
            return await _referenceStore.ResolveHeadAsync(cancellationToken).ConfigureAwait(false);
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
            var resolved = await _referenceStore.TryResolveReferenceAsync(candidate, cancellationToken).ConfigureAwait(false);
            if (resolved.HasValue)
            {
                return resolved.Value;
            }
        }

        throw new InvalidOperationException($"Unknown reference '{reference}'");
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

    private async Task<Dictionary<string, TreeLeaf>> LoadLeafEntriesAsync(GitHash treeHash, CancellationToken cancellationToken)
    {
        var entries = new Dictionary<string, TreeLeaf>(StringComparer.Ordinal);
        await foreach (var item in EnumerateTreeAsync(treeHash, string.Empty, SearchOption.AllDirectories, cancellationToken).ConfigureAwait(false))
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

    /// <inheritdoc />
    public async Task<GitHash> WriteTreeAsync(GitIndex index, CancellationToken cancellationToken = default)
    {
        if (index is null)
        {
            throw new ArgumentNullException(nameof(index));
        }

        if (index.Entries.Any(e => e.Stage > 0))
        {
            throw new InvalidOperationException("Cannot write tree from an unmerged index with conflicted entries.");
        }

        var leaves = index.Entries.Where(e => e.Stage == 0)
            .ToDictionary(e => e.Path, e => new TreeLeaf(e.FileMode, e.Hash), StringComparer.Ordinal);
        return await BuildTreeAsync(leaves, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<GitHash> BuildTreeAsync(IReadOnlyDictionary<string, TreeLeaf> leaves, CancellationToken cancellationToken)
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

        entries.Sort(CompareTreeEntries);

        using var buffer = new MemoryStream();
        foreach (var entry in entries)
        {
            var modeString = Convert.ToString(entry.Mode, 8) ?? string.Empty;
            var modeBytes = Encoding.ASCII.GetBytes(modeString);
            buffer.Write(modeBytes, 0, modeBytes.Length);
            buffer.WriteByte((byte)' ');
            var nameBytes = entry.EncodedName;
            buffer.Write(nameBytes, 0, nameBytes.Length);
            buffer.WriteByte(0);
            var hashBytes = entry.Hash.ToByteArray();
            buffer.Write(hashBytes, 0, hashBytes.Length);
        }

        return await _objectStore.WriteObjectAsync(GitObjectType.Tree, buffer.ToArray(), cancellationToken).ConfigureAwait(false);
    }

    internal static int CompareTreeEntries(TreeEntryData left, TreeEntryData right)
    {
        return CompareTreeEntryNames(left.EncodedName, left.Mode == DirectoryMode, right.EncodedName, right.Mode == DirectoryMode);
    }

    internal static int CompareTreeEntryNames(ReadOnlySpan<byte> name1, bool isDir1, ReadOnlySpan<byte> name2, bool isDir2)
    {
        var minLen = Math.Min(name1.Length, name2.Length);
        for (var i = 0; i < minLen; i++)
        {
            var b1 = name1[i];
            var b2 = name2[i];
            if (b1 != b2)
            {
                return b1.CompareTo(b2);
            }
        }

        var c1 = name1.Length > minLen ? name1[minLen] : (isDir1 ? (byte)'/' : (byte)0);
        var c2 = name2.Length > minLen ? name2[minLen] : (isDir2 ? (byte)'/' : (byte)0);

        return c1.CompareTo(c2);
    }

    internal static byte[] BuildCommitPayload(GitHash treeHash, IEnumerable<GitHash> parents, GitCommitMetadata metadata)
    {
        var builder = new StringBuilder();
        builder.Append("tree ").Append(treeHash.Value).Append('\n');
        foreach (var parent in parents)
        {
            builder.Append("parent ").Append(parent.Value).Append('\n');
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

    private static byte[] BuildCommitPayload(GitHash treeHash, GitHash? parentHash, GitCommitMetadata metadata)
    {
        return BuildCommitPayload(treeHash, parentHash.HasValue ? new[] { parentHash.Value } : Array.Empty<GitHash>(), metadata);
    }

    private static string NormalizePath(string path)
    {
        var normalized = NormalizePathAllowEmpty(path);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Path cannot be empty", nameof(path));
        }

        return normalized;
    }

    private static string NormalizePathAllowEmpty(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }
        var normalized = path.Replace('\\', '/');
        normalized = normalized.Trim();
        normalized = normalized.Trim('/');
        return normalized;
    }

    private async IAsyncEnumerable<GitTreeItem> EnumerateTreeAsync(
        GitHash treeHash,
        string prefix,
        SearchOption searchOption,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var tree = await GetTreeAsync(treeHash, cancellationToken).ConfigureAwait(false);
        foreach (var entry in tree.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = string.IsNullOrEmpty(prefix) ? entry.Name : $"{prefix}/{entry.Name}";
            yield return new GitTreeItem(path, entry);
            if (entry.Kind == GitTreeEntryKind.Tree && searchOption == SearchOption.AllDirectories)
            {
                await foreach (var child in EnumerateTreeAsync(entry.Hash, path, searchOption, cancellationToken).ConfigureAwait(false))
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

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> GetBranchesAsync(bool includeRemote = false, CancellationToken cancellationToken = default)
    {
        var localRefs = await _referenceStore.GetReferencesByPrefixAsync("refs/heads/", cancellationToken).ConfigureAwait(false);
        var result = new List<string>(localRefs.Keys.Select(k => k["refs/heads/".Length..]));

        if (includeRemote)
        {
            var remoteRefs = await _referenceStore.GetReferencesByPrefixAsync("refs/remotes/", cancellationToken).ConfigureAwait(false);
            result.AddRange(remoteRefs.Keys.Select(k => k["refs/remotes/".Length..]));
        }

        result.Sort(StringComparer.Ordinal);
        return result;
    }

    /// <inheritdoc/>
    public async Task CreateBranchAsync(string branchName, string? startPoint = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);

        GitHash targetCommit;
        if (startPoint == null)
        {
            var head = await GetCommitAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            targetCommit = head.Id;
        }
        else
        {
            var commit = await GetCommitAsync(startPoint, cancellationToken).ConfigureAwait(false);
            targetCommit = commit.Id;
        }

        var refPath = branchName.StartsWith("refs/heads/", StringComparison.Ordinal)
            ? branchName
            : $"refs/heads/{branchName}";

        await _referenceStore.CreateReferenceAsync(refPath, targetCommit, overwrite: false, cancellationToken).ConfigureAwait(false);
        InvalidateCaches(raiseChanged: true);
    }

    /// <inheritdoc/>
    public async Task RenameBranchAsync(string oldName, string newName, CancellationToken cancellationToken = default)
    {
        await _referenceStore.RenameBranchAsync(oldName, newName, cancellationToken).ConfigureAwait(false);
        InvalidateCaches(raiseChanged: true);
    }

    /// <inheritdoc/>
    public async Task DeleteBranchAsync(string branchName, bool force = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);
        var normalizedBranch = branchName.Replace('\\', '/').Trim();
        if (normalizedBranch.StartsWith("refs/heads/", StringComparison.Ordinal))
        {
            normalizedBranch = normalizedBranch["refs/heads/".Length..];
        }

        if (!force)
        {
            var branchCommit = await _referenceStore.TryResolveReferenceAsync($"refs/heads/{normalizedBranch}", cancellationToken).ConfigureAwait(false);
            if (branchCommit.HasValue)
            {
                var headCommit = await _referenceStore.ResolveHeadAsync(cancellationToken).ConfigureAwait(false);
                var isMerged = await IsCommitReachableAsync(from: headCommit, to: branchCommit.Value, cancellationToken).ConfigureAwait(false);
                if (!isMerged)
                {
                    throw new InvalidOperationException($"The branch '{normalizedBranch}' is not fully merged.");
                }
            }
        }

        await _referenceStore.DeleteBranchAsync(normalizedBranch, cancellationToken).ConfigureAwait(false);
        InvalidateCaches(raiseChanged: true);
    }

    /// <inheritdoc/>
    public async Task<GitTrackingStatus> GetTrackingStatusAsync(string? branch = null, CancellationToken cancellationToken = default)
    {
        var localBranch = branch ?? await _referenceStore.GetCurrentBranchNameAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(localBranch))
        {
            if (branch != null)
            {
                throw new ArgumentException($"Branch '{branch}' does not exist.", nameof(branch));
            }
            throw new InvalidOperationException("No current branch is checked out (HEAD is detached).");
        }

        if (localBranch.StartsWith("refs/heads/", StringComparison.Ordinal))
        {
            localBranch = localBranch["refs/heads/".Length..];
        }

        var localRef = $"refs/heads/{localBranch}";
        var localCommitHash = await _referenceStore.TryResolveReferenceAsync(localRef, cancellationToken).ConfigureAwait(false);
        if (!localCommitHash.HasValue)
        {
            throw new ArgumentException($"Branch '{localBranch}' does not exist.", nameof(branch));
        }

        var configPath = Path.Combine(GitDirectory, "config");
        var config = await GitConfigFile.ReadFromFileAsync(configPath, cancellationToken).ConfigureAwait(false);

        var remoteName = config.GetValue("branch", localBranch, "remote");
        var mergeRef = config.GetValue("branch", localBranch, "merge");

        if (string.IsNullOrEmpty(remoteName) || string.IsNullOrEmpty(mergeRef))
        {
            return new GitTrackingStatus(localBranch, null, 0, 0);
        }

        string upstreamRef;
        string userFacingUpstreamName;
        if (mergeRef.StartsWith("refs/heads/", StringComparison.Ordinal))
        {
            var remoteBranchName = mergeRef["refs/heads/".Length..];
            upstreamRef = $"refs/remotes/{remoteName}/{remoteBranchName}";
            userFacingUpstreamName = $"{remoteName}/{remoteBranchName}";
        }
        else
        {
            upstreamRef = $"refs/remotes/{remoteName}/{mergeRef}";
            userFacingUpstreamName = $"{remoteName}/{mergeRef}";
        }

        var upstreamCommitHash = await _referenceStore.TryResolveReferenceAsync(upstreamRef, cancellationToken).ConfigureAwait(false);
        if (!upstreamCommitHash.HasValue)
        {
            return new GitTrackingStatus(localBranch, userFacingUpstreamName, 0, 0);
        }

        if (localCommitHash.Value.Equals(upstreamCommitHash.Value))
        {
            return new GitTrackingStatus(localBranch, userFacingUpstreamName, 0, 0);
        }

        var (ahead, behind) = await CountAheadBehindAsync(localCommitHash.Value, upstreamCommitHash.Value, cancellationToken).ConfigureAwait(false);
        return new GitTrackingStatus(localBranch, userFacingUpstreamName, ahead, behind);
    }

    private async Task<(int Ahead, int Behind)> CountAheadBehindAsync(GitHash local, GitHash upstream, CancellationToken cancellationToken)
    {
        if (local.Equals(upstream))
        {
            return (0, 0);
        }

        var localReachable = new HashSet<GitHash>();
        var upstreamReachable = new HashSet<GitHash>();

        var localQueue = new Queue<GitHash>();
        var upstreamQueue = new Queue<GitHash>();

        localQueue.Enqueue(local);
        localReachable.Add(local);

        upstreamQueue.Enqueue(upstream);
        upstreamReachable.Add(upstream);

        while (localQueue.Count > 0 || upstreamQueue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (localQueue.Count > 0)
            {
                var curLocal = localQueue.Dequeue();
                if (!upstreamReachable.Contains(curLocal))
                {
                    var c = await GetCommitAsync(curLocal, cancellationToken).ConfigureAwait(false);
                    foreach (var p in c.Parents)
                    {
                        if (localReachable.Add(p))
                        {
                            localQueue.Enqueue(p);
                        }
                    }
                }
            }

            if (upstreamQueue.Count > 0)
            {
                var curUpstream = upstreamQueue.Dequeue();
                if (!localReachable.Contains(curUpstream))
                {
                    var c = await GetCommitAsync(curUpstream, cancellationToken).ConfigureAwait(false);
                    foreach (var p in c.Parents)
                    {
                        if (upstreamReachable.Add(p))
                        {
                            upstreamQueue.Enqueue(p);
                        }
                    }
                }
            }
        }

        var ahead = 0;
        foreach (var h in localReachable)
        {
            if (!upstreamReachable.Contains(h))
            {
                ahead++;
            }
        }

        var behind = 0;
        foreach (var h in upstreamReachable)
        {
            if (!localReachable.Contains(h))
            {
                behind++;
            }
        }

        return (ahead, behind);
    }

    /// <inheritdoc/>
    public async Task<bool> IsCommitPushedAsync(GitHash commitHash, string? remoteBranch = null, CancellationToken cancellationToken = default)
    {
        if (remoteBranch != null)
        {
            var refPath = remoteBranch.StartsWith("refs/remotes/", StringComparison.Ordinal)
                ? remoteBranch
                : $"refs/remotes/{remoteBranch}";

            var targetCommit = await _referenceStore.TryResolveReferenceAsync(refPath, cancellationToken).ConfigureAwait(false);
            if (!targetCommit.HasValue)
            {
                return false;
            }

            return await IsCommitReachableAsync(from: targetCommit.Value, to: commitHash, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var remoteRefs = await _referenceStore.GetReferencesByPrefixAsync("refs/remotes/", cancellationToken).ConfigureAwait(false);
            foreach (var remoteRef in remoteRefs.Values)
            {
                if (await IsCommitReachableAsync(from: remoteRef, to: commitHash, cancellationToken).ConfigureAwait(false))
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<string?> GetConfigAsync(string key, bool global = false, CancellationToken cancellationToken = default)
    {
        var configPath = GetConfigFilePath(global);
        if (!File.Exists(configPath))
        {
            return null;
        }

        var config = await GitConfigFile.ReadFromFileAsync(configPath, cancellationToken).ConfigureAwait(false);
        return config.GetValue(key);
    }

    /// <inheritdoc/>
    public async Task SetConfigAsync(string key, string value, bool global = false, CancellationToken cancellationToken = default)
    {
        var configPath = GetConfigFilePath(global);
        IDisposable? writeLock = null;
        if (!global)
        {
            writeLock = await _referenceStore.LockManager.LockAllAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var config = await GitConfigFile.ReadFromFileAsync(configPath, cancellationToken).ConfigureAwait(false);
            config.SetValue(key, value);
            await config.WriteToFileAsync(configPath, cancellationToken).ConfigureAwait(false);

            if (!global)
            {
                InvalidateCaches(raiseChanged: false);
            }
        }
        finally
        {
            writeLock?.Dispose();
        }
    }

    /// <inheritdoc/>
    public async Task UnsetConfigAsync(string key, bool global = false, CancellationToken cancellationToken = default)
    {
        var configPath = GetConfigFilePath(global);
        if (!File.Exists(configPath))
        {
            return;
        }

        IDisposable? writeLock = null;
        if (!global)
        {
            writeLock = await _referenceStore.LockManager.LockAllAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var config = await GitConfigFile.ReadFromFileAsync(configPath, cancellationToken).ConfigureAwait(false);
            if (config.UnsetValue(key))
            {
                await config.WriteToFileAsync(configPath, cancellationToken).ConfigureAwait(false);
                if (!global)
                {
                    InvalidateCaches(raiseChanged: false);
                }
            }
        }
        finally
        {
            writeLock?.Dispose();
        }
    }

    private string GetConfigFilePath(bool global)
    {
        if (global)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gitconfig");
        }

        return Path.Combine(GitDirectory, "config");
    }
}
