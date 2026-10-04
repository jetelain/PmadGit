using Pmad.Git.LocalRepositories.Utilities;

namespace Pmad.Git.LocalRepositories.Test;

public class FileHelperTests
{
    [Fact]
    public void SafeDelete_ExistingFile_DeletesFile()
    {
        var tempFile = Path.GetTempFileName();
        Assert.True(File.Exists(tempFile));

        FileHelper.SafeDelete(tempFile);

        Assert.False(File.Exists(tempFile));
    }

    [Fact]
    public void SafeDelete_NonExistingFile_DoesNotThrow()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".tmp");
        Assert.False(File.Exists(tempFile));

        FileHelper.SafeDelete(tempFile);
    }
}
