using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using GBFileCopier.App.ViewModels;
using GBFileCopier.App.Views;
using GBFileCopier.Core.Copying;
using GBFileCopier.Tests;

namespace GBFileCopier.App.Tests;

/// <summary>
/// Renders the windows to PNG files (in "screenshots" next to the test binaries), to look at the
/// layout without running the app. They also check that the windows load and render without errors.
/// </summary>
public sealed class ScreenshotTests : IDisposable
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "screenshots");

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public Task MainWindow_AfterACopyWithProblems(string theme) =>
        RenderMainWindowAsync(theme, $"main-{theme.ToLowerInvariant()}.png");

    private async Task RenderMainWindowAsync(string theme, string fileName)
    {
        SetTheme(theme);
        var viewModel = new MainViewModel(new FakeUiServices(), new FakeSettingsStore());
        viewModel.AddSources(
        [
            _temp.CreateFile("Photos/IMG_0001.jpg", 300_000),
            _temp.CreateFile("Documents/Report 2026.pdf", 120_000),
            _temp.Combine("Photos"),
            _temp.Combine("Missing file.txt"),
        ]);
        viewModel.Destination = _temp.Combine("Backup");
        viewModel.Verify = true;
        await viewModel.CopyCommand.ExecuteAsync(null);

        Save(new MainWindow { DataContext = viewModel }, fileName);
    }

    [AvaloniaFact]
    public async Task MainWindow_InItalian()
    {
        using var italian = EnglishCulture.Use("it");
        await RenderMainWindowAsync("Light", "main-italian.png");
    }

    [AvaloniaFact]
    public void ConflictWindow_InItalian()
    {
        using var italian = EnglishCulture.Use("it");
        RenderConflictWindow("conflict-italian.png");
    }

    [AvaloniaFact]
    public void MainWindow_Empty()
    {
        SetTheme("Light");
        Save(new MainWindow { DataContext = new MainViewModel(new FakeUiServices(), new FakeSettingsStore()) }, "main-empty.png");
    }

    [AvaloniaFact]
    public void ConflictWindow() => RenderConflictWindow("conflict.png");

    private static void RenderConflictWindow(string fileName)
    {
        SetTheme("Light");
        var now = DateTime.UtcNow;
        var conflict = new FileConflict(
            "/Users/me/Photos/IMG_0001.jpg", "/Volumes/Backup/Photos/IMG_0001.jpg", 3_400_000, now, 3_100_000, now.AddDays(-3));
        Save(new ConflictWindow { DataContext = new ConflictViewModel(conflict) }, fileName);
    }

    [AvaloniaFact]
    public void SettingsAndAboutWindows()
    {
        SetTheme("Light");
        var store = new FakeSettingsStore { Saved = new() { Language = "it" } };
        var settings = new SettingsViewModel(store, "/Users/me/Library/Application Support/GBFileCopier/settings.json");
        settings.SelectedLanguage = settings.Languages[0]; // changed: shows the restart note
        Save(new SettingsWindow { DataContext = settings }, "settings.png");
        Save(new AboutWindow(), "about.png");
    }

    private static void SetTheme(string theme) =>
        Application.Current!.RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;

    private static void Save(Window window, string name)
    {
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Directory.CreateDirectory(Folder);
        frame.Save(Path.Combine(Folder, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        window.Close();
    }
}
