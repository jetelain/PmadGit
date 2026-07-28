namespace Pmad.Git.Cli;

/// <summary>
/// 
/// </summary>
public class GitCliRepository
{
    /// <summary>
    /// Absolute path to the repository working tree root.
    /// </summary>
    public string RootPath { get; }

    /// <summary>
    /// Path to the Git CLI executable.
    /// </summary>
    public string GitCliPath => _gitRunner.GitCliPath;

    private readonly IGitRunner _gitRunner;

    public GitCliRepository(string rootPath, string gitCliPath = "git") 
        : this (rootPath, new GitRunner(gitCliPath))
    {

    }

    internal GitCliRepository(string rootPath, IGitRunner gitRunner)
    {
        if (!Directory.Exists(rootPath))
        {
            throw new ArgumentException($"Directory '{rootPath}' does not exists.", nameof(rootPath));
        }
        RootPath = rootPath;
        _gitRunner = gitRunner;
    }

    /// <summary>
    /// Downloads objects and refs from a remote repository, without integrating them.
    /// </summary>
    /// <param name="remote">Name of the remote to fetch from (defaults to <c>origin</c> when not specified).</param>
    /// <param name="branch">Name of the remote branch to fetch (defaults to all branches when not specified).</param>
    /// <param name="prune">When <c>true</c>, removes remote-tracking references that no longer exist on the remote.</param>
    /// <param name="cancellationToken"></param>
    public async Task FetchAsync(string? remote = null, string? branch = null, bool prune = false, CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "fetch" };
        if (prune)
        {
            arguments.Add("--prune");
        }
        if (remote != null)
        {
            arguments.Add(remote);
            if (branch != null)
            {
                arguments.Add(branch);
            }
        }

