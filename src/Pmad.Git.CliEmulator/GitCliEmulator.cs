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
public sealed class GitCliEmulator
{
    private readonly IGitWorkspaceRepository _repository;
    private readonly IGitRepositoryWithRemote? _remote;

    /// <summary>
    /// Initializes a new instance of <see cref="GitCliEmulator"/>.
    /// </summary>
    public GitCliEmulator(
        IGitWorkspaceRepository repository,
        IGitRepositoryWithRemote? remote = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _remote = remote;
    }

    /// <summary>
    /// Invokes a git command and returns the captured output.
    /// </summary>
    public async Task<GitCliResponse> InvokeAsync(string[] args, IUserApproval userApproval)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(userApproval);

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var ctx = new CommandContext(_repository, _remote, userApproval, stdout, stderr);

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
            var parseResult = rootCommand.Parse(args);
            exitCode = await parseResult.InvokeAsync(invocationConfig, userApproval.CancellationToken)
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
}
