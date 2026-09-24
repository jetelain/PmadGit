using System.IO;
using Pmad.Git.LocalRepositories;
using Pmad.Git.LocalRepositories.Config;
using Pmad.Git.Protocol.Pack;

namespace Pmad.Git.RemoteClient;

/// <summary>
/// Implements <see cref="IGitRepositoryWithRemote"/> using a pure managed Smart HTTP client
/// without any external Git CLI dependency.
/// </summary>
public sealed class GitRemoteClientRepository : IGitRepositoryWithRemote, IDisposable
{
    private readonly IGitRepository _repo;
    private readonly IGitWorkspaceRepository? _workspaceRepo;
    private readonly GitHttpConnection _connection;
    private readonly bool _disposeConnection;

    /// <inheritdoc />
    public string RootPath => _repo.RootPath;

    /// <summary>
    /// Gets the underlying local Git repository.
    /// </summary>
    public IGitRepository LocalRepository => _repo;

    /// <summary>
    /// Gets the underlying workspace repository, or <see langword="null"/> if opened on a bare repository.
    /// </summary>
    public IGitWorkspaceRepository? WorkspaceRepository => _workspaceRepo;

    /// <summary>
    /// Gets the default remote URL configured for this repository, if any.
    /// </summary>
    public string? DefaultRemoteUrl { get; }

