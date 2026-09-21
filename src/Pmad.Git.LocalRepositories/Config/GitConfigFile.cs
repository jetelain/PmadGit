using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pmad.Git.LocalRepositories.Config;

/// <summary>
/// Reads, modifies, and writes Git configuration files (.git/config, ~/.gitconfig).
/// Preserves comments, whitespace, and formatting where possible.
/// </summary>
public sealed class GitConfigFile
{
    private abstract class LineNode
    {
        public string RawText { get; set; } = string.Empty;
    }

    private sealed class BlankOrCommentLine : LineNode
    {
        public BlankOrCommentLine(string text) => RawText = text;
    }

    private sealed class SectionHeaderLine : LineNode
    {
        public string Section { get; set; }
        public string? Subsection { get; set; }

        public SectionHeaderLine(string text, string section, string? subsection)
        {
            RawText = text;
            Section = section;
            Subsection = subsection;
        }
    }

    private sealed class KeyValueLine : LineNode
    {
        public SectionHeaderLine SectionHeader { get; }
        public string Key { get; set; }
        public string Value { get; set; }

        public KeyValueLine(string text, SectionHeaderLine sectionHeader, string key, string value)
        {
            RawText = text;
            SectionHeader = sectionHeader;
            Key = key;
            Value = value;
        }
    }

    private readonly List<LineNode> _lines = new();

    /// <summary>
    /// Parses a Git configuration string.
    /// </summary>
    /// <param name="content">The configuration file content.</param>
    /// <returns>A new <see cref="GitConfigFile"/> instance.</returns>
    public static GitConfigFile Parse(string content)
    {
        var config = new GitConfigFile();
        if (string.IsNullOrEmpty(content))
        {
            return config;
        }

        using var reader = new StringReader(content);
        string? rawLine;
        SectionHeaderLine? currentSection = null;

        while ((rawLine = reader.ReadLine()) != null)
        {
            var trimmed = rawLine.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#' || trimmed[0] == ';')
            {
                config._lines.Add(new BlankOrCommentLine(rawLine));
                continue;
            }

            if (trimmed[0] == '[' && trimmed[^1] == ']')
            {
                var inside = trimmed[1..^1].Trim();
                var quoteIndex = inside.IndexOf('"');
                if (quoteIndex > 0 && inside[^1] == '"')
                {
                    var sec = inside[..quoteIndex].Trim();
                    var sub = inside[(quoteIndex + 1)..^1];
                    currentSection = new SectionHeaderLine(rawLine, sec, sub);
                }
                else
                {
                    currentSection = new SectionHeaderLine(rawLine, inside, null);
                }
                config._lines.Add(currentSection);
                continue;
            }

            if (currentSection != null)
            {
                var equalIndex = trimmed.IndexOf('=');
                if (equalIndex > 0)
                {
                    var key = trimmed[..equalIndex].Trim();
                    var val = trimmed[(equalIndex + 1)..].Trim();
                    if (val.Length >= 2 && val[0] == '"' && val[^1] == '"')
                    {
                        val = val[1..^1];
                    }
                    config._lines.Add(new KeyValueLine(rawLine, currentSection, key, val));
                    continue;
                }
            }

            config._lines.Add(new BlankOrCommentLine(rawLine));
        }

        return config;
    }