        var result = await RunGit(cancellationToken, arguments.ToArray());
        result.EnsureSuccess();
    }

    /// <summary>
    /// Pulls changes from a remote repository (fetch + merge).
    /// </summary>
    /// <param name="remote">Name of the remote (defaults to the current branch's remote when not specified).</param>
    /// <param name="branch">Name of the remote branch to pull (defaults to the current branch's upstream when not specified).</param>
    /// <param name="rebase">When <c>true</c>, rebases the current branch on top of the pulled branch instead of merging.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>A <see cref="GitMergeResult"/> describing whether the pull succeeded or stopped because of conflicts.</returns>
    public async Task<GitMergeResult> PullAsync(string? remote = null, string? branch = null, bool rebase = false, CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "pull" };
        if (rebase)
        {
            arguments.Add("--rebase");
        }
        if (remote != null)
        {
            arguments.Add(remote);
            if (branch != null)
            {
                arguments.Add(branch);
            }
        }

        var result = await RunGit(cancellationToken, arguments.ToArray());
        if (result.ExitCode == 0)
        {
            return new GitMergeResult(true, Array.Empty<string>());
        }

        var conflictedFiles = await GetConflictedFilesAsync(cancellationToken);
        if (conflictedFiles.Count > 0)
        {
            return new GitMergeResult(false, conflictedFiles);
        }

        result.EnsureSuccess();
        return new GitMergeResult(false, Array.Empty<string>());
    }

    /// <summary>
    /// Pushes changes to a remote repository.
    /// </summary>
    /// <param name="remote">Name of the remote (defaults to the current branch's remote when not specified).</param>
    /// <param name="branch">Name of the branch to push (defaults to the current branch when not specified).</param>
    /// <param name="force">When <c>true</c>, forces the push (using <c>--force-with-lease</c>).</param>
    /// <param name="setUpstream">When <c>true</c>, sets the pushed branch as the upstream of the current local branch.</param>
    /// <param name="cancellationToken"></param>
    public async Task PushAsync(string? remote = null, string? branch = null, bool force = false, bool setUpstream = false, CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "push" };
        if (setUpstream)
        {
            arguments.Add("-u");
        }
        if (force)
        {
            arguments.Add("--force-with-lease");
        }
        if (remote != null)
        {
            arguments.Add(remote);
            if (branch != null)
            {
                arguments.Add(branch);
            }
        }

        var result = await RunGit(cancellationToken, arguments.ToArray());
        result.EnsureSuccess();
    }

    /// <summary>
    /// Gets the name of the currently checked out branch.
    /// </summary>
    public async Task<string> GetCurrentBranchAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunGit(cancellationToken, "rev-parse", "--abbrev-ref", "HEAD");
        result.EnsureSuccess();
        return result.StdOut.Trim();
    }

    /// <summary>
    /// Lists the branches of the repository.
    /// </summary>
    /// <param name="includeRemote">When <c>true</c>, includes remote-tracking branches in addition to local branches.</param>
    /// <param name="cancellationToken"></param>
    public async Task<IReadOnlyList<string>> GetBranchesAsync(bool includeRemote = false, CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "branch", "--format=%(refname:short)" };
        if (includeRemote)
        {
            arguments.Add("--all");
        }
        var result = await RunGit(cancellationToken, arguments.ToArray());
        result.EnsureSuccess();
        return ParseLines(result.StdOut);
    }

    /// <summary>
    /// Creates a new branch without checking it out.
    /// </summary>
    /// <param name="branchName">Name of the branch to create.</param>
    /// <param name="startPoint">Commit-ish to start the branch from (defaults to <c>HEAD</c> when not specified).</param>
    /// <param name="cancellationToken"></param>
    public async Task CreateBranchAsync(string branchName, string? startPoint = null, CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "branch", branchName };
        if (startPoint != null)
        {
            arguments.Add(startPoint);
        }
        var result = await RunGit(cancellationToken, arguments.ToArray());
        result.EnsureSuccess();
    }

    /// <summary>
    /// Checks out an existing branch, or creates and checks out a new one.
    /// </summary>
    /// <param name="branchName">Name of the branch to check out.</param>
    /// <param name="createNew">When <c>true</c>, creates the branch before checking it out.</param>
    /// <param name="startPoint">Commit-ish to start the branch from when <paramref name="createNew"/> is <c>true</c>.</param>
    /// <param name="cancellationToken"></param>
    public async Task CheckoutAsync(string branchName, bool createNew = false, string? startPoint = null, CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "checkout" };
        if (createNew)
        {
            arguments.Add("-b");
        }
        arguments.Add(branchName);
        if (createNew && startPoint != null)
        {
            arguments.Add(startPoint);
        }
        var result = await RunGit(cancellationToken, arguments.ToArray());
        result.EnsureSuccess();
    }

    /// <summary>
    /// Deletes a local branch.
    /// </summary>
    /// <param name="branchName">Name of the branch to delete.</param>
    /// <param name="force">When <c>true</c>, deletes the branch even if it is not fully merged.</param>
    /// <param name="cancellationToken"></param>
    public async Task DeleteBranchAsync(string branchName, bool force = false, CancellationToken cancellationToken = default)
    {
        var result = await RunGit(cancellationToken, "branch", force ? "-D" : "-d", branchName);
        result.EnsureSuccess();
    }

    /// <summary>
    /// Renames a local branch.
    /// </summary>
    /// <param name="oldName">Current name of the branch.</param>
    /// <param name="newName">New name of the branch.</param>
    /// <param name="cancellationToken"></param>
    public async Task RenameBranchAsync(string oldName, string newName, CancellationToken cancellationToken = default)
    {
        var result = await RunGit(cancellationToken, "branch", "-m", oldName, newName);
        result.EnsureSuccess();
    }

    /// <summary>
    /// Merges the specified branch into the current branch.
    /// </summary>
    /// <param name="branch">Name of the branch (or commit-ish) to merge.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>A <see cref="GitMergeResult"/> describing whether the merge succeeded or stopped because of conflicts.</returns>
    public async Task<GitMergeResult> MergeAsync(string branch, CancellationToken cancellationToken = default)
    {
        var result = await RunGit(cancellationToken, "merge", "--no-edit", branch);
        if (result.ExitCode == 0)
        {
            return new GitMergeResult(true, Array.Empty<string>());
        }

        var conflictedFiles = await GetConflictedFilesAsync(cancellationToken);
        if (conflictedFiles.Count > 0)
        {
            return new GitMergeResult(false, conflictedFiles);
        }

        result.EnsureSuccess();
        return new GitMergeResult(false, Array.Empty<string>());
    }

    /// <summary>
    /// Indicates whether a merge (or pull) is currently in progress and waiting for conflict resolution.
    /// </summary>
    public async Task<bool> IsMergeInProgressAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunGit(cancellationToken, "rev-parse", "-q", "--verify", "MERGE_HEAD");
        return result.ExitCode == 0;
    }

    /// <summary>
    /// Gets the relative paths of the files that are currently in conflict.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetConflictedFilesAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunGit(cancellationToken, "diff", "--name-only", "--diff-filter=U");
        result.EnsureSuccess();
        return ParseLines(result.StdOut);
    }

    /// <summary>
    /// Marks a conflicted file as resolved, by staging its current content.
    /// </summary>
    /// <param name="relativeFilePath">Path of the file, relative to <see cref="RootPath"/>.</param>
    /// <param name="cancellationToken"></param>
    public async Task ResolveConflictAsync(string relativeFilePath, CancellationToken cancellationToken = default)
    {
        var result = await RunGit(cancellationToken, "add", "--", relativeFilePath);
        result.EnsureSuccess();
    }

    /// <summary>
    /// Completes an in-progress merge after all conflicts have been resolved (via <see cref="ResolveConflictAsync"/>).
    /// </summary>
    /// <param name="commitMessage">Optional commit message to use for the merge commit.</param>
    /// <param name="cancellationToken"></param>
    public async Task ContinueMergeAsync(string? commitMessage = null, CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "commit" };
        if (commitMessage != null)
        {
            arguments.Add("-m");
            arguments.Add(commitMessage);
        }
        else
        {
            arguments.Add("--no-edit");
        }
        var result = await RunGit(cancellationToken, arguments.ToArray());
        result.EnsureSuccess();
    }

    /// <summary>
    /// Aborts an in-progress merge, restoring the repository to the state it had before the merge started.
    /// </summary>
    public async Task AbortMergeAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunGit(cancellationToken, "merge", "--abort");
        result.EnsureSuccess();
    }

    private static IReadOnlyList<string> ParseLines(string output)
    {
        return output
            .Split('\n')
            .Select(line => line.Trim('\r', '\n', ' '))
            .Where(line => line.Length > 0)
            .ToList();
    }

    /// <summary>
    /// Runs a git command in the repository and returns the standard output if successful.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <param name="arguments"></param>
    /// <returns></returns>
    public async Task<string> RunAsync(CancellationToken cancellationToken, params string[] arguments)
    {
        var result = await RunGit(cancellationToken, arguments);
        result.EnsureSuccess();
        return result.StdOut;
    }

    /// <summary>
    /// Runs a git command in the repository and returns the standard output if successful.
    /// </summary>
    /// <param name="arguments"></param>
    /// <returns></returns>
    public async Task<string> RunAsync(params string[] arguments)
    {
        var result = await RunGit(default, arguments);
        result.EnsureSuccess();
        return result.StdOut;
    }

    private Task<GitResponse> RunGit(CancellationToken cancellationToken, params string[] arguments)
    {
        return _gitRunner.RunGit(RootPath, arguments, cancellationToken);
    }
}
