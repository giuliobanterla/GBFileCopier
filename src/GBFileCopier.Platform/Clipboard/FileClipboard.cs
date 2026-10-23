namespace GBFileCopier.Platform.Clipboard;

/// <summary>Files on the system clipboard.</summary>
/// <param name="Paths">Full paths of the files and folders.</param>
/// <param name="IsCut">
/// The files were cut, so pasting should move them. Only Windows Explorer marks this:
/// on macOS the Finder decides when pasting (⌘V copies, ⌥⌘V moves), so it is always false.
/// </param>
public sealed record ClipboardFiles(IReadOnlyList<string> Paths, bool IsCut);

/// <summary>
/// Reads and writes files on the system clipboard, the same one used by the Finder and
/// Windows Explorer: files copied there with ⌘C / Ctrl+C can be pasted with GBFileCopier.
/// </summary>
public static class FileClipboard
{
    public static bool IsSupported => OperatingSystem.IsMacOS() || OperatingSystem.IsWindows();

    /// <summary>The files on the clipboard, or null if it holds no files.</summary>
    public static ClipboardFiles? Read()
    {
        if (OperatingSystem.IsMacOS())
        {
            return new MacFileClipboard().Read();
        }

        if (OperatingSystem.IsWindows())
        {
            return WindowsFileClipboard.Read();
        }

        throw new PlatformNotSupportedException("The file clipboard is supported on macOS and Windows");
    }

    /// <summary>Puts files on the clipboard, as copied (or cut, on Windows) in the file manager.</summary>
    public static void Write(IReadOnlyList<string> paths, bool cut = false)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var fullPaths = paths.Select(Path.GetFullPath).ToList();

        if (OperatingSystem.IsMacOS())
        {
            new MacFileClipboard().Write(fullPaths);
        }
        else if (OperatingSystem.IsWindows())
        {
            WindowsFileClipboard.Write(fullPaths, cut);
        }
        else
        {
            throw new PlatformNotSupportedException("The file clipboard is supported on macOS and Windows");
        }
    }

    /// <summary>
    /// Empties the clipboard. Explorer does this after a cut and paste, because the files
    /// are no longer where the clipboard says.
    /// </summary>
    public static void Clear()
    {
        if (OperatingSystem.IsMacOS())
        {
            new MacFileClipboard().Clear();
        }
        else if (OperatingSystem.IsWindows())
        {
            WindowsFileClipboard.Clear();
        }
    }
}
