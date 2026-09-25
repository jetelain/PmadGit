using System.Text.RegularExpressions;

namespace Pmad.Git.HttpServer.Helpers;

/// <summary>
/// Helper class for validating repository names to ensure secure file system access.
/// </summary>
public static partial class RepositoryNameHelper
{
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    [GeneratedRegex(@"^[a-zA-Z0-9\-_]+(?:/[a-zA-Z0-9\-_]+)*$")]
    private static partial Regex DefaultRepositoryNameRegex();

    /// <summary>
    /// Default repository name validator that only allows alphanumeric characters, hyphens, underscores, and forward slashes between path segments.
    /// This prevents directory traversal and injection attacks by disallowing leading/trailing/repeated slashes and other special characters.
    /// Also rejects DOS/Windows reserved device names.
    /// </summary>
    /// <param name="name">The repository name to validate.</param>
    /// <returns>True if the repository name is valid; otherwise, false.</returns>
    public static bool DefaultRepositoryNameValidator(string name)
    {
        if (string.IsNullOrEmpty(name) || !DefaultRepositoryNameRegex().IsMatch(name))
        {
            return false;
        }

        foreach (var segment in name.Split('/'))
        {
            if (ReservedDeviceNames.Contains(segment))
            {
                return false;
            }
        }

        return true;
    }
}
