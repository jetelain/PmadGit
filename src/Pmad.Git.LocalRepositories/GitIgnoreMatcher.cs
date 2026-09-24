using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Evaluates file and directory paths against Git ignore patterns (.gitignore).
/// </summary>
public sealed class GitIgnoreMatcher
{
    private sealed record Rule(Regex Regex, bool IsNegated, bool DirectoryOnly, string BasePrefix, string RawPattern);

    private readonly List<Rule> _rules = new();

    /// <summary>
    /// Loads ignore rules from a repository working directory.
    /// Inspects .git/info/exclude (lowest precedence), root .gitignore, and
    /// any per-directory .gitignore files discovered during scanning.
    /// </summary>
    /// <param name="workingDirectory">The root working tree directory.</param>
    /// <returns>A configured <see cref="GitIgnoreMatcher"/> instance.</returns>
    public static GitIgnoreMatcher Load(string workingDirectory)
    {
        var matcher = new GitIgnoreMatcher();

        // info/exclude has lower precedence than .gitignore; load it first so
        // that repository rules in .gitignore can override it.
        var gitExclude = Path.Combine(workingDirectory, ".git", "info", "exclude");
        if (File.Exists(gitExclude))
        {
            matcher.AddRulesFromFile(gitExclude);
        }

        var rootGitIgnore = Path.Combine(workingDirectory, ".gitignore");
        if (File.Exists(rootGitIgnore))
        {
            matcher.AddRulesFromFile(rootGitIgnore);
        }

        // Load per-directory .gitignore files (Git resolves them during tree walk).
        matcher.LoadSubdirectoryIgnoreFiles(workingDirectory, workingDirectory);

        return matcher;
    }

    /// <summary>
    /// Recursively loads .gitignore files from subdirectories, anchoring their
    /// patterns to their respective directory prefix.
    /// </summary>
    private void LoadSubdirectoryIgnoreFiles(string workingDirectory, string directory)
    {
        try
        {
            foreach (var subDir in Directory.EnumerateDirectories(directory))
            {
                var dirName = Path.GetFileName(subDir);

                // Skip .git itself
                if (dirName.Equals(".git", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var subDirInfo = new DirectoryInfo(subDir);
                // Skip symlinks and reparse points to avoid escaping repository or infinite recursion cycles
                if ((subDirInfo.Attributes & FileAttributes.ReparsePoint) != 0 || subDirInfo.LinkTarget != null)
                {
                    continue;
                }

                var relPrefix = Path.GetRelativePath(workingDirectory, subDir).Replace('\\', '/');

                // Do not recursively scan inside directories that are already ignored by a parent .gitignore rule
                if (IsIgnored(relPrefix, isDirectory: true))
                {
                    continue;
                }

                var subIgnore = Path.Combine(subDir, ".gitignore");
                if (File.Exists(subIgnore))
                {
                    AddRulesFromFile(subIgnore, relPrefix);
                }

                LoadSubdirectoryIgnoreFiles(workingDirectory, subDir);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Skip directories we cannot enumerate.
        }
        catch (IOException)
        {
            // Skip directories that are temporarily unavailable.
        }
    }

    /// <summary>
    /// Parses and adds ignore patterns from a file.
    /// </summary>
    /// <param name="filePath">Absolute path to the ignore file.</param>
    /// <param name="basePrefix">Optional repository-relative directory prefix for patterns in this file.</param>
    public void AddRulesFromFile(string filePath, string? basePrefix = null)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        foreach (var line in File.ReadLines(filePath))
        {
            AddRule(line, basePrefix);
        }
    }

    /// <summary>
    /// Adds a single Git ignore pattern rule.
    /// </summary>
    /// <param name="rawPattern">The raw pattern line from a .gitignore file.</param>
    /// <param name="basePrefix">Optional repository-relative directory prefix for this pattern.</param>
    public void AddRule(string rawPattern, string? basePrefix = null)
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
            var prefix = basePrefix?.Trim('/', '\\').Replace('\\', '/') ?? string.Empty;
            _rules.Add(new Rule(regex, isNegated, directoryOnly, prefix, trimmed));
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

        // Check if any parent directory is ignored per Git spec:
        // "It is not possible to re-include a file if a parent directory of that file is excluded."
        var slashIndex = normalized.IndexOf('/');
        while (slashIndex >= 0)
        {
            var parent = normalized[..slashIndex];
            if (IsDirectlyIgnored(parent, isDirectory: true))
            {
                return true;
            }
            slashIndex = normalized.IndexOf('/', slashIndex + 1);
        }

        return IsDirectlyIgnored(normalized, isDirectory);
    }

    private bool IsDirectlyIgnored(string path, bool isDirectory)
    {
        var isIgnored = false;
        foreach (var rule in _rules)
        {
            if (rule.DirectoryOnly && !isDirectory)
            {
                continue;
            }

            if (MatchesPattern(rule, path, isDirectory))
            {
                isIgnored = !rule.IsNegated;
            }
        }

        return isIgnored;
    }

    /// <summary>
    /// Checks whether any negated rule exists that could match descendants of <paramref name="dirRelPath"/>.
    /// </summary>
    /// <param name="dirRelPath">Repository-relative directory path.</param>
    /// <returns>True if any negated rule could apply within the directory; otherwise false.</returns>
    public bool HasNegatedRuleUnder(string dirRelPath)
    {
        var normalizedDir = dirRelPath.Trim('/', '\\').Replace('\\', '/');
        if (IsIgnored(normalizedDir, isDirectory: true))
        {
            return false;
        }

        foreach (var rule in _rules)
        {
            if (!rule.IsNegated)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(rule.BasePrefix))
            {
                if (rule.BasePrefix.Equals(normalizedDir, StringComparison.Ordinal) ||
                    rule.BasePrefix.StartsWith(normalizedDir + "/", StringComparison.Ordinal) ||
                    normalizedDir.StartsWith(rule.BasePrefix + "/", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            else
            {
                // Root/unanchored negated rules can potentially apply anywhere
                return true;
            }
        }
        return false;
    }

    private static bool MatchesPattern(Rule rule, string targetPath, bool isDirectory)
    {
        if (rule.DirectoryOnly && !isDirectory)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(rule.BasePrefix))
        {
            if (targetPath.Equals(rule.BasePrefix, StringComparison.Ordinal))
            {
                return isDirectory && rule.Regex.IsMatch(string.Empty);
            }

            if (targetPath.StartsWith(rule.BasePrefix + "/", StringComparison.Ordinal))
            {
                var relativeTarget = targetPath[(rule.BasePrefix.Length + 1)..];
                return rule.Regex.IsMatch(relativeTarget);
            }

            return false;
        }

        return rule.Regex.IsMatch(targetPath);
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
