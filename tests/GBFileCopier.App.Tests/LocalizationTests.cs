using Avalonia.Headless.XUnit;
using GBFileCopier.App.Resources;
using GBFileCopier.App.ViewModels;
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
        ResourceAssertions.AssertComplete(typeof(Strings), Strings.ResourceManager, "it", nameof(Strings.Checksum));

    [AvaloniaFact]
    public async Task ViewModel_SpeaksItalian()
    {
        using var italian = EnglishCulture.Use("it");
        var ui = new FakeUiServices { Clipboard = new ClipboardFiles([_temp.CreateFile("a.txt"), _temp.Combine("mancante.txt")], IsCut: true) };
        var viewModel = new MainViewModel(ui);

        viewModel.Paste(move: false);
        Assert.Equal("Elementi incollati: 2. Scegli la destinazione, poi Sposta", viewModel.StatusText);

        viewModel.Destination = _temp.Combine("dst");
        await viewModel.MoveCommand.ExecuteAsync(null);

        Assert.Equal("Completato con errori", viewModel.StatusText);
        Assert.StartsWith("Spostati: 1", viewModel.Summary, StringComparison.Ordinal);
        var failed = Assert.Single(viewModel.Results);
        Assert.Equal("Non riuscito", failed.Status);
        Assert.Equal("L'origine non esiste", failed.Message); // engine message, from Core
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
