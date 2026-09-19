using System.IO;
using System.Text;

namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Represents an entry in the Git index (.git/index).
/// </summary>
public sealed class GitIndexEntry
{
    /// <summary>
    /// The file creation timestamp in seconds since Unix epoch.
    /// </summary>
    public uint CtimeSeconds { get; set; }

    /// <summary>
    /// The nanosecond fraction of the creation timestamp.
    /// </summary>
    public uint CtimeNanoseconds { get; set; }

    /// <summary>
    /// The file modification timestamp in seconds since Unix epoch.
    /// </summary>
    public uint MtimeSeconds { get; set; }

    /// <summary>
    /// The nanosecond fraction of the modification timestamp.
    /// </summary>
    public uint MtimeNanoseconds { get; set; }

    /// <summary>
    /// Device identifier.
    /// </summary>
    public uint Dev { get; set; }

    /// <summary>
    /// Inode identifier.
    /// </summary>
    public uint Ino { get; set; }

    /// <summary>
    /// Git file mode (e.g. 33188 for 100644 normal file, 33261 for 100755 executable).
    /// </summary>
    public int FileMode { get; set; }

    /// <summary>
    /// User identifier.
    /// </summary>
    public uint Uid { get; set; }

    /// <summary>
    /// Group identifier.
    /// </summary>
    public uint Gid { get; set; }

    /// <summary>
    /// On-disk file size in bytes, truncated to 32 bits.
    /// </summary>
    public uint FileSize { get; set; }

    /// <summary>
    /// The blob object hash corresponding to the staged file content.
    /// </summary>
    public GitHash Hash { get; set; }

    /// <summary>
    /// Git index entry flags (stage, assume-unchanged, path length).
    /// </summary>
    public ushort Flags { get; set; }

    /// <summary>
    /// Repository-relative path using '/' separators.
    /// </summary>
    public string Path { get; set; }

    /// <summary>
    /// Gets the stage number (0 for normal, 1 for ancestor, 2 for ours, 3 for theirs).
    /// </summary>
    public int Stage => (Flags >> 12) & 0x3;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitIndexEntry"/> class.
    /// </summary>
    public GitIndexEntry(
        string path,
        GitHash hash,
        int fileMode = 33188,
        uint fileSize = 0,
        uint mtimeSeconds = 0,
        uint mtimeNanoseconds = 0,
        uint ctimeSeconds = 0,
        uint ctimeNanoseconds = 0,
        uint dev = 0,
        uint ino = 0,
        uint uid = 0,
        uint gid = 0,
        ushort flags = 0)
    {
        Path = path?.Replace('\\', '/') ?? throw new ArgumentNullException(nameof(path));
        Hash = hash;
        FileMode = fileMode;
        FileSize = fileSize;
        MtimeSeconds = mtimeSeconds;
        MtimeNanoseconds = mtimeNanoseconds;
        CtimeSeconds = ctimeSeconds;
        CtimeNanoseconds = ctimeNanoseconds;
        Dev = dev;
        Ino = ino;
        Uid = uid;
        Gid = gid;
        Flags = flags;
    }

    /// <summary>
    /// Creates a new index entry populated from an on-disk file and its computed blob hash.
    /// </summary>
    /// <param name="relativePath">Repository-relative file path.</param>
    /// <param name="fileInfo">The file info for the on-disk file.</param>
    /// <param name="blobHash">The blob hash of the file content.</param>
    /// <param name="stage">Stage number (0 for normal).</param>
    /// <returns>A new <see cref="GitIndexEntry"/> with stat cache fields populated.</returns>
    /// <remarks>
    /// The <c>ctime</c> fields in the Git index represent the inode metadata-change time, not the
    /// file birth/creation time. On Windows no true ctime is exposed; Git itself uses the last-write
    /// time for both mtime and ctime in that case, which is what we replicate here.
    /// On Unix the executable bit is preserved: a file whose owner-execute permission is set is stored
    /// with mode 100755, all others with 100644.
    /// </remarks>
    public static GitIndexEntry FromFileInfo(string relativePath, FileInfo fileInfo, GitHash blobHash, int stage = 0)
    {
        var mtimeUtc = fileInfo.LastWriteTimeUtc;

        // Git uses last-write time for both mtime and ctime on Windows (no kernel-change-time API).
        // On Unix we could use stat(2) st_ctime, but .NET does not expose it directly; using mtime
        // is the same conservative strategy Git itself uses when inode change time is unavailable.
        var ctimeUtc = mtimeUtc;

        var mtimeSec = (uint)Math.Max(0, new DateTimeOffset(mtimeUtc).ToUnixTimeSeconds());
        var mtimeNano = (uint)((mtimeUtc.Ticks % TimeSpan.TicksPerSecond) * 100);
        var ctimeSec = (uint)Math.Max(0, new DateTimeOffset(ctimeUtc).ToUnixTimeSeconds());
        var ctimeNano = (uint)((ctimeUtc.Ticks % TimeSpan.TicksPerSecond) * 100);

        // Derive mode: 100755 for executable, 100644 for regular.
        // On Windows there are no Unix permission bits, so we always use 100644.
        var fileMode = GetFileMode(fileInfo);

        var pathBytesLen = Encoding.UTF8.GetByteCount(relativePath);
        var pathLen = (ushort)Math.Min(pathBytesLen, 0xFFF);
        var flags = (ushort)((stage & 0x3) << 12 | pathLen);

        return new GitIndexEntry(
            relativePath.Replace('\\', '/'),
            blobHash,
            fileMode: fileMode,
            fileSize: (uint)Math.Min(fileInfo.Length, uint.MaxValue),
            mtimeSeconds: mtimeSec,
            mtimeNanoseconds: mtimeNano,
            ctimeSeconds: ctimeSec,
            ctimeNanoseconds: ctimeNano,
            flags: flags);
    }

    /// <summary>
    /// Returns the Git file mode for the given file: 33261 (100755) when the file is executable on
    /// Unix, or 33188 (100644) otherwise (including all Windows files).
    /// </summary>
    private static int GetFileMode(FileInfo fileInfo)
    {
        if (!OperatingSystem.IsWindows())
        {
            // On Unix, inspect owner-execute bit via FileInfo.UnixFileMode (available since .NET 7).
            // We use the property on the existing FileInfo rather than File.GetUnixFileMode(path)
            // because the latter opens a new FileStream internally, which can throw IOException
            // when the file was previously opened with FileOptions.SequentialScan.
            try
            {
                var mode = fileInfo.UnixFileMode;
                if ((mode & UnixFileMode.UserExecute) != 0)
                {
                    return 33261; // 100755
                }
            }
            catch (Exception)
            {
                // Fall through to default if the API fails for any reason.
            }
        }

        return 33188; // 100644
    }
}

