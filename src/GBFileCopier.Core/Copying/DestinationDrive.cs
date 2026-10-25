namespace GBFileCopier.Core.Copying;

/// <summary>Finds the drive (volume) a destination folder is on, to queue the jobs per drive.</summary>
public static class DestinationDrive
{
    /// <summary>Drive keys are paths: case-insensitive on Windows and macOS, as their file systems usually are.</summary>
    public static StringComparer KeyComparer { get; } =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// The mount point of the volume that holds <paramref name="path"/>, for example <c>D:\</c>,
    /// <c>\\server\share</c> or <c>/Volumes/Backup</c>. The folder does not have to exist yet.
    /// </summary>
    public static string KeyFor(string path) => KeyFor(path, MountPoints());

    /// <summary>As <see cref="KeyFor(string)"/>, with the given mount points (for tests).</summary>
    public static string KeyFor(string path, IEnumerable<string> mountPoints)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(mountPoints);
        var fullPath = WithoutDevicePrefix(Path.GetFullPath(path));

        // The longest mount point that contains the path: /Volumes/Backup rather than /.
        // Roots such as "/" and "C:\" keep their separator when trimmed.
        var best = mountPoints
            .Where(m => !IsMacSystemVolume(m))
            .Select(m => Path.TrimEndingDirectorySeparator(m))
            .Where(m => m.Length > 0 && Contains(m, fullPath))
            .MaxBy(m => m.Length);
        return best ?? (Path.GetPathRoot(fullPath) is { Length: > 0 } root ? root : fullPath);
    }

    /// <summary>\\?\D:\x is D:\x and \\?\UNC\server\share is \\server\share, so that they get the same key.</summary>
    private static string WithoutDevicePrefix(string path) =>
        path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ? @"\\" + path[8..]
        : path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal) ? path[4..]
        : path;

    /// <summary>
    /// macOS lists its own volumes under /System/Volumes; /System/Volumes/Data is the same disk as
    /// "/" (/Users is a firmlink into it): leaving them out keys both as "/".
    /// </summary>
    private static bool IsMacSystemVolume(string mountPoint) =>
        mountPoint.StartsWith("/System/Volumes/", StringComparison.Ordinal);

    private static bool Contains(string mountPoint, string path)
    {
        var comparison = KeyComparer == StringComparer.Ordinal ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (!path.StartsWith(mountPoint, comparison))
        {
            return false;
        }

        // "/Volumes/Backup" contains "/Volumes/Backup/x" but not "/Volumes/Backup2"; "/" and "C:\" end with a separator
        return path.Length == mountPoint.Length
            || Path.EndsInDirectorySeparator(mountPoint)
            || path[mountPoint.Length] == Path.DirectorySeparatorChar
            || path[mountPoint.Length] == Path.AltDirectorySeparatorChar;
    }

    private static IEnumerable<string> MountPoints()
    {
        try
        {
            return [.. DriveInfo.GetDrives().Select(d => d.Name)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
