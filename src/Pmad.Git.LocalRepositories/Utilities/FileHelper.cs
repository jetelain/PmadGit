namespace Pmad.Git.LocalRepositories.Utilities;

internal static class FileHelper
{
    internal static void SafeDelete(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
            // Ignore all exceptions; this is a best-effort cleanup.
        }
    }
}
