using GBFileCopier.Platform;

namespace GBFileCopier.Platform.Tests;

public sealed class ExplorerPasteTests
{
    private static readonly PasteKeyContext InFileList =
        new("CabinetWClass", "DirectUIHWND", "SHELLDLL_DefView", Ctrl: true, Shift: false, Alt: false, Win: false, ClipboardHasFiles: true);

    [Fact]
    public void CtrlV_InExplorersFileList_WithFiles_IsTaken() => Assert.True(ExplorerPasteRules.ShouldIntercept(InFileList));

    public static TheoryData<string, PasteKeyContext> Others => new()
    {
        { "text on the clipboard", InFileList with { ClipboardHasFiles = false } },
        { "renaming a file", InFileList with { FocusClass = "Edit", FocusParentClass = "DirectUIHWND" } },
        { "address bar or search box", InFileList with { FocusClass = "Microsoft.UI.Content.DesktopChildSiteBridge", FocusParentClass = "CabinetWClass" } },
        { "navigation pane", InFileList with { FocusClass = "SysTreeView32", FocusParentClass = "NamespaceTreeControl" } },
        { "another program", InFileList with { ForegroundClass = "Notepad" } },
        { "the desktop", InFileList with { ForegroundClass = "Progman" } },
        { "Ctrl+Shift+V", InFileList with { Shift = true } },
        { "Ctrl+Alt+V", InFileList with { Alt = true } },
        { "Win+Ctrl+V", InFileList with { Win = true } },
        { "V alone", InFileList with { Ctrl = false } },
    };

    [Theory]
    [MemberData(nameof(Others))]
    public void Otherwise_ExplorerKeepsCtrlV(string _, PasteKeyContext context) => Assert.False(ExplorerPasteRules.ShouldIntercept(context));

    [Fact]
    public void SameFolder_IsLeftToExplorer()
    {
        var folder = Path.Combine(Path.GetTempPath(), "photos");

        Assert.True(ExplorerPasteRules.IsSameFolder(folder, [Path.Combine(folder, "a.jpg")]));
        Assert.True(ExplorerPasteRules.IsSameFolder(folder + Path.DirectorySeparatorChar, [Path.Combine(folder, "sub")]));
        Assert.False(ExplorerPasteRules.IsSameFolder(Path.Combine(folder, "backup"), [Path.Combine(folder, "a.jpg")]));
    }

    [Fact]
    public void Windows_ExplorerFolder_WithoutExplorerWindow_IsNull()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Runs the COM calls (ShellWindows, its count and entries) without a real Explorer window
        Assert.Null(WindowsExplorerFolder.GetFolder(0));
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")] // the analyzer does not follow the guard into the lambda
    public void Windows_Hook_InstallsAndUninstalls()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");

        using (WindowsPasteHook.Install(_ => { }))
        {
            Assert.Throws<InvalidOperationException>(() => WindowsPasteHook.Install(_ => { })); // one per process
        }

        WindowsPasteHook.Install(_ => { }).Dispose(); // installable again after Dispose
    }

    [Fact]
    public void Windows_StartAtSignIn_RoundTrips()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("CI") == "true", "Windows CI only: changes the user's Run key");
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        WindowsStartup.Set(true, @"C:\Program Files\GBFileCopier\GBFileCopier.exe", "--background");
        Assert.Equal("\"C:\\Program Files\\GBFileCopier\\GBFileCopier.exe\" --background", WindowsStartup.Command);
        WindowsStartup.Set(false, @"C:\x.exe", "--background");
        Assert.Null(WindowsStartup.Command);
    }
}