    /// <summary>
    /// Reads and parses a Git configuration file from disk.
    /// </summary>
    /// <param name="filePath">Absolute path to the configuration file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="GitConfigFile"/> instance, or an empty one if the file does not exist.</returns>
    public static async Task<GitConfigFile> ReadFromFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return new GitConfigFile();
        }

        var content = await File.ReadAllTextAsync(filePath, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        return Parse(content);
    }

    /// <summary>
    /// Writes the configuration to disk.
    /// </summary>
    /// <param name="filePath">Absolute path to write the configuration to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task WriteToFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var content = Serialize();
        await File.WriteAllTextAsync(filePath, content, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Serializes the configuration into an INI string.
    /// </summary>
    /// <returns>The serialized configuration content.</returns>
    public string Serialize()
    {
        var sb = new StringBuilder();
        foreach (var line in _lines)
        {
            sb.AppendLine(line.RawText);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Retrieves the configuration value for a key path (e.g. "core.bare" or "remote.origin.url").
    /// </summary>
    /// <param name="keyPath">The dotted configuration key path.</param>
    /// <returns>The configuration value, or <see langword="null"/> if not set.</returns>
    public string? GetValue(string keyPath)
    {
        if (!TryParseKeyPath(keyPath, out var section, out var subsection, out var key))
        {
            return null;
        }

        return GetValue(section, subsection, key);
    }

    /// <summary>
    /// Retrieves the configuration value for a section, optional subsection, and key.
    /// </summary>
    /// <param name="section">Section name (case-insensitive).</param>
    /// <param name="subsection">Subsection name (case-sensitive), or null.</param>
    /// <param name="key">Key name (case-insensitive).</param>
    /// <returns>The configuration value, or <see langword="null"/> if not set.</returns>
    public string? GetValue(string section, string? subsection, string key)
    {
        for (var i = _lines.Count - 1; i >= 0; i--)
        {
            if (_lines[i] is KeyValueLine kv && Matches(kv.SectionHeader, section, subsection) && string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return kv.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Sets a configuration value for a dotted key path (e.g. "core.bare" or "remote.origin.url").
    /// </summary>
    /// <param name="keyPath">The dotted configuration key path.</param>
    /// <param name="value">The value to set.</param>
    public void SetValue(string keyPath, string value)
    {
        if (!TryParseKeyPath(keyPath, out var section, out var subsection, out var key))
        {
            throw new ArgumentException($"Invalid Git config key path '{keyPath}'. Must contain at least section and variable name.", nameof(keyPath));
        }

        SetValue(section, subsection, key, value);
    }

    /// <summary>
    /// Sets a configuration value for a section, optional subsection, and key.
    /// </summary>
    /// <param name="section">Section name.</param>
    /// <param name="subsection">Subsection name (or null).</param>
    /// <param name="key">Key name.</param>
    /// <param name="value">The value to set.</param>
    public void SetValue(string section, string? subsection, string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(section);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        // Check if existing key line exists
        for (var i = 0; i < _lines.Count; i++)
        {
            if (_lines[i] is KeyValueLine kv && Matches(kv.SectionHeader, section, subsection) && string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                kv.Value = value;
                kv.RawText = $"\t{kv.Key} = {FormatValue(value)}";
                return;
            }
        }

        // Section exists? Find last entry or section header
        SectionHeaderLine? targetSection = null;
        var insertIndex = -1;

        for (var i = 0; i < _lines.Count; i++)
        {
            if (_lines[i] is SectionHeaderLine sh && Matches(sh, section, subsection))
            {
                targetSection = sh;
                insertIndex = i + 1;
                while (insertIndex < _lines.Count && _lines[insertIndex] is not SectionHeaderLine)
                {
                    insertIndex++;
                }
                break;
            }
        }

        if (targetSection == null)
        {
            var headerText = subsection != null ? $"[{section} \"{subsection}\"]" : $"[{section}]";
            targetSection = new SectionHeaderLine(headerText, section, subsection);
            _lines.Add(targetSection);
            insertIndex = _lines.Count;
        }

        var newKeyLine = new KeyValueLine($"\t{key} = {FormatValue(value)}", targetSection, key, value);
        _lines.Insert(insertIndex, newKeyLine);
    }

    /// <summary>
    /// Removes a configuration key.
    /// </summary>
    /// <param name="keyPath">The dotted configuration key path.</param>
    /// <returns><see langword="true"/> if the key was found and removed; otherwise, <see langword="false"/>.</returns>
    public bool UnsetValue(string keyPath)
    {
        if (!TryParseKeyPath(keyPath, out var section, out var subsection, out var key))
        {
            return false;
        }

        return UnsetValue(section, subsection, key);
    }

    /// <summary>
    /// Removes a configuration key for a section, optional subsection, and key.
    /// </summary>
    /// <param name="section">Section name.</param>
    /// <param name="subsection">Subsection name (or null).</param>
    /// <param name="key">Key name.</param>
    /// <returns><see langword="true"/> if the key was found and removed; otherwise, <see langword="false"/>.</returns>
    public bool UnsetValue(string section, string? subsection, string key)
    {
        var removed = false;
        SectionHeaderLine? sectionHeader = null;

        for (var i = _lines.Count - 1; i >= 0; i--)
        {
            if (_lines[i] is KeyValueLine kv && Matches(kv.SectionHeader, section, subsection) && string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                sectionHeader = kv.SectionHeader;
                _lines.RemoveAt(i);
                removed = true;
            }
        }

        if (removed && sectionHeader != null && subsection != null)
        {
            // If it was a subsection and no keys remain, remove the subsection header
            var hasRemainingKeys = _lines.OfType<KeyValueLine>().Any(kv => kv.SectionHeader == sectionHeader);
            if (!hasRemainingKeys)
            {
                _lines.Remove(sectionHeader);
            }
        }

        return removed;
    }

    /// <summary>
    /// Enumerates all subsection names for a given section (e.g. all remote names for "remote").
    /// </summary>
    /// <param name="section">The section name.</param>
    /// <returns>A collection of distinct subsection names.</returns>
    public IReadOnlyList<string> GetSubsections(string section)
    {
        return _lines
            .OfType<SectionHeaderLine>()
            .Where(sh => string.Equals(sh.Section, section, StringComparison.OrdinalIgnoreCase) && sh.Subsection != null)
            .Select(sh => sh.Subsection!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Renames a subsection (e.g. when renaming a branch or remote).
    /// </summary>
    /// <param name="section">The section name (e.g. "branch" or "remote").</param>
    /// <param name="oldSubsection">The old subsection name.</param>
    /// <param name="newSubsection">The new subsection name.</param>
    /// <returns><see langword="true"/> if the subsection was found and renamed; otherwise, <see langword="false"/>.</returns>
    public bool RenameSubsection(string section, string oldSubsection, string newSubsection)
    {
        var renamed = false;
        foreach (var sh in _lines.OfType<SectionHeaderLine>())
        {
            if (string.Equals(sh.Section, section, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(sh.Subsection, oldSubsection, StringComparison.Ordinal))
            {
                sh.Subsection = newSubsection;
                sh.RawText = $"[{sh.Section} \"{newSubsection}\"]";
                renamed = true;
            }
        }
        return renamed;
    }

    /// <summary>
    /// Removes an entire section or subsection including all its keys.
    /// </summary>
    /// <param name="section">The section name.</param>
    /// <param name="subsection">Optional subsection name.</param>
    /// <returns><see langword="true"/> if removed; otherwise, <see langword="false"/>.</returns>
    public bool RemoveSection(string section, string? subsection = null)
    {
        SectionHeaderLine? targetHeader = null;
        foreach (var sh in _lines.OfType<SectionHeaderLine>())
        {
            if (Matches(sh, section, subsection))
            {
                targetHeader = sh;
                break;
            }
        }

        if (targetHeader == null)
        {
            return false;
        }

        _lines.RemoveAll(l => l == targetHeader || (l is KeyValueLine kv && kv.SectionHeader == targetHeader));
        return true;
    }

    private static bool TryParseKeyPath(string keyPath, out string section, out string? subsection, out string key)
    {
        section = string.Empty;
        subsection = null;
        key = string.Empty;

        if (string.IsNullOrWhiteSpace(keyPath))
        {
            return false;
        }

        var firstDot = keyPath.IndexOf('.');
        if (firstDot <= 0)
        {
            return false;
        }

        var lastDot = keyPath.LastIndexOf('.');
        if (lastDot == firstDot)
        {
            section = keyPath[..firstDot].Trim();
            key = keyPath[(firstDot + 1)..].Trim();
            return section.Length > 0 && key.Length > 0;
        }

        section = keyPath[..firstDot].Trim();
        subsection = keyPath.Substring(firstDot + 1, lastDot - firstDot - 1);
        key = keyPath[(lastDot + 1)..].Trim();
        return section.Length > 0 && subsection.Length > 0 && key.Length > 0;
    }

    private static bool Matches(SectionHeaderLine header, string section, string? subsection)
    {
        if (!string.Equals(header.Section, section, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (subsection == null)
        {
            return header.Subsection == null;
        }

        return string.Equals(header.Subsection, subsection, StringComparison.Ordinal);
    }

    private static string FormatValue(string value)
    {
        if (value.Contains('#') || value.Contains(';') || value.StartsWith(' ') || value.EndsWith(' '))
        {
            return $"\"{value}\"";
        }

        return value;
    }
}
