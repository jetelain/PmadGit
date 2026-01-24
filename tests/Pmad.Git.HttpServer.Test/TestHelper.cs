using System.Diagnostics;

namespace Pmad.Git.HttpServer.Test;

/// <summary>
/// Helper methods for test cleanup and other common test operations.
/// </summary>
internal static class TestHelper
{
    /// <summary>
    /// Attempts to delete a directory recursively, ignoring any exceptions.
    /// This is useful for test cleanup where failures should not affect test results.
    /// </summary>
    /// <param name="path">The directory path to delete.</param>
    internal static void TryDeleteDirectory(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to delete test directory '{path}': {ex}");
            // Ignore cleanup failures in tests
        }
    }
}
