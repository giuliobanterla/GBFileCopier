using Avalonia.Headless.XUnit;
using GBFileCopier.App.Settings;
using GBFileCopier.App.ViewModels;
using GBFileCopier.Core.Copying;
using GBFileCopier.Core.Hashing;
using GBFileCopier.Platform;
using GBFileCopier.Tests;

namespace GBFileCopier.App.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Store_RoundTripsTheSettings()
    {
        var store = new SettingsStore(_temp.Combine("sub", "settings.json"));
        var settings = new AppSettings
        {
            Language = "it",
            LastDestination = "/Volumes/Backup",
            Verify = true,
            HashAlgorithm = HashAlgorithmKind.Sha256,
            ConflictPolicy = ConflictPolicy.KeepBoth,
            WindowWidth = 900,
            WindowHeight = 700,
        };

        store.Save(settings);

        Assert.Equal(settings, store.Load());
        var json = File.ReadAllText(store.FilePath);
        Assert.Contains("\"KeepBoth\"", json, StringComparison.Ordinal); // enums as names: readable and stable
        Assert.False(File.Exists(store.FilePath + ".tmp"));
    }

    [Theory]
    [InlineData(null)] // missing
    [InlineData("{ not json")] // corrupted
    [InlineData("{\"HashAlgorithm\": \"Blake9\"}")] // unknown value
    public void Store_FallsBackToDefaults(string? content)
    {
        var path = _temp.Combine("settings.json");
        if (content is not null)
        {
            File.WriteAllText(path, content);
        }

        Assert.Equal(new AppSettings(), new SettingsStore(path).Load());
    }

    [AvaloniaFact]
    public void MainViewModel_RestoresTheLastOptions()
    {
        var store = new FakeSettingsStore
        {
            Saved = new AppSettings
            {
                LastDestination = "/Volumes/Backup",
                Verify = true,
                HashAlgorithm = HashAlgorithmKind.Sha256,
                ConflictPolicy = ConflictPolicy.Skip,
            },
        };

        var viewModel = new MainViewModel(new FakeUiServices(), store);

        Assert.Equal("/Volumes/Backup", viewModel.Destination);
        Assert.True(viewModel.Verify);
        Assert.Equal(HashAlgorithmKind.Sha256, viewModel.SelectedHash.Kind);
        Assert.Equal(ConflictPolicy.Skip, viewModel.SelectedConflict.Policy);
    }

    [AvaloniaFact]
    public async Task MainViewModel_SavesTheOptionsWhenCopying()
    {
        var store = new FakeSettingsStore { Saved = new AppSettings { Language = "it" } };
        var viewModel = new MainViewModel(new FakeUiServices(), store);
        viewModel.AddSources([_temp.CreateFile("a.txt")]);
        viewModel.Destination = _temp.Combine("dst");
        viewModel.Verify = true;
        viewModel.SelectedHash = viewModel.HashOptions.First(o => o.Kind == HashAlgorithmKind.Md5);

        await viewModel.CopyCommand.ExecuteAsync(null);

        Assert.Equal(_temp.Combine("dst"), store.Saved.LastDestination);
        Assert.True(store.Saved.Verify);
        Assert.Equal(HashAlgorithmKind.Md5, store.Saved.HashAlgorithm);
        Assert.Equal("it", store.Saved.Language); // untouched
    }

    [AvaloniaFact]
    public void MainViewModel_SavesTheWindowSizeOnClose()
    {
        var store = new FakeSettingsStore();
        var viewModel = new MainViewModel(new FakeUiServices(), store);

        viewModel.SaveSettings(950, 720);

        Assert.Equal((950d, 720d), (store.Saved.WindowWidth, store.Saved.WindowHeight));
    }

    [AvaloniaFact]
    public void SettingsViewModel_SavesTheLanguageAndAsksForARestart()
    {
        var store = new FakeSettingsStore { Saved = new AppSettings { LastDestination = "/x" } };
        var viewModel = new SettingsViewModel(store);
        Assert.Equal(string.Empty, viewModel.SelectedLanguage.Code);
        Assert.False(viewModel.NeedsRestart);

        viewModel.SelectedLanguage = viewModel.Languages.First(l => l.Code == "it");

        Assert.Equal("it", store.Saved.Language);
        Assert.Equal("/x", store.Saved.LastDestination);
        Assert.True(viewModel.NeedsRestart);
    }

    [Fact]
    public void UiLanguage_ConfiguredLanguageWinsOverTheSystem()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable(UiLanguage.OverrideVariable) is { Length: > 0 }, "Language forced by the environment");

        Assert.Equal("it", UiLanguage.GetPreferred("it").Name);
        Assert.Equal("en", UiLanguage.GetPreferred("en").Name);
    }
}
