using System.Text;

namespace Pmad.Git.LocalRepositories;

internal readonly record struct TreeEntryData(string Name, int Mode, GitHash Hash, byte[] EncodedName)
{
    public TreeEntryData(string name, int mode, GitHash hash)
        : this(name, mode, hash, Encoding.UTF8.GetBytes(name))
    {
    }
}
