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

        remoteName ??= "origin";
        var clientOptions = options ?? new GitRemoteClientOptions();
        using var connection = new GitHttpConnection(clientOptions);

        var remoteUri = new Uri(remoteUrl);
        var advertisement = await connection.DiscoverReferencesAsync(remoteUri, "git-upload-pack", cancellationToken).ConfigureAwait(false);

        // Determine default branch to initialize
        string targetBranch;
        if (!string.IsNullOrEmpty(branch))
        {
            targetBranch = branch.StartsWith("refs/heads/", StringComparison.Ordinal)
                ? branch["refs/heads/".Length..]
                : branch;
        }
        else if (!string.IsNullOrEmpty(advertisement.HeadSymrefTarget) &&
                 advertisement.HeadSymrefTarget.StartsWith("refs/heads/", StringComparison.Ordinal))
        {
            targetBranch = advertisement.HeadSymrefTarget["refs/heads/".Length..];
        }
        else if (advertisement.HeadHash.HasValue)
        {
            var matchingRef = advertisement.References.FirstOrDefault(r =>
                r.Key.StartsWith("refs/heads/", StringComparison.Ordinal) &&
                r.Value.Equals(advertisement.HeadHash.Value));

            targetBranch = !string.IsNullOrEmpty(matchingRef.Key)
                ? matchingRef.Key["refs/heads/".Length..]
                : "main";
        }
        else
        {
            targetBranch = "main";
        }

        var fullTargetPath = Path.GetFullPath(targetPath);
        var targetExisted = Directory.Exists(fullTargetPath);
        var parentDir = Path.GetDirectoryName(fullTargetPath);
        if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        try
        {
            var workspace = GitRepositoryWithIndexAndWorkspace.Init(fullTargetPath, initialBranch: targetBranch, objectFormat: advertisement.ObjectFormat);

            // Configure remote in .git/config
            var configPath = Path.Combine(workspace.GitDirectory, "config");
            var config = await GitConfigFile.ReadFromFileAsync(configPath, cancellationToken).ConfigureAwait(false);
            config.SetValue("remote", remoteName, "url", remoteUrl);
            config.SetValue("remote", remoteName, "fetch", $"+refs/heads/*:refs/remotes/{remoteName}/*");
            await config.WriteToFileAsync(configPath, cancellationToken).ConfigureAwait(false);

            var repository = new GitRemoteClientRepository(workspace, remoteUrl, clientOptions);

            // Fetch objects and update remote tracking refs
            await repository.FetchAsync(remote: remoteName, branch: null, prune: false, cancellationToken: cancellationToken).ConfigureAwait(false);

            // Check if target branch has a commit to check out
            GitHash? targetCommitHash = null;
            if (advertisement.References.TryGetValue($"refs/heads/{targetBranch}", out var branchHash))
            {
                targetCommitHash = branchHash;
            }
            else if (!string.IsNullOrEmpty(branch))
            {
                throw new GitRemoteException($"Remote branch '{branch}' not found in upstream '{remoteName}'.");
            }
            else if (advertisement.HeadHash.HasValue)
            {
                targetCommitHash = advertisement.HeadHash;
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
            }

            return repository;
        }
        catch
        {
            if (!targetExisted && Directory.Exists(fullTargetPath))
            {
                TryDeleteDirectory(fullTargetPath);
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
        var targetRemote = remote ?? await ResolveRemoteNameAsync(branch, cancellationToken).ConfigureAwait(false);
        var remoteUrl = await ResolveRemoteUrlAsync(targetRemote, cancellationToken).ConfigureAwait(false);

        var advertisement = await _connection.DiscoverReferencesAsync(remoteUrl, "git-upload-pack", cancellationToken).ConfigureAwait(false);

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
                    await _repo.ReferenceStore.CreateReferenceAsync(refName, hash, overwrite: true, cancellationToken).ConfigureAwait(false);
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
        var targetRemote = remote ?? await ResolveRemoteNameAsync(branch, cancellationToken).ConfigureAwait(false);
        var localBranch = branch ?? await _repo.ReferenceStore.GetCurrentBranchNameAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(localBranch))
        {
            throw new InvalidOperationException("Cannot push in detached HEAD state without specifying a branch.");
        }

        if (localBranch.StartsWith("refs/heads/", StringComparison.Ordinal))
        {
            localBranch = localBranch["refs/heads/".Length..];
        }

        var localRef = $"refs/heads/{localBranch}";
        var localCommit = await _repo.ReferenceStore.TryResolveReferenceAsync(localRef, cancellationToken).ConfigureAwait(false);
        if (!localCommit.HasValue)
        {
            throw new ArgumentException($"Branch '{localBranch}' does not exist.", nameof(branch));
        }

        var remoteUrl = await ResolveRemoteUrlAsync(targetRemote, cancellationToken).ConfigureAwait(false);
        var advertisement = await _connection.DiscoverReferencesAsync(remoteUrl, "git-receive-pack", cancellationToken).ConfigureAwait(false);

        var remoteRefName = $"refs/heads/{localBranch}";
        GitHash? remoteCommit = advertisement.References.TryGetValue(remoteRefName, out var existingHash)
            ? existingHash
            : null;

        // If remote ref already equals local commit, nothing to push
        if (remoteCommit.HasValue && remoteCommit.Value.Equals(localCommit.Value))
        {
            if (setUpstream)
            {
                var configPath = Path.Combine(_repo.GitDirectory, "config");
                var config = await GitConfigFile.ReadFromFileAsync(configPath, cancellationToken).ConfigureAwait(false);
                config.SetValue("branch", localBranch, "remote", targetRemote);
                config.SetValue("branch", localBranch, "merge", remoteRefName);
                await config.WriteToFileAsync(configPath, cancellationToken).ConfigureAwait(false);
                _repo.InvalidateCaches(raiseChanged: true);
            }
            return;
        }

        // Validate fast-forward unless force is specified
        if (remoteCommit.HasValue && !force)
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

        _repo.InvalidateCaches(raiseChanged: true);
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

        EnsureWorkspace();

        await FetchAsync(remote, branch, prune: false, cancellationToken).ConfigureAwait(false);

        var targetRemote = remote ?? await ResolveRemoteNameAsync(branch, cancellationToken).ConfigureAwait(false);
        string targetRemoteBranch;
        if (!string.IsNullOrEmpty(branch))
        {
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

    private async Task<string> ResolveRemoteNameAsync(string? branch, CancellationToken cancellationToken)
    {
        var targetBranch = branch ?? await _repo.ReferenceStore.GetCurrentBranchNameAsync(cancellationToken).ConfigureAwait(false);
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
                    return configuredRemote;
                }
            }
        }

        return "origin";
    }

    private async Task<Uri> ResolveRemoteUrlAsync(string remoteName, CancellationToken cancellationToken)
    {
        var configPath = Path.Combine(_repo.GitDirectory, "config");
        if (File.Exists(configPath))
        {
            var config = await GitConfigFile.ReadWithIncludesAsync(configPath, cancellationToken).ConfigureAwait(false);
            var configuredUrl = config.GetValue("remote", remoteName, "url");
            if (!string.IsNullOrEmpty(configuredUrl))
            {
                return new Uri(configuredUrl);
            }
        }

        if (!string.IsNullOrEmpty(DefaultRemoteUrl))
        {
            return new Uri(DefaultRemoteUrl);
        }

        throw new InvalidOperationException($"No remote URL configured for remote '{remoteName}'.");
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

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposeConnection)
        {
            _connection.Dispose();
        }
    }
}
