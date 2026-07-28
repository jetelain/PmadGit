namespace Pmad.Git.Cli.Test.Infrastructure;

internal static class GitCliTestHelper
{
    /// <summary>
    /// Attempts to delete a directory recursively, ignoring any exceptions.
    /// This is useful for test cleanup where failures should not affect test results.
    /// </summary>
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

            const int maxAttempts = 3;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    if (attempt > 0)
                    {
                        Thread.Sleep(100 * attempt);
                    }

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
                    return;
                }
                catch when (attempt < maxAttempts - 1)
                {
                    // Retry
                }
            }
        }
        catch
        {
            // Ignore cleanup failures
        }
    }
}
