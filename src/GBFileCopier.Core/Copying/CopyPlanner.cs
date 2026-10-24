using System.Globalization;
using GBFileCopier.Core.Resources;

namespace GBFileCopier.Core.Copying;

/// <summary>A file, folder or symbolic link to recreate at the destination.</summary>
internal sealed record CopyItem(
    CopyItemKind Kind,
    string SourcePath,
    string DestinationPath,
    long Length,
    DateTime LastWriteTimeUtc,
    string? LinkTarget = null);

/// <summary>A source that cannot be copied, found while scanning.</summary>
internal sealed record PlanError(string SourcePath, string DestinationPath, string Message);

internal sealed class CopyPlan
{
    /// <summary>Folders to create, parents before children.</summary>
    public List<CopyItem> Directories { get; } = [];

    /// <summary>Files and symbolic links, in copy order.</summary>
    public List<CopyItem> Files { get; } = [];

    public List<PlanError> Errors { get; } = [];

    public long TotalBytes => Files.Sum(f => f.Length);
}

/// <summary>
/// Expands the sources into the list of folders and files to copy. Folders are walked
/// recursively; symbolic links inside them are copied as links, not followed.
/// </summary>
internal static class CopyPlanner
{
    private static readonly EnumerationOptions Enumeration = new()
    {
        AttributesToSkip = 0, // include hidden and system files
        IgnoreInaccessible = false,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };

    // Windows and macOS file systems are usually case-insensitive
    internal static StringComparison PathComparison { get; } =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static CopyPlan Create(CopyRequest request, CancellationToken cancellationToken)
    {
        var plan = new CopyPlan();
        var destinationRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Destination));

        foreach (var source in request.Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddSource(plan, source, destinationRoot, cancellationToken);
        }

        return plan;
    }

    private static void AddSource(CopyPlan plan, string source, string destinationRoot, CancellationToken cancellationToken)
    {
        var sourcePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        var name = Path.GetFileName(sourcePath);
        var target = Path.Combine(destinationRoot, name);

        if (name.Length == 0)
        {
            plan.Errors.Add(new(sourcePath, destinationRoot, Messages.DriveNotSupported));
            return;
        }

        if (string.Equals(sourcePath, target, PathComparison))
        {
            plan.Errors.Add(new(sourcePath, target, Messages.SameSourceAndDestination));
            return;
        }

        // A symbolic link is copied as a link, like the Finder and cp -R do. Checked first:
        // File.Exists and Directory.Exists follow links, while FileInfo describes the link itself
        // (its length is that of the target path), so the two would not match.
        var linkTarget = GetLinkTarget(sourcePath);
        if (linkTarget is not null)
        {
            plan.Files.Add(new(CopyItemKind.SymbolicLink, sourcePath, target, 0, File.GetLastWriteTimeUtc(sourcePath), linkTarget));
        }
        else if (File.Exists(sourcePath))
        {
            var file = new FileInfo(sourcePath);
            plan.Files.Add(new(CopyItemKind.File, sourcePath, target, file.Length, file.LastWriteTimeUtc));
        }
        else if (Directory.Exists(sourcePath))
        {
            if (IsSameOrInside(destinationRoot, sourcePath))
            {
                plan.Errors.Add(new(sourcePath, target, Messages.DestinationInsideSource));
                return;
            }

            AddDirectory(plan, new DirectoryInfo(sourcePath), target, cancellationToken);
        }
        else
        {
            plan.Errors.Add(new(sourcePath, target, Messages.SourceDoesNotExist));
        }
    }

    private static void AddDirectory(CopyPlan plan, DirectoryInfo directory, string target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        plan.Directories.Add(new(CopyItemKind.Directory, directory.FullName, target, 0, directory.LastWriteTimeUtc));

        List<FileSystemInfo> entries;
        try
        {
            entries = [.. directory.EnumerateFileSystemInfos("*", Enumeration)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            plan.Errors.Add(new(directory.FullName, target, string.Format(CultureInfo.CurrentCulture, Messages.FolderCannotBeRead, ex.Message)));
            return;
        }

        // Files first, then subfolders: a stable, predictable order
        entries.Sort((a, b) =>
        {
            var byKind = (a is DirectoryInfo).CompareTo(b is DirectoryInfo);
            return byKind != 0 ? byKind : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        });

        foreach (var entry in entries)
        {
            var entryTarget = Path.Combine(target, entry.Name);
            var linkTarget = entry.Attributes.HasFlag(FileAttributes.ReparsePoint) ? entry.LinkTarget : null;

            if (linkTarget is not null)
            {
                plan.Files.Add(new(CopyItemKind.SymbolicLink, entry.FullName, entryTarget, 0, entry.LastWriteTimeUtc, linkTarget));
            }
            else if (entry is DirectoryInfo subdirectory)
            {
                AddDirectory(plan, subdirectory, entryTarget, cancellationToken);
            }
            else if (entry is FileInfo file)
            {
                plan.Files.Add(new(CopyItemKind.File, file.FullName, entryTarget, file.Length, file.LastWriteTimeUtc));
            }
        }
    }

    private static string? GetLinkTarget(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Attributes.HasFlag(FileAttributes.ReparsePoint) ? info.LinkTarget : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null; // missing or unreadable: reported by the checks that follow
        }
    }

    private static bool IsSameOrInside(string path, string folder) =>
        string.Equals(path, folder, PathComparison)
        || path.StartsWith(folder + Path.DirectorySeparatorChar, PathComparison);
}
