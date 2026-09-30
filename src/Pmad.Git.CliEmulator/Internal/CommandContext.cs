using Pmad.Git.LocalRepositories;
using Pmad.Git.RemoteClient;

namespace Pmad.Git.CliEmulator.Internal;

/// <summary>Carries shared state for all command handlers within a single InvokeAsync call.</summary>
internal sealed class CommandContext
{
    public CommandContext(
        IGitWorkspaceRepository repository,
        IGitRepositoryWithRemote? remote,
        IUserApproval approval,
        TextWriter stdout,
        TextWriter stderr)
    {
        Repository = repository;
        Remote = remote;
        Approval = approval;
        StdOut = stdout;
        StdErr = stderr;
    }

    public IGitWorkspaceRepository Repository { get; }
    public IGitRepositoryWithRemote? Remote { get; }
    public IUserApproval Approval { get; }
    public TextWriter StdOut { get; }
    public TextWriter StdErr { get; }

    /// <summary>Writes a line to stderr and returns exit code 1.</summary>
    public int WriteError(string message)
    {
        StdErr.WriteLine($"error: {message}");
        return 1;
    }

    /// <summary>Writes a fatal error to stderr and returns exit code 128.</summary>
    public int WriteFatal(string message)
    {
        StdErr.WriteLine($"fatal: {message}");
        return 128;
    }

    /// <summary>Ensures remote is configured; writes error and returns false if not.</summary>
    public bool EnsureRemote(out IGitRepositoryWithRemote remote)
    {
        if (Remote is null)
        {
            StdErr.WriteLine("fatal: No remote configured. Provide an IGitRepositoryWithRemote to GitCliEmulator.");
            remote = null!;
            return false;
        }
        remote = Remote;
        return true;
    }
}
