using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace GBFileCopier.Platform;

/// <summary>
/// The folder open in an Explorer window (its active tab on Windows 11), through the Shell's COM
/// objects: ShellWindows → IShellBrowser → IFolderView → the folder's PIDL → its path. The calls go
/// through the interfaces' vtables directly: no reflection, no IDispatch, fine for Native AOT.
/// Call on an STA thread (the UI thread).
/// </summary>
[SupportedOSPlatform("windows")]
public static unsafe partial class WindowsExplorerFolder
{
    private static readonly Guid ClsidShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid IidShellWindows = new("85CB6900-4D95-11CF-960C-0080C7F4EE85");
    private static readonly Guid IidServiceProvider = new("6D5140C1-7436-11CE-8034-00AA006009FA");
    private static readonly Guid SidTopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid IidShellBrowser = new("000214E2-0000-0000-C000-000000000046");
    private static readonly Guid IidFolderView = new("CDE725B0-CCC9-4519-917E-325D72FAB4CE");
    private static readonly Guid IidPersistFolder2 = new("1AC3D9F0-175C-11D1-95BE-00609797EA4F");

    private const uint CLSCTX_ALL = 0x17;
    private const ushort VT_I4 = 3;
    private const uint GA_ROOT = 2;
    private const int SIGDN_FILESYSPATH = unchecked((int)0x80058000);

    /// <summary>
    /// The file system path of the folder shown in <paramref name="explorerWindow"/>; null for
    /// virtual folders (This PC, Recycle Bin, libraries…) or if Explorer does not answer.
    /// </summary>
    public static string? GetFolder(nint explorerWindow)
    {
        nint windows = 0;
        try
        {
            var clsid = ClsidShellWindows;
            var iid = IidShellWindows;
            if (CoCreateInstance(&clsid, 0, CLSCTX_ALL, &iid, &windows) < 0 || windows == 0)
            {
                return null;
            }

            int count;
            if (Call(windows, 7, (nint)(&count)) < 0) // IShellWindows::get_Count
            {
                return null;
            }

            // Windows 11: one entry per tab. The tab in front is the first ShellTabWindowClass child.
            var activeTab = FindWindowEx(explorerWindow, 0, "ShellTabWindowClass", null);
            string? fallback = null;
            var candidates = 0;
            for (var index = 0; index < count; index++)
            {
                var (tab, path) = ReadWindow(windows, index);
                if (tab == 0 || GetAncestor(tab, GA_ROOT) != explorerWindow)
                {
                    continue;
                }

                if (tab == activeTab || tab == explorerWindow)
                {
                    return path;
                }

                candidates++;
                fallback = path;
            }

            return candidates == 1 ? fallback : null;
        }
        catch (Exception ex) when (ex is COMException or SEHException)
        {
            return null;
        }
        finally
        {
            Release(windows);
        }
    }

    /// <summary>The window (tab) of one ShellWindows entry and the path of its folder.</summary>
    private static (nint Tab, string? Path) ReadWindow(nint windows, int index)
    {
        nint dispatch = 0, provider = 0, browser = 0, view = 0, folderView = 0, persist = 0;
        nint pidl = 0;
        try
        {
            var variant = stackalloc byte[24]; // VARIANT: vt at 0, value at 8 (24 bytes on 64-bit)
            new Span<byte>(variant, 24).Clear();
            *(ushort*)variant = VT_I4;
            *(int*)(variant + 8) = index;
            // IShellWindows::Item(VARIANT, IDispatch**): a VARIANT is passed by reference on x64
            if (Call(windows, 8, (nint)variant, (nint)(&dispatch)) < 0 || dispatch == 0)
            {
                return (0, null);
            }

            if (QueryInterface(dispatch, IidServiceProvider, out provider) < 0)
            {
                return (0, null);
            }

            var service = SidTopLevelBrowser;
            var iid = IidShellBrowser;
            if (Call(provider, 3, (nint)(&service), (nint)(&iid), (nint)(&browser)) < 0 || browser == 0) // QueryService
            {
                return (0, null);
            }

            nint tab;
            if (Call(browser, 3, (nint)(&tab)) < 0) // IOleWindow::GetWindow
            {
                return (0, null);
            }

            if (Call(browser, 15, (nint)(&view)) < 0 || view == 0 // IShellBrowser::QueryActiveShellView
                || QueryInterface(view, IidFolderView, out folderView) < 0)
            {
                return (tab, null);
            }

            iid = IidPersistFolder2;
            if (Call(folderView, 5, (nint)(&iid), (nint)(&persist)) < 0 || persist == 0 // IFolderView::GetFolder
                || Call(persist, 5, (nint)(&pidl)) < 0 || pidl == 0) // IPersistFolder2::GetCurFolder
            {
                return (tab, null);
            }

            char* name = null;
            if (SHGetNameFromIDList(pidl, SIGDN_FILESYSPATH, &name) < 0 || name == null)
            {
                return (tab, null); // a virtual folder: no path
            }

            var path = new string(name);
            Marshal.FreeCoTaskMem((nint)name);
            return (tab, path);
        }
        finally
        {
            if (pidl != 0)
            {
                Marshal.FreeCoTaskMem(pidl);
            }

            Release(persist);
            Release(folderView);
            Release(view);
            Release(browser);
            Release(provider);
            Release(dispatch);
        }
    }

    private static int QueryInterface(nint unknown, Guid iid, out nint result)
    {
        nint pointer = 0;
        var hr = Call(unknown, 0, (nint)(&iid), (nint)(&pointer));
        result = pointer;
        return hr;
    }

    private static void Release(nint unknown)
    {
        if (unknown != 0)
        {
            ((delegate* unmanaged[Stdcall]<nint, uint>)(*(*(void***)unknown + 2)))(unknown);
        }
    }

    // Every argument of these methods is a pointer: passed as nint
    private static int Call(nint instance, int slot, nint argument) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, int>)(*(*(void***)instance + slot)))(instance, argument);

    private static int Call(nint instance, int slot, nint first, nint second) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, nint, int>)(*(*(void***)instance + slot)))(instance, first, second);

    private static int Call(nint instance, int slot, nint first, nint second, nint third) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, nint, nint, int>)(*(*(void***)instance + slot)))(instance, first, second, third);

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(Guid* clsid, nint outer, uint context, Guid* iid, nint* result);

    [LibraryImport("shell32.dll")]
    private static partial int SHGetNameFromIDList(nint pidl, int kind, char** name);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowEx(nint parent, nint after, string? className, string? windowName);

    [LibraryImport("user32.dll")]
    private static partial nint GetAncestor(nint window, uint flags);
}
