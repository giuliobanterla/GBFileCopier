namespace GBFileCopier.Platform;

/// <summary>What Explorer showed when Ctrl+V was pressed, read by the keyboard hook.</summary>
/// <param name="ForegroundClass">Window class of the foreground window.</param>
/// <param name="FocusClass">Window class of the control with the keyboard focus.</param>
/// <param name="FocusParentClass">Window class of that control's parent.</param>
/// <param name="Ctrl">Ctrl is held.</param>
/// <param name="Shift">Shift is held.</param>
/// <param name="Alt">Alt is held.</param>
/// <param name="Win">A Windows key is held.</param>
/// <param name="ClipboardHasFiles">The clipboard holds files (CF_HDROP).</param>
public readonly record struct PasteKeyContext(
    string ForegroundClass,
    string? FocusClass,
    string? FocusParentClass,
    bool Ctrl,
    bool Shift,
    bool Alt,
    bool Win,
    bool ClipboardHasFiles);

/// <summary>
/// When Ctrl+V in Explorer is taken over by GBFileCopier: only plain Ctrl+V, in an Explorer window,
/// with the focus in the list of files (not while renaming, or in the address or search box), and
/// with files on the clipboard. Everything else goes to Explorer as usual. Plain checks only: the
/// hook must answer within a few milliseconds.
/// </summary>
public static class ExplorerPasteRules
{
    /// <summary>Explorer windows (CabinetWClass; ExploreWClass in older versions).</summary>
    public static bool IsExplorerWindow(string windowClass) =>
        windowClass is "CabinetWClass" or "ExploreWClass";

    public static bool ShouldIntercept(PasteKeyContext context) =>
        context is { Ctrl: true, Shift: false, Alt: false, Win: false, ClipboardHasFiles: true }
        && IsExplorerWindow(context.ForegroundClass)
        // The items view: a DirectUIHWND inside SHELLDLL_DefView. A rename box or the address bar is an edit control.
        && context.FocusClass == "DirectUIHWND"
        && context.FocusParentClass == "SHELLDLL_DefView";

    /// <summary>
    /// Pasting into the folder the files come from makes Explorer create "name - Copy": leave that to
    /// Explorer, GBFileCopier would only report that source and destination are the same.
    /// </summary>
    public static bool IsSameFolder(string destination, IReadOnlyList<string> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        return sources.Any(source =>
            Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(source))) is { } parent
            && string.Equals(Path.TrimEndingDirectorySeparator(parent), folder, StringComparison.OrdinalIgnoreCase));
    }
}
