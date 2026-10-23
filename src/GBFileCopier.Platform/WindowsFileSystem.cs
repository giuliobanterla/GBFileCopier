using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using GBFileCopier.Core.Copying;
using Microsoft.Win32.SafeHandles;

namespace GBFileCopier.Platform;

/// <summary>Windows: FILE_FLAG_NO_BUFFERING for verification, FlushFileBuffers, MoveFileEx for moves.</summary>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsFileSystem : IFileSystemPlatform
{
    // FILE_FLAG_NO_BUFFERING: not in the enum, but .NET passes it through to CreateFile
    private const FileOptions NoBuffering = (FileOptions)0x20000000;

    private const uint MOVEFILE_REPLACE_EXISTING = 0x1;
    private const int ERROR_NOT_SAME_DEVICE = 17;

    public string Name => "Windows native (FILE_FLAG_NO_BUFFERING, FlushFileBuffers, MoveFileEx)";

    public SafeFileHandle OpenForVerification(string path) =>
        // Unbuffered reads bypass the system cache: Windows first writes any cached data, then reads from the disk
        File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            FileOptions.Asynchronous | FileOptions.SequentialScan | NoBuffering);

    public void FlushToDisk(SafeFileHandle handle)
    {
        if (!FlushFileBuffers(handle))
        {
            throw NewIOException("Cannot flush the file to disk");
        }
    }

    public bool TryMoveWithinVolume(string sourcePath, string destinationPath, bool overwrite)
    {
        // Without MOVEFILE_COPY_ALLOWED, MoveFileEx only renames, and refuses to cross volumes
        if (MoveFileEx(ToExtendedPath(sourcePath), ToExtendedPath(destinationPath), overwrite ? MOVEFILE_REPLACE_EXISTING : 0))
        {
            return true;
        }

        if (Marshal.GetLastPInvokeError() == ERROR_NOT_SAME_DEVICE)
        {
            return false;
        }

        throw NewIOException($"Cannot move '{sourcePath}' to '{destinationPath}'");
    }

    /// <summary>The \\?\ prefix lifts the 260 character limit for direct Win32 calls.</summary>
    internal static string ToExtendedPath(string path)
    {
        path = Path.GetFullPath(path);
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            return path;
        }

        return path.StartsWith(@"\\", StringComparison.Ordinal)
            ? @"\\?\UNC\" + path[2..]
            : @"\\?\" + path;
    }

    private static IOException NewIOException(string message)
    {
        var error = Marshal.GetLastPInvokeError();
        return new IOException($"{message}: {Marshal.GetPInvokeErrorMessage(error)}", Marshal.GetHRForLastWin32Error());
    }

    [LibraryImport("kernel32.dll", EntryPoint = "MoveFileExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MoveFileEx(string existingFileName, string newFileName, uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FlushFileBuffers(SafeFileHandle file);
}
