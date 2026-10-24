using System.Text;
using GBFileCopier.Platform.Clipboard;
using GBFileCopier.Tests;

namespace GBFileCopier.Platform.Tests;

public sealed class FileClipboardTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string[] CreateItems()
    {
        _temp.CreateFile("folder/inside.txt", text: "x");
        return
        [
            _temp.CreateFile("one.txt", text: "1"),
            _temp.Combine("folder"),
            _temp.CreateFile("nome con spazi è ü.txt", text: "2"),
        ];
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)] // the Finder copies file reference URLs (file:///.file/id=...)
    public void MacPasteboard_RoundTripsFilePaths(bool asFileReferences)
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "macOS only");
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        // A private pasteboard: running the tests doesn't touch the user's clipboard
        var pasteboard = new MacFileClipboard($"GBFileCopier.Tests.{Guid.NewGuid():N}");
        try
        {
            var items = CreateItems();
            pasteboard.Write(items, asFileReferences);

            var read = pasteboard.Read();

            Assert.NotNull(read);
            Assert.False(read.IsCut);
            // Compare resolved paths: on macOS the temp folder is behind the /var -> /private/var link
            Assert.Equal(items.Select(Resolve), read.Paths.Select(Resolve));
        }
        finally
        {
            pasteboard.Release();
        }
    }

    [Fact]
    public void MacPasteboard_WithoutFiles_ReturnsNull()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "macOS only");
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var pasteboard = new MacFileClipboard($"GBFileCopier.Tests.{Guid.NewGuid():N}");
        try
        {
            pasteboard.Clear();
            Assert.Null(pasteboard.Read());
        }
        finally
        {
            pasteboard.Release();
        }
    }

    [Fact]
    public void WindowsDropFiles_HasHeaderAndDoubleNullTerminatedWideList()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var data = WindowsFileClipboard.CreateDropFiles([@"C:\a.txt", @"D:\b"]);

        Assert.Equal(20, BitConverter.ToInt32(data, 0)); // pFiles
        Assert.Equal(1, BitConverter.ToInt32(data, 16)); // fWide
        Assert.Equal("C:\\a.txt\0D:\\b\0\0", Encoding.Unicode.GetString(data, 20, data.Length - 20));
    }

    /// <summary>Uses the real clipboard, so it runs only in CI, never on a developer's machine.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WindowsClipboard_RoundTripsFilesAndCutFlag(bool cut)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("CI") == "true", "Windows CI only");

        var items = CreateItems();
        FileClipboard.Write(items, cut);

        var read = FileClipboard.Read();

        Assert.NotNull(read);
        Assert.Equal(cut, read.IsCut);
        Assert.Equal(items, read.Paths);
        FileClipboard.Clear();
        Assert.Null(FileClipboard.Read());
    }

    private static string Resolve(string path)
    {
        var full = Path.GetFullPath(path);
        var directory = new DirectoryInfo(Path.GetDirectoryName(full)!);
        var resolved = directory.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? directory.FullName;
        // Resolve each ancestor: /var is the link, not the temp folder itself
        while (directory.Parent is { } parent)
        {
            var target = parent.ResolveLinkTarget(returnFinalTarget: true);
            if (target is not null)
            {
                resolved = target.FullName + resolved[parent.FullName.Length..];
                break;
            }

            directory = parent;
        }

        return Path.Combine(resolved, Path.GetFileName(full));
    }
}
