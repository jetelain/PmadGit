using System.CommandLine;
using Pmad.Git.CliEmulator.Commands;
using Pmad.Git.CliEmulator.Internal;
using Pmad.Git.LocalRepositories;
using Pmad.Git.RemoteClient;

namespace Pmad.Git.CliEmulator;

/// <summary>
/// Managed Git CLI emulator that dispatches git-like commands against a local
/// <see cref="IGitWorkspaceRepository"/> and optional <see cref="IGitRepositoryWithRemote"/>.
/// </summary>
public sealed class GitCliEmulator : IGitCliEmulator, IDisposable, IAsyncDisposable
{
    private readonly IGitWorkspaceRepository _repository;
    private readonly IGitRepositoryWithRemote? _remote;
    private readonly bool _disposeWorkspace;
    private readonly bool _disposeRemote;

    /// <summary>
    /// Gets the underlying local workspace repository.
    /// </summary>
    public IGitWorkspaceRepository Repository => _repository;

    /// <summary>
    /// Gets the underlying remote client repository, or <see langword="null"/> if not configured.
    /// </summary>
    public IGitRepositoryWithRemote? Remote => _remote;

    /// <summary>
    /// Initializes a new instance of <see cref="GitCliEmulator"/>.
    /// </summary>
    /// <param name="repository">The local workspace repository (required).</param>
    /// <param name="remote">Optional remote client.</param>
    /// <param name="disposeRepositories">
    /// When <see langword="true"/>, the emulator disposes <paramref name="repository"/> and <paramref name="remote"/>
    /// when the emulator is disposed. Defaults to <see langword="false"/>.
    /// </param>
    public GitCliEmulator(
        IGitWorkspaceRepository repository,
        IGitRepositoryWithRemote? remote = null,
        bool disposeRepositories = false)
        : this(repository, remote, disposeWorkspace: disposeRepositories, disposeRemote: disposeRepositories)
    {
    }

    internal GitCliEmulator(
        IGitWorkspaceRepository repository,
        IGitRepositoryWithRemote? remote,
        bool disposeWorkspace,
        bool disposeRemote)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _remote = remote;
        _disposeWorkspace = disposeWorkspace;
        _disposeRemote = disposeRemote;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="GitCliEmulator"/> wrapping an existing managed <see cref="GitRemoteClientRepository"/>.
    /// </summary>
    /// <param name="remoteClient">The remote client repository with an active workspace.</param>
    /// <param name="disposeRepositories">
    /// When <see langword="true"/>, the emulator disposes <paramref name="remoteClient"/>
    /// when the emulator is disposed. Defaults to <see langword="false"/>.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when the repository is bare or lacks an active workspace.</exception>
    public GitCliEmulator(GitRemoteClientRepository remoteClient, bool disposeRepositories = false)
        : this(
            remoteClient?.WorkspaceRepository ?? throw new ArgumentException("A non-bare repository with an active workspace is required.", nameof(remoteClient)),
            remoteClient,
            disposeWorkspace: disposeRepositories,
            disposeRemote: disposeRepositories)
    {
    }

