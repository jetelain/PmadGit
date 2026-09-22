using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Pmad.Git.LocalRepositories.Config;

/// <summary>
/// Resolves Git configuration file paths according to Git's environment variable precedence and platform conventions,
/// and provides process-wide synchronization for global configuration file modifications.
/// </summary>
internal static class GitConfigEnvironment
{
    private static readonly SemaphoreSlim _globalConfigLock = new(1, 1);

    /// <summary>
    /// Acquires a process-wide asynchronous exclusive lock for writing to global Git configuration files.
    /// </summary>
    public static async Task<IDisposable> LockGlobalConfigAsync(CancellationToken cancellationToken = default)
    {
        await _globalConfigLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new LockReleaser(_globalConfigLock);
    }

    private sealed class LockReleaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private int _disposed;

        public LockReleaser(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _semaphore.Release();
            }
        }
    }

    /// <summary>
    /// Gets the user's home directory, checking the <c>HOME</c> environment variable before <see cref="Environment.SpecialFolder.UserProfile"/>.
    /// </summary>
    public static string GetHomeDirectory()
    {
        var home = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrWhiteSpace(home))
        {
            return home;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    /// <summary>
    /// Gets the path to the XDG Git configuration file ($XDG_CONFIG_HOME/git/config or ~/.config/git/config).
    /// </summary>
    public static string? GetXdgConfigPath()
    {
        var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(xdgConfigHome))
        {
            return Path.Combine(xdgConfigHome, "git", "config");
        }

        var home = GetHomeDirectory();
        if (!string.IsNullOrEmpty(home))
        {
            return Path.Combine(home, ".config", "git", "config");
        }

        return null;
    }

    /// <summary>
    /// Resolves the global Git configuration file paths in order of precedence (highest priority first).
    /// Precedence:
    /// 1. $GIT_CONFIG_GLOBAL (if set, overrides both global files)
    /// 2. ~/.gitconfig (user-specific file, higher priority than XDG)
    /// 3. $XDG_CONFIG_HOME/git/config or ~/.config/git/config (second user-specific file)
    /// </summary>
    public static IReadOnlyList<string> GetGlobalConfigPaths()
    {
        var envGlobal = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
        if (!string.IsNullOrWhiteSpace(envGlobal))
        {
            return new[] { envGlobal };
        }

        var list = new List<string>(2);
        var home = GetHomeDirectory();
        if (!string.IsNullOrEmpty(home))
        {
            list.Add(Path.Combine(home, ".gitconfig"));
        }

        var xdg = GetXdgConfigPath();
        if (!string.IsNullOrEmpty(xdg) && !list.Contains(xdg, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(xdg);
        }

        return list;
    }

    /// <summary>
    /// Resolves the destination file path when writing to global configuration.
    /// Matches Git's behavior: writes to $GIT_CONFIG_GLOBAL if set; else ~/.gitconfig if it exists;
    /// else XDG config file if it exists; else ~/.gitconfig.
    /// </summary>
    public static string GetGlobalConfigWritePath()
    {
        var envGlobal = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
        if (!string.IsNullOrWhiteSpace(envGlobal))
        {
            return envGlobal;
        }

        var home = GetHomeDirectory();
        var homeConfig = !string.IsNullOrEmpty(home) ? Path.Combine(home, ".gitconfig") : null;
        if (homeConfig != null && File.Exists(homeConfig))
        {
            return homeConfig;
        }

        var xdg = GetXdgConfigPath();
        if (xdg != null && File.Exists(xdg))
        {
            return xdg;
        }

        return homeConfig ?? (xdg ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gitconfig"));
    }

    /// <summary>
    /// Resolves the system Git configuration file path, or <see langword="null"/> if disabled or not found.
    /// Precedence:
    /// 1. $GIT_CONFIG_NOSYSTEM=1 or true -> returns null
    /// 2. $GIT_CONFIG_SYSTEM (if set)
    /// 3. Platform-specific default paths (%PROGRAMDATA%\Git\config, Program Files\Git\etc\gitconfig, /etc/gitconfig)
    /// </summary>
    public static string? GetSystemConfigPath()
    {
        var noSystem = Environment.GetEnvironmentVariable("GIT_CONFIG_NOSYSTEM");
        if (string.Equals(noSystem, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(noSystem, "true", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var envSystem = Environment.GetEnvironmentVariable("GIT_CONFIG_SYSTEM");
        if (!string.IsNullOrWhiteSpace(envSystem))
        {
            return envSystem;
        }

        if (OperatingSystem.IsWindows())
        {
            // Check %PROGRAMDATA%\Git\config
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (!string.IsNullOrEmpty(programData))
            {
                var pdGit = Path.Combine(programData, "Git", "config");
                if (File.Exists(pdGit))
                {
                    return pdGit;
                }
            }

            // Check C:\Program Files\Git\etc\gitconfig
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrEmpty(programFiles))
            {
                var pfGit = Path.Combine(programFiles, "Git", "etc", "gitconfig");
                if (File.Exists(pfGit))
                {
                    return pfGit;
                }
            }
        }
        else
        {
            const string etcGitConfig = "/etc/gitconfig";
            if (File.Exists(etcGitConfig))
            {
                return etcGitConfig;
            }
        }

        return null;
    }
}
