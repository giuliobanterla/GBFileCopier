using Avalonia.Headless.XUnit;
using GBFileCopier.App.Resources;
using GBFileCopier.App.ViewModels;
using GBFileCopier.Core.Copying;
using GBFileCopier.Platform;
using GBFileCopier.Platform.Clipboard;
using GBFileCopier.Tests;

namespace GBFileCopier.App.Tests;

public sealed class LocalizationTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Italian_IsComplete() =>
        ResourceAssertions.AssertComplete(typeof(Strings), Strings.ResourceManager, "it", nameof(Strings.Checksum), nameof(Strings.MenuFile));

    [AvaloniaFact]
    public async Task ViewModel_SpeaksItalian()
    {
        using var italian = EnglishCulture.Use("it");
        var ui = new FakeUiServices { Clipboard = new ClipboardFiles([_temp.CreateFile("a.txt"), _temp.Combine("mancante.txt")], IsCut: true) };
        var viewModel = new MainViewModel(ui, new FakeSettingsStore());

        viewModel.Paste(move: false);
        Assert.Equal("Elementi incollati: 2. Scegli la destinazione, poi Sposta", viewModel.StatusText);

        viewModel.Destination = _temp.Combine("dst");
        await viewModel.MoveCommand.ExecuteAsync(null);

        var job = Assert.Single(viewModel.Jobs);
        Assert.StartsWith("Sposta in ", job.Title, StringComparison.Ordinal);
        Assert.Equal("Elementi: 2", job.ItemsText);
        Assert.Equal("Completato con errori", job.StatusText);
        Assert.StartsWith("Spostati: 1", job.Summary, StringComparison.Ordinal);
        var failed = Assert.Single(job.Files);
        Assert.Equal("L'origine non esiste", failed.Message); // engine message, from Core
        // The rows are updated from the UI thread's queue, which in the headless tests does not get the
        // language set by this test (the app sets it for the whole process): check the row text here
        Assert.Equal("Non riuscito", JobFileItem.Finished(new() { SourcePath = "a", DestinationPath = "b", Status = FileCopyStatus.Failed }, CopyMode.Move).Status);
        Assert.Equal("In coda", JobFileItem.Queued(new PlannedFile(CopyItemKind.File, "a", "b", 1)).Status);
        Assert.Equal("Chiedi", viewModel.SelectedConflict.Label);
    }

    [Fact]
    public void OverrideVariable_ChoosesTheLanguage()
    {
        var previous = Environment.GetEnvironmentVariable(UiLanguage.OverrideVariable);
        try
        {
            Environment.SetEnvironmentVariable(UiLanguage.OverrideVariable, "it");
            Assert.Equal("it", UiLanguage.GetPreferred().Name);
        }
        finally
        {
            Environment.SetEnvironmentVariable(UiLanguage.OverrideVariable, previous);
        }
    }
}
