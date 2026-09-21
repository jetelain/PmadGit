using System;
using System.IO;

namespace Pmad.Git.LocalRepositories.Config;

/// <summary>
/// Resolves Git configuration file paths according to Git's environment variable precedence and platform conventions.
/// </summary>
internal static class GitConfigEnvironment
{
    /// <summary>
    /// Resolves the global Git configuration file path.
    /// Precedence:
    /// 1. $GIT_CONFIG_GLOBAL (if set)
    /// 2. $XDG_CONFIG_HOME/git/config (if exists)
    /// 3. ~/.config/git/config (if exists)
    /// 4. ~/.gitconfig (default)
    /// </summary>
    public static string GetGlobalConfigPath()
    {
        var envGlobal = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
        if (!string.IsNullOrWhiteSpace(envGlobal))
        {
            return envGlobal;
        }

        var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(xdgConfigHome))
        {
            var xdgPath = Path.Combine(xdgConfigHome, "git", "config");
            if (File.Exists(xdgPath))
            {
                return xdgPath;
            }
        }
        else
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(userProfile))
            {
                var defaultXdgPath = Path.Combine(userProfile, ".config", "git", "config");
                if (File.Exists(defaultXdgPath))
                {
                    return defaultXdgPath;
                }
            }
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".gitconfig");
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
