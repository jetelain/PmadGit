using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Evaluates file and directory paths against Git ignore patterns (.gitignore).
/// </summary>
public sealed class GitIgnoreMatcher
{
    private sealed record Rule(Regex Regex, bool IsNegated, bool DirectoryOnly);

    private readonly List<Rule> _rules = new();

    /// <summary>
    /// Loads ignore rules from a repository working directory.
    /// Inspects root .gitignore and .git/info/exclude if they exist.
    /// </summary>
    /// <param name="workingDirectory">The root working tree directory.</param>
    /// <returns>A configured <see cref="GitIgnoreMatcher"/> instance.</returns>
    public static GitIgnoreMatcher Load(string workingDirectory)
    {
        var matcher = new GitIgnoreMatcher();

        var rootGitIgnore = Path.Combine(workingDirectory, ".gitignore");
        if (File.Exists(rootGitIgnore))
        {
            matcher.AddRulesFromFile(rootGitIgnore);
        }

        var gitExclude = Path.Combine(workingDirectory, ".git", "info", "exclude");
        if (File.Exists(gitExclude))
        {
            matcher.AddRulesFromFile(gitExclude);
        }

        return matcher;
    }

    /// <summary>
    /// Parses and adds ignore patterns from a file.
    /// </summary>
    /// <param name="filePath">Absolute path to the ignore file.</param>
    public void AddRulesFromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        foreach (var line in File.ReadLines(filePath))
        {
            AddRule(line);
        }
    }

    /// <summary>
    /// Adds a single Git ignore pattern rule.
    /// </summary>
    /// <param name="rawPattern">The raw pattern line from a .gitignore file.</param>
    public void AddRule(string rawPattern)
    {
        if (string.IsNullOrWhiteSpace(rawPattern))
        {
            return;
        }

        var trimmed = rawPattern.Trim();
        if (trimmed.StartsWith('#'))
        {
            return; // Comment
        }

        var isNegated = false;
        if (trimmed.StartsWith('!'))
        {
            isNegated = true;
            trimmed = trimmed[1..].Trim();
        }

        if (string.IsNullOrEmpty(trimmed))
        {
            return;
        }

        var directoryOnly = false;
        if (trimmed.EndsWith('/'))
        {
            directoryOnly = true;
            trimmed = trimmed[..^1];
        }

        var rootAnchored = false;
        if (trimmed.StartsWith('/'))
        {
            rootAnchored = true;
            trimmed = trimmed[1..];
        }
        else if (trimmed.Contains('/'))
        {
            rootAnchored = true;
        }

        var regex = CompilePattern(trimmed, rootAnchored);
        if (regex is not null)
        {
            _rules.Add(new Rule(regex, isNegated, directoryOnly));
        }
    }

    /// <summary>
    /// Determines whether the specified repository-relative path matches ignore rules.
    /// </summary>
    /// <param name="relativePath">Repository-relative path using '/' or '\'.</param>
    /// <param name="isDirectory">Whether the path represents a directory.</param>
    /// <returns>True if the path should be ignored; false otherwise.</returns>
    public bool IsIgnored(string relativePath, bool isDirectory)
    {
        var normalized = relativePath.TrimStart('/', '\\').Replace('\\', '/');
        if (normalized.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(".git/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Check parent directory components
        if (!isDirectory)
        {
            var slashIndex = normalized.IndexOf('/');
            while (slashIndex >= 0)
            {
                var parentDir = normalized[..slashIndex];
                if (IsSinglePathIgnored(parentDir, isDirectory: true))
                {
                    return true;
                }
                slashIndex = normalized.IndexOf('/', slashIndex + 1);
            }
        }

        return IsSinglePathIgnored(normalized, isDirectory);
    }

    private bool IsSinglePathIgnored(string path, bool isDirectory)
    {
        var isIgnored = false;
        foreach (var rule in _rules)
        {
            if (rule.DirectoryOnly && !isDirectory)
            {
                continue;
            }

            if (rule.Regex.IsMatch(path))
            {
                isIgnored = !rule.IsNegated;
            }
        }

        return isIgnored;
    }

    private static Regex? CompilePattern(string pattern, bool rootAnchored)
    {
        var sb = new StringBuilder();
        if (rootAnchored)
        {
            sb.Append('^');
        }
        else
        {
            sb.Append("(?:^|/)");
        }

        var i = 0;
        while (i < pattern.Length)
        {
            var c = pattern[i];
            if (c == '*' && i + 1 < pattern.Length && pattern[i + 1] == '*')
            {
                i += 2;
                if (i < pattern.Length && pattern[i] == '/')
                {
                    i++;
                    sb.Append("(?:.+/)?");
                }
                else
                {
                    sb.Append(".*");
                }
            }
            else if (c == '*')
            {
                sb.Append("[^/]*");
                i++;
            }
            else if (c == '?')
            {
                sb.Append("[^/]");
                i++;
            }
            else if (c == '.')
            {
                sb.Append(@"\.");
                i++;
            }
            else if ("+()[]{}^$|\\".IndexOf(c) >= 0)
            {
                sb.Append('\\').Append(c);
                i++;
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }

        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.CultureInvariant);
    }
}
