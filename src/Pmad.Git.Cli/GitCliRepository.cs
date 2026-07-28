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

    public async Task FetchAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunGit(cancellationToken, "fetch");
        result.EnsureSuccess();
    }

    // TODO: Pull, Push, Branches management, and Conflict management (on one or more branches)

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