    /// <summary>
    /// Opens a local Git repository and configures a pure managed stack (<see cref="GitRepositoryWithIndexAndWorkspace"/>
    /// and <see cref="GitRemoteClientRepository"/>).
    /// </summary>
    /// <param name="path">Path to the working directory or .git directory of the repository.</param>
    /// <param name="remoteOptions">Optional client options for HTTP communication, credentials, and timeouts.</param>
    /// <param name="defaultRemoteUrl">Optional default remote URL.</param>
    /// <param name="lockManager">Optional lock manager for concurrency control.</param>
    /// <returns>A new <see cref="GitCliEmulator"/> instance that manages the lifetime of the underlying repositories.</returns>
    public static GitCliEmulator Open(
        string path,
        GitRemoteClientOptions? remoteOptions = null,
        string? defaultRemoteUrl = null,
        IGitRepositoryLockManager? lockManager = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var localRepo = GitRepositoryWithIndexAndWorkspace.Open(path, lockManager);
        try
        {
            var remoteRepo = new GitRemoteClientRepository(localRepo, defaultRemoteUrl, remoteOptions);
            return new GitCliEmulator(localRepo, remoteRepo, disposeRepositories: true);
        }
        catch
        {
            localRepo.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<GitCliResponse> InvokeAsync(string[] args, IUserApproval? userApproval = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);

        var approval = userApproval ?? AutoApproval.Instance;
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var ctx = new CommandContext(_repository, _remote, approval, stdout, stderr);

        var rootCommand = BuildRootCommand(ctx);

        var invocationConfig = new InvocationConfiguration
        {
            Output = stdout,
            Error = stderr,
            EnableDefaultExceptionHandler = false,
        };

        int exitCode;
        try
        {
            var parseResult = rootCommand.Parse(NormalizeArgs(args));
            exitCode = await parseResult.InvokeAsync(invocationConfig, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GitCliDeniedException ex)
        {
            return new GitCliResponse
            {
                ExitCode = 130,
                StdErr = $"error: {ex.Message}",
            };
        }
        catch (OperationCanceledException)
        {
            return new GitCliResponse
            {
                ExitCode = 130,
                StdErr = "error: Operation cancelled.",
            };
        }
        catch (Exception ex)
        {
            return new GitCliResponse
            {
                ExitCode = 1,
                StdErr = $"error: {ex.Message}",
            };
        }

        return new GitCliResponse
        {
            ExitCode = exitCode,
            StdOut = stdout.ToString(),
            StdErr = stderr.ToString(),
        };
    }

    private static RootCommand BuildRootCommand(CommandContext ctx)
    {
        var root = new RootCommand("Pmad.Git CLI emulator");

        root.Subcommands.Add(StatusCommand.Build(ctx));
        root.Subcommands.Add(LogCommand.Build(ctx));
        root.Subcommands.Add(DiffCommand.Build(ctx));
        root.Subcommands.Add(ShowCommand.Build(ctx));
        root.Subcommands.Add(AddCommand.Build(ctx));
        root.Subcommands.Add(RestoreCommand.Build(ctx));
        root.Subcommands.Add(CommitCommand.Build(ctx));
        root.Subcommands.Add(ResetCommand.Build(ctx));
        root.Subcommands.Add(RevertCommand.Build(ctx));
        root.Subcommands.Add(MergeCommand.Build(ctx));
        root.Subcommands.Add(BranchCommand.Build(ctx));
        root.Subcommands.Add(TagCommand.Build(ctx));
        root.Subcommands.Add(ConfigCommand.Build(ctx));
        root.Subcommands.Add(RemoteCommand.Build(ctx));
        root.Subcommands.Add(LsTreeCommand.Build(ctx));
        root.Subcommands.Add(RevParseCommand.Build(ctx));
        root.Subcommands.Add(CatFileCommand.Build(ctx));
        root.Subcommands.Add(FetchCommand.Build(ctx));
        root.Subcommands.Add(PullCommand.Build(ctx));
        root.Subcommands.Add(PushCommand.Build(ctx));

        return root;
    }

    internal static string[] NormalizeArgs(string[] args)
    {
        if (args.Length == 0)
        {
            return args;
        }

        var isLog = false;
        var list = new List<string>(args.Length);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!isLog)
            {
                if (arg == "log")
                {
                    isLog = true;
                }
                list.Add(arg);
            }
            else
            {
                if (arg.Length > 1 && arg[0] == '-' && int.TryParse(arg.AsSpan(1), out var count) && count >= 0)
                {
                    list.Add("-n");
                    list.Add(arg[1..]);
                }
                else
                {
                    list.Add(arg);
                }
            }
        }

        return [.. list];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposeRemote && _remote is IDisposable disposableRemote)
        {
            disposableRemote.Dispose();
        }
        if (_disposeWorkspace && _repository is IDisposable disposableWorkspace)
        {
            disposableWorkspace.Dispose();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposeRemote)
        {
            if (_remote is IAsyncDisposable asyncRemote)
            {
                await asyncRemote.DisposeAsync().ConfigureAwait(false);
            }
            else if (_remote is IDisposable disposableRemote)
            {
                disposableRemote.Dispose();
            }
        }

        if (_disposeWorkspace)
        {
            if (_repository is IAsyncDisposable asyncWorkspace)
            {
                await asyncWorkspace.DisposeAsync().ConfigureAwait(false);
            }
            else if (_repository is IDisposable disposableWorkspace)
            {
                disposableWorkspace.Dispose();
            }
        }
    }
}
