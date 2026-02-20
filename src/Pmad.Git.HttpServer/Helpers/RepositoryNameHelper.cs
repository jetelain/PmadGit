using System.Text.RegularExpressions;

namespace Pmad.Git.HttpServer.Helpers;

/// <summary>
/// Helper class for validating repository names to ensure secure file system access.
/// </summary>
public static partial class RepositoryNameHelper
{
    [GeneratedRegex(@"^[a-zA-Z0-9\-_]+(?:/[a-zA-Z0-9\-_]+)*$")]
    private static partial Regex DefaultRepositoryNameRegex();

    /// <summary>
    /// Default repository name validator that only allows alphanumeric characters, hyphens, underscores, and forward slashes between path segments.
    /// This prevents directory traversal and injection attacks by disallowing leading/trailing/repeated slashes and other special characters.
    /// </summary>
    /// <param name="name">The repository name to validate.</param>
    /// <returns>True if the repository name is valid; otherwise, false.</returns>
    public static bool DefaultRepositoryNameValidator(string name)
    {
        return !string.IsNullOrEmpty(name) && DefaultRepositoryNameRegex().IsMatch(name);
    }

}
