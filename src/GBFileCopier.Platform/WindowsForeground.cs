using System.Runtime.InteropServices;

namespace GBFileCopier.Platform;

/// <summary>
/// Windows lets only the foreground process bring a window to the front. A process started by a
/// user action (a context menu click) has that right and can pass it on before handing over its
/// work to the app that is already running.
/// </summary>
public static partial class WindowsForeground
{
    private const int ASFW_ANY = -1;

    public static void AllowAnyProcess()
    {
        if (OperatingSystem.IsWindows())
        {
            _ = AllowSetForegroundWindow(ASFW_ANY);
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);
}