    /// <summary>
    /// Gets the client configuration options.
    /// </summary>
    public GitRemoteClientOptions Options { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="GitRemoteClientRepository"/> class using an active workspace repository.
    /// </summary>
    /// <param name="workspaceRepository">The workspace repository with active index and working tree.</param>
    /// <param name="defaultRemoteUrl">Optional default remote URL.</param>
    /// <param name="options">Optional client configuration options.</param>
    public GitRemoteClientRepository(
        IGitWorkspaceRepository workspaceRepository,
        string? defaultRemoteUrl = null,
        GitRemoteClientOptions? options = null)
        : this((IGitRepository)workspaceRepository, defaultRemoteUrl, options)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GitRemoteClientRepository"/> class.
    /// </summary>
    /// <param name="repository">The local Git repository.</param>
    /// <param name="defaultRemoteUrl">Optional default remote URL.</param>
    /// <param name="options">Optional client configuration options.</param>
    public GitRemoteClientRepository(
        IGitRepository repository,
        string? defaultRemoteUrl = null,
        GitRemoteClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(repository);

        _repo = repository;
        if (repository is IGitWorkspaceRepository ws)
        {
            _workspaceRepo = ws;
        }
        else if (repository is GitRepository gitRepo && !gitRepo.IsBare)
        {
            _workspaceRepo = new GitRepositoryWithIndexAndWorkspace(gitRepo);
        }

        DefaultRemoteUrl = defaultRemoteUrl;
        Options = options ?? new GitRemoteClientOptions();
        _connection = new GitHttpConnection(Options);
        _disposeConnection = true;
    }

    internal GitRemoteClientRepository(
        IGitRepository repository,
        GitHttpConnection connection,
        string? defaultRemoteUrl = null,
        GitRemoteClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(connection);

        _repo = repository;
        if (repository is IGitWorkspaceRepository ws)
        {
            _workspaceRepo = ws;
        }
        else if (repository is GitRepository gitRepo && !gitRepo.IsBare)
        {
            _workspaceRepo = new GitRepositoryWithIndexAndWorkspace(gitRepo);
        }

        DefaultRemoteUrl = defaultRemoteUrl;
        Options = options ?? new GitRemoteClientOptions();
        _connection = connection;
        _disposeConnection = false;
    }

    /// <summary>
    /// Clones a remote Git repository into the specified local directory and checks out HEAD.
    /// </summary>
    /// <param name="remoteUrl">The remote Git repository URL.</param>
    /// <param name="targetPath">The local target directory to clone into.</param>
    /// <param name="options">Optional client configuration options.</param>
    /// <param name="branch">Optional branch name to check out (defaults to the remote's default branch).</param>
    /// <param name="remoteName">Optional remote name (defaults to <c>origin</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A new <see cref="GitRemoteClientRepository"/> instance wrapping the cloned repository.</returns>
    public static async Task<GitRemoteClientRepository> CloneAsync(
        string remoteUrl,
        string targetPath,
        GitRemoteClientOptions? options = null,
        string? branch = null,
        string? remoteName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        if (remoteName != null)
        {
            ValidateRemoteName(remoteName, nameof(remoteName));
        }

        remoteName ??= "origin";

        if (!Uri.TryCreate(remoteUrl, UriKind.Absolute, out var remoteUri) ||
            (remoteUri.Scheme != Uri.UriSchemeHttp && remoteUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new NotSupportedException($"Unsupported remote URL '{FormatDiagnosticUrl(remoteUrl)}'. The managed Git remote client only supports HTTP and HTTPS protocols.");
        }

        if (!string.IsNullOrEmpty(branch))
        {
            ValidateBranchName(branch);
            if (branch.StartsWith("refs/", StringComparison.Ordinal) && !branch.StartsWith("refs/heads/", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Cannot clone reference '{branch}'. Only branch names or 'refs/heads/<name>' are supported.", nameof(branch));
            }
        }

        var clientOptions = options != null
            ? new GitRemoteClientOptions
            {
                Credentials = options.Credentials,
                HttpClient = options.HttpClient,
                Agent = options.Agent,
                Timeout = options.Timeout,
                OnProgress = options.OnProgress,
                SanitizeRemoteUrlInConfig = options.SanitizeRemoteUrlInConfig
            }
            : new GitRemoteClientOptions();

        if (clientOptions.Credentials == null && !string.IsNullOrEmpty(remoteUri.UserInfo))
        {
            var userInfo = remoteUri.UserInfo;
            var colonIndex = userInfo.IndexOf(':');
            if (colonIndex >= 0)
            {
                var username = Uri.UnescapeDataString(userInfo[..colonIndex]);
                var password = Uri.UnescapeDataString(userInfo[(colonIndex + 1)..]);
                clientOptions.Credentials = GitHttpCredentials.Basic(username, password);
            }
            else
            {
                var username = Uri.UnescapeDataString(userInfo);
                clientOptions.Credentials = GitHttpCredentials.Basic(username, string.Empty);
            }
        }

        using var connection = new GitHttpConnection(clientOptions);

        var advertisement = await connection.DiscoverReferencesAsync(remoteUri, "git-upload-pack", cancellationToken).ConfigureAwait(false);

        // Determine remote's default branch from advertisement
        string? remoteDefaultBranch = null;
        if (!string.IsNullOrEmpty(advertisement.HeadSymrefTarget) &&
            advertisement.HeadSymrefTarget.StartsWith("refs/heads/", StringComparison.Ordinal))
        {
            remoteDefaultBranch = advertisement.HeadSymrefTarget["refs/heads/".Length..];
        }
        else if (advertisement.HeadHash.HasValue)
        {
            var matchingRefs = advertisement.References
                .Where(r => r.Key.StartsWith("refs/heads/", StringComparison.Ordinal) && r.Value.Equals(advertisement.HeadHash.Value))
                .Select(r => r.Key["refs/heads/".Length..])
                .ToList();

            if (matchingRefs.Contains("main"))
            {
                remoteDefaultBranch = "main";
            }
            else if (matchingRefs.Contains("master"))
            {
                remoteDefaultBranch = "master";
            }
            else if (matchingRefs.Count > 0)
            {
                remoteDefaultBranch = matchingRefs[0];
            }
        }

        // Determine local branch to initialize and checkout (defaults to remote's default branch, or "main")
        string targetBranch;
        if (!string.IsNullOrEmpty(branch))
        {
            targetBranch = branch.StartsWith("refs/heads/", StringComparison.Ordinal)
                ? branch["refs/heads/".Length..]
                : branch;
        }
        else
        {
            targetBranch = remoteDefaultBranch ?? "main";
        }

        var fullTargetPath = Path.GetFullPath(targetPath);
        if (File.Exists(fullTargetPath))
        {
            throw new ArgumentException($"Target path '{fullTargetPath}' already exists and is not a directory.", nameof(targetPath));
        }

        var targetExisted = Directory.Exists(fullTargetPath);
        if (targetExisted && Directory.EnumerateFileSystemEntries(fullTargetPath).Any())
        {
            throw new ArgumentException($"Target path '{fullTargetPath}' already exists and is not empty.", nameof(targetPath));
        }

        var parentDir = Path.GetDirectoryName(fullTargetPath);
        if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        try
        {
            var workspace = GitRepositoryWithIndexAndWorkspace.Init(fullTargetPath, initialBranch: targetBranch, objectFormat: advertisement.ObjectFormat);

            // Configure remote in .git/config
            var persistedUrl = remoteUrl;
            if (clientOptions.SanitizeRemoteUrlInConfig && Uri.TryCreate(remoteUrl, UriKind.Absolute, out var parsedUri) && !string.IsNullOrEmpty(parsedUri.UserInfo))
            {
                var builder = new UriBuilder(parsedUri)
                {
                    UserName = string.Empty,
                    Password = string.Empty
                };
                persistedUrl = builder.Uri.ToString();
            }

            var configPath = Path.Combine(workspace.GitDirectory, "config");
            var config = await GitConfigFile.ReadFromFileAsync(configPath, cancellationToken).ConfigureAwait(false);
            config.SetValue("remote", remoteName, "url", persistedUrl);
            config.SetValue("remote", remoteName, "fetch", $"+refs/heads/*:refs/remotes/{remoteName}/*");
            await config.WriteToFileAsync(configPath, cancellationToken).ConfigureAwait(false);

            var repository = new GitRemoteClientRepository(workspace, remoteUrl, clientOptions);

            // Fetch objects and update remote tracking refs
            await repository.FetchAsync(remote: remoteName, branch: null, prune: false, cancellationToken: cancellationToken).ConfigureAwait(false);

            // Check if target branch has a commit to check out from the fetched tracking ref
            var trackingRefName = $"refs/remotes/{remoteName}/{targetBranch}";
            var targetCommitHash = await workspace.ReferenceStore.TryResolveReferenceAsync(trackingRefName, cancellationToken).ConfigureAwait(false);

            if (!targetCommitHash.HasValue && !string.IsNullOrEmpty(branch))
            {
                throw new GitRemoteException($"Remote branch '{branch}' not found in upstream '{remoteName}'.");
            }

            if (targetCommitHash.HasValue)
            {
                // Point local branch to fetched commit
                await workspace.ReferenceStore.CreateReferenceAsync($"refs/heads/{targetBranch}", targetCommitHash.Value, overwrite: true, cancellationToken).ConfigureAwait(false);

                // Configure upstream tracking
                config = await GitConfigFile.ReadFromFileAsync(configPath, cancellationToken).ConfigureAwait(false);
                config.SetValue("branch", targetBranch, "remote", remoteName);
                config.SetValue("branch", targetBranch, "merge", $"refs/heads/{targetBranch}");
                await config.WriteToFileAsync(configPath, cancellationToken).ConfigureAwait(false);

                // Checkout working tree matching commit
                await workspace.ResetAsync(targetCommitHash.Value, GitResetMode.Hard, cancellationToken).ConfigureAwait(false);

                // Create remote HEAD symbolic ref pointing to default branch
                var defaultTrackingBranch = remoteDefaultBranch ?? targetBranch;
                var remoteHeadPath = Path.Combine(workspace.GitDirectory, "refs", "remotes", remoteName, "HEAD");
                var remoteHeadDir = Path.GetDirectoryName(remoteHeadPath);
                if (!string.IsNullOrEmpty(remoteHeadDir) && !Directory.Exists(remoteHeadDir))
                {
                    Directory.CreateDirectory(remoteHeadDir);
                }
                await File.WriteAllTextAsync(remoteHeadPath, $"ref: refs/remotes/{remoteName}/{defaultTrackingBranch}\n", cancellationToken).ConfigureAwait(false);
                workspace.ReferenceStore.InvalidateCaches();
            }

            return repository;
        }
        catch
        {
            if (!targetExisted && Directory.Exists(fullTargetPath))
            {
                TryDeleteDirectory(fullTargetPath);
            }
            else if (targetExisted)
            {
                var gitDir = Path.Combine(fullTargetPath, ".git");
                if (Directory.Exists(gitDir))
                {
                    TryDeleteDirectory(gitDir);
                }
            }
            throw;
        }
    }

    /// <inheritdoc />
    public async Task FetchAsync(
        string? remote = null,
        string? branch = null,
        bool prune = false,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(branch))
        {
            ValidateBranchName(branch);
        }
        if (remote != null)
        {
            ValidateRemoteName(remote, nameof(remote));
        }

        var targetRemote = remote ?? await ResolveRemoteNameAsync(branch, cancellationToken).ConfigureAwait(false);
        var remoteUrl = await ResolveRemoteUrlAsync(targetRemote, cancellationToken).ConfigureAwait(false);

        var advertisement = await _connection.DiscoverReferencesAsync(remoteUrl, "git-upload-pack", cancellationToken).ConfigureAwait(false);

        if (advertisement.ObjectFormat != _repo.ObjectFormat)
        {
            throw new GitRemoteException($"Object format mismatch: local repository is {_repo.ObjectFormat.ToFormatName()} but remote repository is {advertisement.ObjectFormat.ToFormatName()}.");
        }

        // Determine remote branches to fetch
        var remoteRefsToFetch = new Dictionary<string, GitHash>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(branch))
        {
            var fullRef = branch.StartsWith("refs/", StringComparison.Ordinal)
                ? branch
                : $"refs/heads/{branch}";

            if (advertisement.References.TryGetValue(fullRef, out var hash))
            {
                remoteRefsToFetch[fullRef] = hash;
            }
            else if (!branch.StartsWith("refs/", StringComparison.Ordinal) &&
                     advertisement.References.TryGetValue($"refs/tags/{branch}", out var tagHash))
            {
                remoteRefsToFetch[$"refs/tags/{branch}"] = tagHash;
            }
            else
            {
                throw new GitRemoteException($"Could not find remote ref '{branch}'.");
            }
        }
        else
        {
            foreach (var (refName, hash) in advertisement.References)
            {
                if (refName.StartsWith("refs/heads/", StringComparison.Ordinal) ||
                    refName.StartsWith("refs/tags/", StringComparison.Ordinal))
                {
                    remoteRefsToFetch[refName] = hash;
                }
            }
        }

        if (remoteRefsToFetch.Count > 0)
        {
            // Determine which objects we need to download (wants)
            var wants = new List<GitHash>();
            foreach (var hash in remoteRefsToFetch.Values.Distinct())
            {
                var exists = false;
                try
                {
                    await _repo.ObjectStore.ReadObjectAsync(hash, cancellationToken).ConfigureAwait(false);
                    exists = true;
                }
                catch (FileNotFoundException)
                {
                    exists = false;
                }

                if (!exists)
                {
                    wants.Add(hash);
                }
            }

            if (wants.Count > 0)
            {
                // Collect local commits we already have (haves)
                var haves = new List<GitHash>();
                var localRefs = await _repo.ReferenceStore.GetReferencesAsync(cancellationToken).ConfigureAwait(false);
                foreach (var hash in localRefs.Values.Distinct())
                {
                    haves.Add(hash);
                }

                await using var uploadPackResponse = await _connection.UploadPackAsync(
                    remoteUrl,
                    wants,
                    haves,
                    advertisement,
                    cancellationToken).ConfigureAwait(false);

                var packReader = new GitPackReader();
                await packReader.ReadAsync(_repo, uploadPackResponse.PackStream, cancellationToken).ConfigureAwait(false);
            }

            // Update local remote-tracking references (refs/remotes/{remote}/{branch})
            foreach (var (refName, hash) in remoteRefsToFetch)
            {
                if (refName.StartsWith("refs/heads/", StringComparison.Ordinal))
                {
                    var branchName = refName["refs/heads/".Length..];
                    var trackingRef = $"refs/remotes/{targetRemote}/{branchName}";
                    await _repo.ReferenceStore.CreateReferenceAsync(trackingRef, hash, overwrite: true, cancellationToken).ConfigureAwait(false);
                }
                else if (refName.StartsWith("refs/tags/", StringComparison.Ordinal))
                {
                    var existingTag = await _repo.ReferenceStore.TryResolveReferenceAsync(refName, cancellationToken).ConfigureAwait(false);
                    if (!existingTag.HasValue)
                    {
                        await _repo.ReferenceStore.CreateReferenceAsync(refName, hash, overwrite: false, cancellationToken).ConfigureAwait(false);
                    }
                    else if (!existingTag.Value.Equals(hash))
                    {
                        if (!string.IsNullOrEmpty(branch))
                        {
                            throw new GitRemoteException($"Cannot update tag '{refName}': existing local tag points to {existingTag.Value} (would clobber existing tag).");
                        }
                    }
                }
            }
        }

        // Prune deleted remote branches if requested
        if (prune)
        {
            var remotePrefix = $"refs/remotes/{targetRemote}/";
            var existingTrackingRefs = await _repo.ReferenceStore.GetReferencesByPrefixAsync(remotePrefix, cancellationToken).ConfigureAwait(false);

            foreach (var trackingRef in existingTrackingRefs.Keys)
            {
                var branchName = trackingRef[remotePrefix.Length..];
                if (branchName.Equals("HEAD", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(branch))
                {
                    var cleanBranch = branch.StartsWith("refs/heads/", StringComparison.Ordinal)
                        ? branch["refs/heads/".Length..]
                        : branch;

                    if (!branchName.Equals(cleanBranch, StringComparison.Ordinal))
                    {
                        continue;
                    }
                }

                var remoteBranchRef = $"refs/heads/{branchName}";
                if (!advertisement.References.ContainsKey(remoteBranchRef))
                {
                    await _repo.ReferenceStore.DeleteReferenceAsync(trackingRef, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        _repo.InvalidateCaches(raiseChanged: false);
    }

    /// <inheritdoc />
    public async Task PushAsync(
        string? remote = null,
        string? branch = null,
        bool force = false,
        bool setUpstream = false,
        CancellationToken cancellationToken = default)
    {
        if (remote != null)
        {
            ValidateRemoteName(remote, nameof(remote));
        }

        var targetRemote = remote ?? await ResolveRemoteNameAsync(branch, cancellationToken).ConfigureAwait(false);
        var localBranch = branch ?? await GetCurrentBranchOrUnbornBranchAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(localBranch))
        {
            throw new InvalidOperationException("Cannot push in detached HEAD state without specifying a branch.");
        }

        string localRef;
        string remoteRefName;
        bool isTag = false;

        if (localBranch.StartsWith("refs/tags/", StringComparison.Ordinal))
        {
            ValidateBranchName(localBranch);
            isTag = true;
            localRef = localBranch;
            remoteRefName = localBranch;
        }
        else if (localBranch.StartsWith("refs/heads/", StringComparison.Ordinal))
        {
            localBranch = localBranch["refs/heads/".Length..];
            ValidateBranchName(localBranch);
            localRef = $"refs/heads/{localBranch}";
            remoteRefName = localRef;
        }
        else
        {
            ValidateBranchName(localBranch);
            var headCommit = await _repo.ReferenceStore.TryResolveReferenceAsync($"refs/heads/{localBranch}", cancellationToken).ConfigureAwait(false);
            if (headCommit.HasValue)
            {
                localRef = $"refs/heads/{localBranch}";
                remoteRefName = localRef;
            }
            else
            {
                var tagCommit = await _repo.ReferenceStore.TryResolveReferenceAsync($"refs/tags/{localBranch}", cancellationToken).ConfigureAwait(false);
                if (tagCommit.HasValue)
                {
                    isTag = true;
                    localRef = $"refs/tags/{localBranch}";
                    remoteRefName = localRef;
                }
                else
                {
                    localRef = $"refs/heads/{localBranch}";
                    remoteRefName = localRef;
                }
            }
        }

        try
        {
            GitReferenceStore.NormalizeAbsoluteReferencePath(remoteRefName);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"Invalid reference name to push '{remoteRefName}': {ex.Message}", nameof(branch), ex);
        }

        using (await _repo.LockManager.AcquireReferenceLockAsync(localRef, cancellationToken).ConfigureAwait(false))
        {
            var localCommit = await _repo.ReferenceStore.TryResolveReferenceAsync(localRef, cancellationToken).ConfigureAwait(false);
            if (!localCommit.HasValue)
            {
                if (string.IsNullOrEmpty(branch))
                {
                    throw new InvalidOperationException($"Current branch '{localBranch}' has no commits to push.");
                }
                var refKind = isTag ? "Tag" : "Branch";
                throw new ArgumentException($"{refKind} '{localBranch}' does not exist.", nameof(branch));
            }

            var remoteUrl = await ResolveRemoteUrlAsync(targetRemote, cancellationToken).ConfigureAwait(false);
            var advertisement = await _connection.DiscoverReferencesAsync(remoteUrl, "git-receive-pack", cancellationToken).ConfigureAwait(false);

            if (advertisement.ObjectFormat != _repo.ObjectFormat)
            {
                throw new GitRemoteException($"Object format mismatch: local repository is {_repo.ObjectFormat.ToFormatName()} but remote repository is {advertisement.ObjectFormat.ToFormatName()}.");
            }

            GitHash? remoteCommit = advertisement.References.TryGetValue(remoteRefName, out var existingHash)
                ? existingHash
                : null;

            // If remote ref already equals local commit, nothing to push
            if (remoteCommit.HasValue && remoteCommit.Value.Equals(localCommit.Value))
            {
                if (!isTag && setUpstream)
                {
                    var configPath = Path.Combine(_repo.GitDirectory, "config");
                    var config = await GitConfigFile.ReadFromFileAsync(configPath, cancellationToken).ConfigureAwait(false);
                    config.SetValue("branch", localBranch, "remote", targetRemote);
                    config.SetValue("branch", localBranch, "merge", remoteRefName);
                    await config.WriteToFileAsync(configPath, cancellationToken).ConfigureAwait(false);
                    _repo.InvalidateCaches(raiseChanged: false);
                }
                return;
            }

            // Validate fast-forward unless force is specified (or tag update requires force)
            if (isTag)
            {
                if (remoteCommit.HasValue && !force)
                {
                    throw new GitRemoteException($"Remote tag '{remoteRefName}' already exists (use force to overwrite).");
                }
            }
            else if (remoteCommit.HasValue && !force)
            {
                var isFastForward = false;
                try
                {
                    isFastForward = await _repo.IsCommitReachableAsync(from: localCommit.Value, to: remoteCommit.Value, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException)
                {
                    isFastForward = false;
                }

                if (!isFastForward)
                {
                    throw new GitRemoteException("Non-fast-forward push rejected (use force to overwrite).");
                }
            }

            // Collect objects to send, excluding objects already present on remote
            var walker = new GitObjectWalker(_repo);
            var objectsToSend = await walker.CollectAsync(
                roots: new[] { localCommit.Value },
                excludes: advertisement.References.Values,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            // Always send a packfile for non-delete updates (even an empty pack if objects already exist on remote).
            // Use a temporary file stream so large packs are not buffered in memory.
            var tempFilePath = Path.Combine(Path.GetTempPath(), $"pmad_git_pack_{Guid.NewGuid():N}.tmp");
            FileStream? packDataStream = null;
            try
            {
                packDataStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.DeleteOnClose);
                var packBuilder = new GitPackBuilder();
                await packBuilder.WriteAsync(_repo, objectsToSend, packDataStream, cancellationToken).ConfigureAwait(false);
                packDataStream.Seek(0, SeekOrigin.Begin);

                var command = new GitRefUpdateCommand(remoteCommit, localCommit.Value, remoteRefName);
                await _connection.ReceivePackAsync(
                    remoteUrl,
                    new[] { command },
                    packDataStream,
                    advertisement,
                    _repo.HashLengthBytes,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (packDataStream != null)
                {
                    await packDataStream.DisposeAsync().ConfigureAwait(false);
                }
                if (File.Exists(tempFilePath))
                {
                    try { File.Delete(tempFilePath); } catch { }
                }
            }

            if (!isTag)
            {
                // Update local remote-tracking reference
                var trackingRef = $"refs/remotes/{targetRemote}/{localBranch}";
                await _repo.ReferenceStore.CreateReferenceAsync(trackingRef, localCommit.Value, overwrite: true, cancellationToken).ConfigureAwait(false);

                // Configure upstream tracking if requested
                if (setUpstream)
                {
                    var configPath = Path.Combine(_repo.GitDirectory, "config");
                    var config = await GitConfigFile.ReadFromFileAsync(configPath, cancellationToken).ConfigureAwait(false);
                    config.SetValue("branch", localBranch, "remote", targetRemote);
                    config.SetValue("branch", localBranch, "merge", remoteRefName);
                    await config.WriteToFileAsync(configPath, cancellationToken).ConfigureAwait(false);
                }
            }

            _repo.InvalidateCaches(raiseChanged: false);
        }
    }

    /// <inheritdoc />
    public async Task<GitMergeResult> PullAsync(
        string? remote = null,
        string? branch = null,
        bool rebase = false,
        CancellationToken cancellationToken = default)
    {
        if (rebase)
        {
            throw new NotSupportedException("Rebase is not supported in managed Git remote client; use merge instead.");
        }

        if (remote != null)
        {
            ValidateRemoteName(remote, nameof(remote));
        }

        EnsureWorkspace();

        await FetchAsync(remote, branch, prune: false, cancellationToken).ConfigureAwait(false);

        var targetRemote = remote ?? await ResolveRemoteNameAsync(branch, cancellationToken).ConfigureAwait(false);
        string targetRemoteBranch;
        if (!string.IsNullOrEmpty(branch))
        {
            ValidateBranchName(branch);
            var cleanBranch = branch.StartsWith("refs/heads/", StringComparison.Ordinal)
                ? branch["refs/heads/".Length..]
                : branch;
            targetRemoteBranch = $"refs/remotes/{targetRemote}/{cleanBranch}";
        }
        else
        {
            var currentBranch = await _repo.ReferenceStore.GetCurrentBranchNameAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(currentBranch))
            {
                throw new InvalidOperationException("Cannot pull when HEAD is detached.");
            }

            var tracking = await _repo.GetTrackingStatusAsync(currentBranch, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(tracking.UpstreamBranch))
            {
                targetRemoteBranch = tracking.UpstreamBranch.StartsWith("refs/", StringComparison.Ordinal)
                    ? tracking.UpstreamBranch
                    : $"refs/remotes/{tracking.UpstreamBranch}";
            }
            else
            {
                targetRemoteBranch = $"refs/remotes/{targetRemote}/{currentBranch}";
            }
        }

        var remoteCommit = await _repo.ReferenceStore.TryResolveReferenceAsync(targetRemoteBranch, cancellationToken).ConfigureAwait(false);
        if (!remoteCommit.HasValue)
        {
            throw new InvalidOperationException($"Remote-tracking branch '{targetRemoteBranch}' not found. Cannot pull without an existing remote tracking branch.");
        }

        return await MergeAsync(targetRemoteBranch, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<GitMergeResult> MergeAsync(string branch, CancellationToken cancellationToken = default)
    {
        EnsureWorkspace();
        return _workspaceRepo!.MergeAsync(branch, cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> IsMergeInProgressAsync(CancellationToken cancellationToken = default)
    {
        EnsureWorkspace();
        return _workspaceRepo!.IsMergeInProgressAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetConflictedFilesAsync(CancellationToken cancellationToken = default)
    {
        EnsureWorkspace();
        return _workspaceRepo!.GetConflictedFilesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task ResolveConflictAsync(string relativeFilePath, CancellationToken cancellationToken = default)
    {
        EnsureWorkspace();
        return _workspaceRepo!.ResolveConflictAsync(relativeFilePath, cancellationToken);
    }

    /// <inheritdoc />
    public async Task ContinueMergeAsync(string? commitMessage = null, CancellationToken cancellationToken = default)
    {
        EnsureWorkspace();
        await _workspaceRepo!.ContinueMergeAsync(commitMessage, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task AbortMergeAsync(CancellationToken cancellationToken = default)
    {
        EnsureWorkspace();
        return _workspaceRepo!.AbortMergeAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<GitTrackingStatus> GetTrackingStatusAsync(string? branch = null, CancellationToken cancellationToken = default)
        => _repo.GetTrackingStatusAsync(branch, cancellationToken);

    /// <inheritdoc />
    public Task<bool> IsCommitPushedAsync(string commitHash, string? remoteBranch = null, CancellationToken cancellationToken = default)
    {
        if (GitHash.TryParse(commitHash, out var hash))
        {
            return _repo.IsCommitPushedAsync(hash, remoteBranch, cancellationToken);
        }
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<bool> IsCommitPushedAsync(GitHash commitHash, string? remoteBranch = null, CancellationToken cancellationToken = default)
        => _repo.IsCommitPushedAsync(commitHash, remoteBranch, cancellationToken);

    private void EnsureWorkspace()
    {
        if (_workspaceRepo is null)
        {
            throw new InvalidOperationException("This operation requires a repository with an active workspace and working tree.");
        }
    }

    private async Task<string?> GetCurrentBranchOrUnbornBranchAsync(CancellationToken cancellationToken)
    {
        var currentBranch = await _repo.ReferenceStore.GetCurrentBranchNameAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(currentBranch))
        {
            return currentBranch;
        }

        var headPath = Path.Combine(_repo.GitDirectory, "HEAD");
        if (File.Exists(headPath))
        {
            var content = (await File.ReadAllTextAsync(headPath, cancellationToken).ConfigureAwait(false)).Trim();
            const string prefix = "ref: refs/heads/";
            if (content.StartsWith(prefix, StringComparison.Ordinal))
            {
                var branch = content[prefix.Length..].Trim();
                if (!string.IsNullOrEmpty(branch))
                {
                    return branch;
                }
            }
        }

        return null;
    }

    private async Task<string> ResolveRemoteNameAsync(string? branch, CancellationToken cancellationToken)
    {
        var targetBranch = branch ?? await GetCurrentBranchOrUnbornBranchAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(targetBranch))
        {
            if (targetBranch.StartsWith("refs/heads/", StringComparison.Ordinal))
            {
                targetBranch = targetBranch["refs/heads/".Length..];
            }

            var configPath = Path.Combine(_repo.GitDirectory, "config");
            if (File.Exists(configPath))
            {
                var config = await GitConfigFile.ReadWithIncludesAsync(configPath, cancellationToken).ConfigureAwait(false);
                var configuredRemote = config.GetValue("branch", targetBranch, "remote");
                if (!string.IsNullOrEmpty(configuredRemote))
                {
                    ValidateRemoteName(configuredRemote, "configuredRemote");
                    return configuredRemote;
                }
            }
        }

        return "origin";
    }

    private async Task<Uri> ResolveRemoteUrlAsync(string remoteName, CancellationToken cancellationToken)
    {
        ValidateRemoteName(remoteName, nameof(remoteName));

        string? urlString = null;
        var configPath = Path.Combine(_repo.GitDirectory, "config");
        if (File.Exists(configPath))
        {
            var config = await GitConfigFile.ReadWithIncludesAsync(configPath, cancellationToken).ConfigureAwait(false);
            var configuredUrl = config.GetValue("remote", remoteName, "url");
            if (!string.IsNullOrEmpty(configuredUrl))
            {
                urlString = configuredUrl;
            }
        }

        if (string.IsNullOrEmpty(urlString) && !string.IsNullOrEmpty(DefaultRemoteUrl))
        {
            urlString = DefaultRemoteUrl;
        }

        if (string.IsNullOrEmpty(urlString))
        {
            throw new InvalidOperationException($"No remote URL configured for remote '{remoteName}'.");
        }

        if (!Uri.TryCreate(urlString, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new NotSupportedException($"Unsupported remote URL '{FormatDiagnosticUrl(urlString)}'. The managed Git remote client only supports HTTP and HTTPS protocols.");
        }

        return uri;
    }

    internal static string FormatDiagnosticUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return GitHttpConnection.FormatDiagnosticUri(uri);
        }

        var atIndex = url.IndexOf('@');
        var schemeIndex = url.IndexOf("://", StringComparison.Ordinal);
        if (schemeIndex >= 0 && atIndex > schemeIndex)
        {
            return string.Concat(url.AsSpan(0, schemeIndex + 3), url.AsSpan(atIndex + 1));
        }

        return url;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
                }
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup failure during exception propagation
        }
    }

    private static void ValidateBranchName(string branchName, string paramName = "branch")
    {
        if (string.IsNullOrWhiteSpace(branchName))
        {
            throw new ArgumentException("Branch name cannot be empty.", paramName);
        }

        var fullRef = branchName.StartsWith("refs/", StringComparison.Ordinal)
            ? branchName
            : $"refs/heads/{branchName}";

        try
        {
            GitReferenceStore.NormalizeAbsoluteReferencePath(fullRef);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"Invalid branch name '{branchName}': {ex.Message}", paramName, ex);
        }

        if (branchName.Contains("@{") || branchName.EndsWith('/'))
        {
            throw new ArgumentException($"Invalid branch name '{branchName}'.", paramName);
        }
    }

    private static void ValidateRemoteName(string remoteName, string paramName = "remote")
    {
        if (string.IsNullOrWhiteSpace(remoteName))
        {
            throw new ArgumentException("Remote name cannot be empty or whitespace.", paramName);
        }

        if (remoteName.Contains('"') ||
            remoteName.Contains('\n') ||
            remoteName.Contains('\r') ||
            remoteName.Contains('\0') ||
            remoteName.Contains('\\') ||
            remoteName.Contains('/') ||
            remoteName.Contains("..") ||
            remoteName.Contains(' ') ||
            remoteName.Contains('~') ||
            remoteName.Contains('^') ||
            remoteName.Contains(':') ||
            remoteName.Contains('?') ||
            remoteName.Contains('*') ||
            remoteName.Contains('[') ||
            remoteName.Contains("@{") ||
            remoteName.StartsWith('.') ||
            remoteName.EndsWith(".lock", StringComparison.OrdinalIgnoreCase) ||
            remoteName.EndsWith('.'))
        {
            throw new ArgumentException($"Invalid remote name '{remoteName}'.", paramName);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposeConnection)
        {
            _connection.Dispose();
        }
    }
}
