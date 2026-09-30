using System.ComponentModel;
using Microsoft.Extensions.AI;
using Pmad.Git.CliEmulator;

namespace Pmad.Git.CliEmulator.AI;

/// <summary>
/// Factory methods for creating a <see cref="AIFunction"/> that wraps a <see cref="IGitCliEmulator"/>
/// as a single <c>git</c> tool, suitable for use with any <see cref="IChatClient"/>-based agent pipeline.
/// </summary>
public static class GitAIFunctionFactory
{
    /// <summary>
    /// Creates a <see cref="AIFunction"/> named <c>git</c> that forwards a command-line argument array
    /// to <see cref="IGitCliEmulator.InvokeAsync"/> and returns the combined stdout / stderr output.
    /// </summary>
    /// <param name="emulator">The underlying CLI emulator to invoke.</param>
    /// <param name="userApproval">
    /// Optional approval gate. When <see langword="null"/> all operations are auto-approved
    /// (equivalent to <c>NoUserApproval</c>).
    /// </param>
    /// <returns>An <see cref="AIFunction"/> ready to be placed in <see cref="ChatOptions.Tools"/>.</returns>
    public static AIFunction Create(IGitCliEmulator emulator, IUserApproval? userApproval = null)
    {
        ArgumentNullException.ThrowIfNull(emulator);

        return AIFunctionFactory.Create(
            async ([Description("Git sub-command and its arguments (e.g. [\"status\"], [\"commit\", \"-m\", \"msg\"], [\"log\", \"--oneline\", \"-10\"])")] string[] args,
                   CancellationToken cancellationToken) =>
            {
                var response = await emulator.InvokeAsync(args, userApproval, cancellationToken).ConfigureAwait(false);
                return FormatResponse(response);
            },
            "git",
            "Runs a git command against the current repository. " +
            "Supported sub-commands: status, log, diff, show, add, restore, commit, reset, revert, " +
            "merge, branch, tag, config, remote, ls-tree, rev-parse, cat-file, fetch, pull, push. " +
            "Returns stdout on success or an error message on failure.");
    }

    /// <summary>
    /// Creates a <see cref="AIFunction"/> named <c>git</c> that accepts the full git command line as a
    /// space-separated string (for models that prefer a single string parameter over an array).
    /// The string is split on spaces; quoted arguments are not supported.
    /// </summary>
    /// <param name="emulator">The underlying CLI emulator to invoke.</param>
    /// <param name="userApproval">
    /// Optional approval gate. When <see langword="null"/> all operations are auto-approved.
    /// </param>
    /// <returns>An <see cref="AIFunction"/> ready to be placed in <see cref="ChatOptions.Tools"/>.</returns>
    public static AIFunction CreateFromString(IGitCliEmulator emulator, IUserApproval? userApproval = null)
    {
        ArgumentNullException.ThrowIfNull(emulator);

        return AIFunctionFactory.Create(
            async ([Description("Space-separated git command and arguments, e.g. \"status\", \"commit -m 'Initial commit'\", \"log --oneline -10\"")] string commandLine,
                   CancellationToken cancellationToken) =>
            {
                var args = ParseCommandLine(commandLine);
                var response = await emulator.InvokeAsync(args, userApproval, cancellationToken).ConfigureAwait(false);
                return FormatResponse(response);
            },
            "git",
            "Runs a git command against the current repository. " +
            "Supported sub-commands: status, log, diff, show, add, restore, commit, reset, revert, " +
            "merge, branch, tag, config, remote, ls-tree, rev-parse, cat-file, fetch, pull, push. " +
            "Pass the sub-command and flags as a single string. " +
            "Returns stdout on success or an error message on failure.");
    }

    private static string FormatResponse(GitCliResponse response)
    {
        if (response.IsSuccess)
        {
            return string.IsNullOrWhiteSpace(response.StdOut)
                ? "(success, no output)"
                : response.StdOut;
        }

        var parts = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(response.StdOut))
        {
            parts.Add(response.StdOut);
        }
        if (!string.IsNullOrWhiteSpace(response.StdErr))
        {
            parts.Add(response.StdErr);
        }
        return $"[exit {response.ExitCode}] " + string.Join(Environment.NewLine, parts);
    }

    private static string[] ParseCommandLine(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return [];
        }

        // Simple splitter: honours single/double quotes for basic cases.
        var args = new List<string>();
        var current = new System.Text.StringBuilder();
        char? quote = null;

        foreach (var ch in commandLine)
        {
            if (quote.HasValue)
            {
                if (ch == quote.Value)
                {
                    quote = null;
                }
                else
                {
                    current.Append(ch);
                }
            }
            else if (ch is '\'' or '"')
            {
                quote = ch;
            }
            else if (ch == ' ')
            {
                if (current.Length > 0)
                {
                    args.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(ch);
            }
        }

        if (current.Length > 0)
        {
            args.Add(current.ToString());
        }

        return [.. args];
    }
}
