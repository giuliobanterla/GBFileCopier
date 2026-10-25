using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using GBFileCopier.App.Services.Updates;
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

    /// <summary>Three jobs: one finished, one stopped on a conflict, one waiting for the same drive.</summary>
    [AvaloniaFact]
    public async Task MainWindow_WithQueuedJobs()
    {
        SetTheme("Light");
        var ui = new FakeUiServices { ConflictGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var viewModel = new MainViewModel(ui, new FakeSettingsStore());
        viewModel.AddSources([_temp.CreateFile("Photos/IMG_0001.jpg", 300_000)]);
        viewModel.Destination = _temp.Combine("Backup");
        await viewModel.CopyCommand.ExecuteAsync(null);

        _temp.CreateFile("Archive/Report 2026.pdf", text: "old");
        viewModel.AddSources([_temp.CreateFile("Documents/Report 2026.pdf", 120_000)]);
        viewModel.Destination = _temp.Combine("Archive");
        var blocked = viewModel.MoveCommand.ExecuteAsync(null);
        for (var i = 0; i < 500 && ui.ConflictsAsked == 0; i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        viewModel.AddSources([_temp.CreateFile("Videos/Holiday.mp4", 500_000)]);
        viewModel.Destination = _temp.Combine("Videos backup");
        var waiting = viewModel.CopyCommand.ExecuteAsync(null);
        viewModel.SelectedJob = viewModel.Jobs[0];

        Save(new MainWindow { DataContext = viewModel }, "main-queue.png");

        ui.ConflictGate.SetResult();
        await Task.WhenAll(blocked, waiting);
    }

    [AvaloniaTheory]
    [InlineData("Light", "en")]
    [InlineData("Dark", "it")]
    public void MainWindow_WithUpdateBar(string theme, string language)
    {
        using var culture = EnglishCulture.Use(language);
        SetTheme(theme);
        var viewModel = new MainViewModel(new FakeUiServices(), new FakeSettingsStore())
        {
            Updates = new UpdateViewModel(new BarUpdateService(), new FakeSettingsStore(), () => true, () => { }, _ => { }),
        };
        viewModel.Updates.CheckNowAsync().GetAwaiter().GetResult();

        Save(new MainWindow { DataContext = viewModel }, $"main-update-{language}-{theme.ToLowerInvariant()}.png");
    }

    [AvaloniaFact]
    public async Task MainWindow_AskingAboutUpdates()
    {
        SetTheme("Light");
        var viewModel = new MainViewModel(new FakeUiServices(), new FakeSettingsStore())
        {
            Updates = new UpdateViewModel(new BarUpdateService(), new FakeSettingsStore(), () => true, () => { }, _ => { }),
        };
        await viewModel.Updates.StartAsync();

        Save(new MainWindow { DataContext = viewModel }, "main-update-ask.png");
    }

    private sealed class BarUpdateService : IUpdateService
    {
        public UpdateVersion CurrentVersion => UpdateVersion.Parse("0.3.1")!;

        public bool CanInstall => true;

        public Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ReleaseInfo?>(new ReleaseInfo(UpdateVersion.Parse("0.4.0")!, new Uri("https://github.com/x"), []));

        public Task<PreparedUpdate> DownloadAsync(ReleaseInfo release, IProgress<double> progress, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> InstallAsync(PreparedUpdate update) => throw new NotSupportedException();
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

    [AvaloniaFact]
    public void ConflictWindow_Dark()
    {
        SetTheme("Dark");
        RenderConflictWindow("conflict-dark.png", theme: null);
    }

    private static void RenderConflictWindow(string fileName, string? theme = "Light")
    {
        if (theme is not null)
        {
            SetTheme(theme);
        }

        var now = DateTime.UtcNow;
        var conflict = new FileConflict(
            "/Users/me/Photos/IMG_0001.jpg", "/Volumes/Backup/Photos/IMG_0001.jpg", 3_400_000, now, 3_100_000, now.AddDays(-3));
        Save(new ConflictWindow { DataContext = new ConflictViewModel(conflict) }, fileName);
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void SettingsAndAboutWindows(string theme)
    {
        SetTheme(theme);
        var store = new FakeSettingsStore { Saved = new() { Language = "it" } };
        var settings = new SettingsViewModel(store, "/Users/me/Library/Application Support/GBFileCopier/settings.json");
        settings.SelectedLanguage = settings.Languages[0]; // changed: shows the restart note
        Save(new SettingsWindow { DataContext = settings }, $"settings-{theme.ToLowerInvariant()}.png");
        Save(new AboutWindow(), $"about-{theme.ToLowerInvariant()}.png");
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
