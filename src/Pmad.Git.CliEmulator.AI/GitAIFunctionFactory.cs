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
    /// Optional approval gate. When <see langword="null"/> all gated operations are denied
    /// (equivalent to <see cref="DenyApproval.Instance"/>).
    /// </param>
    /// <returns>An <see cref="AIFunction"/> ready to be placed in <see cref="ChatOptions.Tools"/>.</returns>
    public static AIFunction Create(IGitCliEmulator emulator, IUserApproval? userApproval = null)
    {
        ArgumentNullException.ThrowIfNull(emulator);
        var approval = userApproval ?? DenyApproval.Instance;

        return AIFunctionFactory.Create(
            async ([Description("Git sub-command and its arguments (e.g. [\"status\"], [\"commit\", \"-m\", \"msg\"], [\"log\", \"--oneline\", \"-10\"])")] string[] args,
                   CancellationToken cancellationToken) =>
            {
                var response = await emulator.InvokeAsync(args, approval, cancellationToken).ConfigureAwait(false);
                return FormatResponse(response);
            },
            "git",
            "Runs a git command against the current repository. " +
            "Supported sub-commands: status, log, diff, show, add, mv, restore, commit, reset, revert, " +
            "merge, branch, switch, checkout, tag, config, remote, ls-tree, rev-parse, cat-file, fetch, pull, push. " +
            "Returns stdout on success or an error message on failure.");
    }

    /// <summary>
    /// Creates a <see cref="AIFunction"/> named <c>git</c> that accepts the full git command line as a
    /// space-separated string (for models that prefer a single string parameter over an array).
    /// Single and double quotes are supported for arguments containing whitespace.
    /// </summary>
    /// <param name="emulator">The underlying CLI emulator to invoke.</param>
    /// <param name="userApproval">
    /// Optional approval gate. When <see langword="null"/> all gated operations are denied
    /// (equivalent to <see cref="DenyApproval.Instance"/>).
    /// </param>
    /// <returns>An <see cref="AIFunction"/> ready to be placed in <see cref="ChatOptions.Tools"/>.</returns>
    public static AIFunction CreateFromString(IGitCliEmulator emulator, IUserApproval? userApproval = null)
    {
        ArgumentNullException.ThrowIfNull(emulator);
        var approval = userApproval ?? DenyApproval.Instance;

        return AIFunctionFactory.Create(
            async ([Description("Space-separated git command and arguments, e.g. \"status\", \"commit -m 'Initial commit'\", \"log --oneline -10\"")] string command,
                   CancellationToken cancellationToken) =>
            {
                var args = ParseCommandLine(command);
                var response = await emulator.InvokeAsync(args, approval, cancellationToken).ConfigureAwait(false);
                return FormatResponse(response);
            },
            "git",
            "Runs a git command against the current repository. " +
            "Supported sub-commands: status, log, diff, show, add, mv, restore, commit, reset, revert, " +
            "merge, branch, switch, checkout, tag, config, remote, ls-tree, rev-parse, cat-file, fetch, pull, push. " +
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
        var message = string.Join(Environment.NewLine, parts);
        return string.IsNullOrEmpty(message) ? $"[exit {response.ExitCode}]" : $"[exit {response.ExitCode}] {message}";
    }

    internal static string[] ParseCommandLine(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return [];
        }

        var args = new List<string>();
        var current = new System.Text.StringBuilder();
        char? quote = null;
        var hadQuotes = false;

        for (var i = 0; i < commandLine.Length; i++)
        {
            var ch = commandLine[i];
            if (quote.HasValue)
            {
                if (ch == '\\' && i + 1 < commandLine.Length && (commandLine[i + 1] == quote.Value || commandLine[i + 1] == '\\'))
                {
                    current.Append(commandLine[++i]);
                }
                else if (ch == quote.Value)
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
                hadQuotes = true;
            }
            else if (char.IsWhiteSpace(ch))
            {
                if (current.Length > 0 || hadQuotes)
                {
                    args.Add(current.ToString());
                    current.Clear();
                    hadQuotes = false;
                }
            }
            else
            {
                current.Append(ch);
            }
        }

        if (current.Length > 0 || hadQuotes)
        {
            args.Add(current.ToString());
        }

        return [.. args];
    }
}
