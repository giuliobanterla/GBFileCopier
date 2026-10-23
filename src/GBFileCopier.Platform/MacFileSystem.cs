using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using GBFileCopier.Core.Copying;
using Microsoft.Win32.SafeHandles;

namespace GBFileCopier.Platform;

/// <summary>macOS: cache eviction and F_NOCACHE for verification, F_FULLFSYNC for flushing, renamex_np for moves.</summary>
[SupportedOSPlatform("macos")]
internal sealed partial class MacFileSystem : IFileSystemPlatform
{
    private const int F_NOCACHE = 48;
    private const int F_FULLFSYNC = 51;
    private const uint RENAME_EXCL = 0x4;
    private const int EXDEV = 18;
    private const int PROT_READ = 0x1;
    private const int MAP_SHARED = 0x1;
    private const int MS_SYNC = 0x10;
    private const int MS_INVALIDATE = 0x2;
    private const long EvictWindow = 1L << 30; // map at most 1 GB at a time

    public string Name => "macOS native (cache eviction + F_NOCACHE, F_FULLFSYNC, renamex_np)";

    public SafeFileHandle OpenForVerification(string path)
    {
        var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.Asynchronous | FileOptions.SequentialScan);
        // F_NOCACHE alone still serves pages that are already in memory, as those of a file just
        // written are: they must be dropped first. Measured with iostat on macOS 26: without the
        // eviction a 1.5 GB verification read 0 MB from the disk, with it the whole file.
        // Both steps are best effort: if they fail (some network file systems) the verification
        // still works, only from the cache.
        EvictFromCache(handle);
        Fcntl(handle, F_NOCACHE, 1);
        return handle;
    }

    public void FlushToDisk(SafeFileHandle handle)
    {
        // fsync on macOS only hands the data to the drive, which may keep it in its own cache:
        // F_FULLFSYNC also asks the drive to write it to permanent storage.
        if (Fcntl(handle, F_FULLFSYNC, 0) == 0)
        {
            return;
        }

        // Not supported by every file system (for example SMB shares): plain fsync
        if (fsync(Fd(handle)) != 0)
        {
            throw NewIOException("Cannot flush the file to disk");
        }
    }

    public bool TryMoveWithinVolume(string sourcePath, string destinationPath, bool overwrite)
    {
        // RENAME_EXCL makes "fail if the destination exists" atomic
        if (renamex_np(sourcePath, destinationPath, overwrite ? 0 : RENAME_EXCL) == 0)
        {
            return true;
        }

        if (Marshal.GetLastPInvokeError() == EXDEV)
        {
            return false; // different volumes
        }

        throw NewIOException($"Cannot move '{sourcePath}' to '{destinationPath}'");
    }

    /// <summary>Drops the file's pages from the unified buffer cache, writing dirty ones first (MS_SYNC).</summary>
    private static void EvictFromCache(SafeFileHandle handle)
    {
        var length = RandomAccess.GetLength(handle);
        for (long offset = 0; offset < length; offset += EvictWindow)
        {
            var size = (nuint)Math.Min(EvictWindow, length - offset);
            var address = mmap(0, size, PROT_READ, MAP_SHARED, Fd(handle), offset);
            if (address == -1)
            {
                return;
            }

            _ = msync(address, size, MS_SYNC | MS_INVALIDATE);
            _ = munmap(address, size);
        }
    }

    private static int Fd(SafeFileHandle handle) => (int)handle.DangerousGetHandle();

    private static IOException NewIOException(string message)
    {
        var errno = Marshal.GetLastPInvokeError();
        return new IOException($"{message}: {Marshal.GetPInvokeErrorMessage(errno)}", errno);
    }

    /// <summary>
    /// fcntl is variadic. On Apple Silicon variadic arguments go on the stack, while P/Invoke
    /// passes them in registers like normal ones: six dummy arguments fill the remaining
    /// registers, so that the real argument ends up on the stack where fcntl looks for it.
    /// On x64 variadic integers are passed like normal ones.
    /// </summary>
    internal static int Fcntl(SafeFileHandle handle, int command, int argument)
    {
        var fd = Fd(handle);
        return RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? fcntl_arm64(fd, command, 0, 0, 0, 0, 0, 0, argument)
            : fcntl_x64(fd, command, argument);
    }

    [LibraryImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static partial int fcntl_x64(int fd, int command, int argument);

    [LibraryImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static partial int fcntl_arm64(int fd, int command, nint x2, nint x3, nint x4, nint x5, nint x6, nint x7, nint argument);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int fsync(int fd);

    [LibraryImport("libc", SetLastError = true)]
    private static partial nint mmap(nint address, nuint length, int protection, int flags, int fd, long offset);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int msync(nint address, nuint length, int flags);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int munmap(nint address, nuint length);

    [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int renamex_np(string from, string to, uint flags);
}
