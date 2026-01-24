using System.Diagnostics;
using Microsoft.Extensions.Hosting;

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
            if (!Directory.Exists(path))
            {
                return;
            }

            // Try multiple times with delays to handle locked files
            const int maxAttempts = 3;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    // On non-Windows, files might be locked by processes that haven't fully exited
                    if (attempt > 0)
                    {
                        System.Threading.Thread.Sleep(100 * attempt);
                    }

                    // First, make sure all files are not read-only
                    foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            File.SetAttributes(file, FileAttributes.Normal);
                        }
                        catch
                        {
                            // Ignore errors setting attributes
                        }
                    }

                    Directory.Delete(path, recursive: true);
                    return; // Success
                }
                catch (Exception ex) when (attempt < maxAttempts - 1)
                {
                    Debug.WriteLine($"Attempt {attempt + 1} to delete test directory '{path}' failed: {ex.Message}");
                    // Continue to next attempt
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to delete test directory '{path}': {ex}");
            // Ignore cleanup failures in tests - directory will be cleaned up eventually by temp cleanup
        }
    }

    internal static void SafeStop(IHost? host)
    {
        if (host != null)
        {
            try
            {
                // Use Task.Run to avoid potential deadlocks on sync disposal
                Task.Run(async () =>
                {
                    await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }).GetAwaiter().GetResult();
            }
            catch
            {
                // Ignore errors during shutdown
            }
            finally
            {
                host.Dispose();
                host = null;
            }

            // Give the server time to fully release resources
            System.Threading.Thread.Sleep(100);
        }
    }
}
