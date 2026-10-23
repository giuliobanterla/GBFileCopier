using Microsoft.Win32.SafeHandles;

namespace GBFileCopier.Core.Copying;

/// <summary>
/// Operations that need native APIs to be done properly. The GBFileCopier.Platform project
/// implements them for each operating system; <see cref="ManagedFileSystemPlatform"/> is the
/// portable fallback.
/// </summary>
public interface IFileSystemPlatform
{
    /// <summary>Short description shown by "gbcopy info".</summary>
    string Name { get; }

    /// <summary>
    /// Opens a file to read it back for verification, bypassing the operating system cache where
    /// possible, so that the data comes from the disk and not from memory. Reads must use buffers,
    /// offsets and sizes aligned to 4096 bytes.
    /// </summary>
    SafeFileHandle OpenForVerification(string path);

    /// <summary>Waits until the data written through the handle is on the physical disk.</summary>
    void FlushToDisk(SafeFileHandle handle);

    /// <summary>
    /// Moves a file by renaming it, which is instant, when source and destination are on the
    /// same volume. Returns false if they are not: the caller then copies and deletes.
    /// </summary>
    /// <exception cref="IOException">The rename failed for another reason, for example the destination exists and <paramref name="overwrite"/> is false.</exception>
    bool TryMoveWithinVolume(string sourcePath, string destinationPath, bool overwrite);
}

/// <summary>Portable implementation with plain .NET APIs: cached reads, no rename optimization.</summary>
public sealed class ManagedFileSystemPlatform : IFileSystemPlatform
{
    public static ManagedFileSystemPlatform Instance { get; } = new();

    public string Name => "portable (.NET)";

    public SafeFileHandle OpenForVerification(string path) =>
        File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.Asynchronous | FileOptions.SequentialScan);

    public void FlushToDisk(SafeFileHandle handle)
    {
        // FileStream is the only portable way to fsync / FlushFileBuffers a handle.
        // It takes ownership of the handle, which the caller disposes again: harmless.
        using var stream = new FileStream(handle, FileAccess.Write, bufferSize: 0, isAsync: handle.IsAsync);
        stream.Flush(flushToDisk: true);
    }

    // File.Move would silently fall back to copy + delete across volumes, without verification
    public bool TryMoveWithinVolume(string sourcePath, string destinationPath, bool overwrite) => false;
}
