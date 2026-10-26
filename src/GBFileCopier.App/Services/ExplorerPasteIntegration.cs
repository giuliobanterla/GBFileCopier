using System.Runtime.Versioning;
using System.Security;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using GBFileCopier.App.Resources;
using GBFileCopier.App.ViewModels;
using GBFileCopier.Core;
using GBFileCopier.Platform;
using GBFileCopier.Platform.Clipboard;

namespace GBFileCopier.App.Services;

/// <summary>
/// "Use GBFileCopier for Ctrl+V in Explorer" (Windows): while on, a keyboard hook takes Ctrl+V in
/// Explorer's file lists, the app stays in the notification area when its window is closed, and
/// Windows starts it at sign-in (in the background). Ctrl+V the app cannot handle (a virtual
/// folder, the same folder, no files) is given back to Explorer.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ExplorerPasteIntegration(Application app, Window window, MainViewModel viewModel, Action showWindow, Action quit) : IDisposable
{
    private WindowsPasteHook? _hook;
    private TrayIcon? _trayIcon;

    public bool IsEnabled => _hook is not null;

    /// <summary>Stops the hook and the icon; the start at sign-in stays as chosen.</summary>
    public void Dispose()
    {
        _hook?.Dispose();
        _hook = null;
        HideTrayIcon();
    }

    /// <summary>Switches the whole feature on or off: hook, notification area icon, start at sign-in.</summary>
    /// <returns>Whether it is on now: false if Windows refused the hook.</returns>
    public bool Apply(bool enabled)
    {
        if (enabled == IsEnabled)
        {
            if (!enabled && WindowsStartup.Command is not null)
            {
                SetStartAtSignIn(false); // left over, for example from a copy that crashed
            }

            return IsEnabled;
        }

        if (enabled)
        {
            try
            {
                // The hook must answer at once: the real work is posted to the UI thread
                _hook = WindowsPasteHook.Install(explorer => Dispatcher.UIThread.Post(() => Paste(explorer)));
            }
            catch (InvalidOperationException ex)
            {
                DiagnosticLog.Write($"Explorer paste: {ex.Message}");
                return false;
            }

            ShowTrayIcon();
        }
        else
        {
            _hook?.Dispose();
            _hook = null;
            HideTrayIcon();
        }

        SetStartAtSignIn(enabled);
        DiagnosticLog.Write($"Explorer paste: {(enabled ? "on" : "off")}");
        return IsEnabled;
    }

    private void Paste(nint explorerWindow)
    {
        string? folder = null;
        ClipboardFiles? clipboard = null;
        try
        {
            folder = WindowsExplorerFolder.GetFolder(explorerWindow);
            clipboard = FileClipboard.Read();
        }
        catch (IOException ex)
        {
            DiagnosticLog.Write($"Explorer paste: {ex.Message}");
        }

        // Not a real folder (This PC, a .zip opened as a folder…) or the files' own folder: Explorer's paste
        if (folder is null || !Directory.Exists(folder) || clipboard is null || ExplorerPasteRules.IsSameFolder(folder, clipboard.Paths))
        {
            DiagnosticLog.Write($"Explorer paste: given back to Explorer (folder={folder ?? "none"}, files={clipboard?.Paths.Count ?? 0})");
            WindowsPasteHook.SendPasteToExplorer(explorerWindow);
            return;
        }

        DiagnosticLog.Write($"Explorer paste: {clipboard.Paths.Count} item(s) to {folder}, {(clipboard.IsCut ? "move" : "copy")}");
        _ = viewModel.PasteInto(folder, clipboard);
        showWindow();
    }

    private void ShowTrayIcon()
    {
        var open = new NativeMenuItem(Strings.TrayOpen);
        open.Click += (_, _) => showWindow();
        var exit = new NativeMenuItem(Strings.MenuExit);
        exit.Click += (_, _) => quit();
        _trayIcon = new TrayIcon
        {
            Icon = window.Icon,
            ToolTipText = AppInfo.Name,
            Menu = [open, new NativeMenuItemSeparator(), exit],
        };
        _trayIcon.Clicked += (_, _) => showWindow();
        TrayIcon.SetIcons(app, [_trayIcon]);
    }

    private void HideTrayIcon()
    {
        if (_trayIcon is not null)
        {
            TrayIcon.SetIcons(app, []);
            _trayIcon.Dispose();
            _trayIcon = null;
        }
    }

    private static void SetStartAtSignIn(bool enabled)
    {
        if (Environment.ProcessPath is not { } executable)
        {
            return;
        }

        try
        {
            WindowsStartup.Set(enabled, executable, ExternalRequest.BackgroundArgument);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
        {
            DiagnosticLog.Write($"Explorer paste: cannot change the start at sign-in: {ex.Message}");
        }
    }
}
