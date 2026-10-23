using GBFileCopier.Core.Copying;

namespace GBFileCopier.Platform;

/// <summary>Picks the native file system implementation for the current operating system.</summary>
public static class PlatformFileSystem
{
    public static IFileSystemPlatform Current { get; } = Create();

    private static IFileSystemPlatform Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsFileSystem();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacFileSystem();
        }

        return ManagedFileSystemPlatform.Instance;
    }
}
