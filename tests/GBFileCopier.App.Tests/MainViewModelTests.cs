using Avalonia.Headless.XUnit;
using GBFileCopier.App.Services;
using GBFileCopier.App.ViewModels;
using GBFileCopier.Core.Copying;
using GBFileCopier.Platform.Clipboard;
using GBFileCopier.Tests;

namespace GBFileCopier.App.Tests;

public sealed class MainViewModelTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeUiServices _ui = new();
    private readonly FakeSettingsStore _settings = new();

    public void Dispose() => _temp.Dispose();

    private MainViewModel CreateViewModel() => new(_ui, _settings);

    [AvaloniaFact]
    public void Paste_AddsTheClipboardFiles()
    {
        var file = _temp.CreateFile("a.txt");
        _ui.Clipboard = new ClipboardFiles([file, _temp.Path], IsCut: false);
        var viewModel = CreateViewModel();

        viewModel.Paste(move: false);

        Assert.Equal(2, viewModel.Sources.Count);
        Assert.False(viewModel.PreferMove);
    }

    [AvaloniaTheory]
    [InlineData(false, true, true)] // cut in Explorer
    [InlineData(true, false, true)] // ⌥⌘V
    [InlineData(false, false, false)]
    public void Paste_PrefersMoveForCutOrOptionCommandV(bool move, bool isCut, bool expected)
    {
        _ui.Clipboard = new ClipboardFiles([_temp.CreateFile("a.txt")], isCut);
        var viewModel = CreateViewModel();

        viewModel.Paste(move);

        Assert.Equal(expected, viewModel.PreferMove);
    }

    [AvaloniaFact]
    public void Paste_WithEmptyClipboard_ExplainsWhatToDo()
    {
        var viewModel = CreateViewModel();

        viewModel.Paste(move: false);

        Assert.Empty(viewModel.Sources);
        Assert.Contains("no files on the clipboard", viewModel.StatusText, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void AddSources_IgnoresDuplicates()
    {
        var file = _temp.CreateFile("a.txt");
        var viewModel = CreateViewModel();

        viewModel.AddSources([file, file, file + Path.DirectorySeparatorChar]);

        Assert.Single(viewModel.Sources);
    }

    [AvaloniaFact]
    public void Copy_NeedsSourcesAndDestination()
    {
        var viewModel = CreateViewModel();
        Assert.False(viewModel.CopyCommand.CanExecute(null));

        viewModel.AddSources([_temp.CreateFile("a.txt")]);
        Assert.False(viewModel.CopyCommand.CanExecute(null));

        viewModel.Destination = _temp.Combine("dst");
        Assert.True(viewModel.CopyCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Copy_RunsTheJobAndShowsTheSummary()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSources([_temp.CreateFile("src/a.bin", 10_000), _temp.CreateFile("src/b.bin", 20_000)]);
        viewModel.Destination = _temp.Combine("dst");
        viewModel.Verify = true;

        await viewModel.CopyCommand.ExecuteAsync(null);

        Assert.True(File.Exists(_temp.Combine("dst", "b.bin")));
        Assert.Equal("Done", viewModel.StatusText);
        Assert.Equal(100, viewModel.ProgressPercent);
        Assert.StartsWith("Copied: 2", viewModel.Summary, StringComparison.Ordinal);
        Assert.Contains("verified: 2", viewModel.Summary, StringComparison.Ordinal);
        Assert.True(viewModel.ShowAllResults); // no problems: all files are listed
        Assert.All(viewModel.Results, r => Assert.Equal("Verified", r.Status));
        Assert.False(viewModel.IsRunning);
    }

    [AvaloniaFact]
    public async Task Copy_WithProblems_ListsOnlyThem()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSources([_temp.CreateFile("src/ok.txt"), _temp.Combine("src", "missing.txt")]);
        viewModel.Destination = _temp.Combine("dst");

        await viewModel.CopyCommand.ExecuteAsync(null);

        Assert.Equal("Finished with errors", viewModel.StatusText);
        Assert.Equal(1, viewModel.ProblemCount);
        Assert.Equal("Failed", Assert.Single(viewModel.Results).Status);

        viewModel.ShowAllResults = true;
        Assert.Equal(2, viewModel.Results.Count);
    }

    [AvaloniaFact]
    public async Task MoveAfterCut_RemovesSourcesAndEmptiesTheClipboard()
    {
        var file = _temp.CreateFile("src/a.txt");
        _ui.Clipboard = new ClipboardFiles([file], IsCut: true);
        var viewModel = CreateViewModel();
        viewModel.Paste(move: false);
        viewModel.Destination = _temp.Combine("dst");

        await viewModel.MoveCommand.ExecuteAsync(null);

        Assert.False(File.Exists(file));
        Assert.Empty(viewModel.Sources);
        Assert.True(_ui.ClipboardCleared);
        Assert.False(viewModel.PreferMove);
    }

    [AvaloniaFact]
    public async Task Conflicts_ApplyToAll_AsksOnlyOnce()
    {
        _temp.CreateFile("dst/a.txt", text: "old");
        _temp.CreateFile("dst/b.txt", text: "old");
        _ui.OnConflict = _ => new ConflictAnswer(ConflictAction.Overwrite, ApplyToAll: true);
        var viewModel = CreateViewModel();
        viewModel.AddSources([_temp.CreateFile("src/a.txt", text: "new"), _temp.CreateFile("src/b.txt", text: "new")]);
        viewModel.Destination = _temp.Combine("dst");

        await viewModel.CopyCommand.ExecuteAsync(null);

        Assert.Equal(1, _ui.ConflictsAsked);
        Assert.Equal("new", File.ReadAllText(_temp.Combine("dst", "b.txt")));
    }

    [AvaloniaFact]
    public void ConflictViewModel_ReturnsTheChosenAnswer()
    {
        var now = DateTime.UtcNow;
        var conflict = new FileConflict("/src/a.txt", "/dst/a.txt", 10, now, 5, now.AddDays(-1));
        var viewModel = new ConflictViewModel(conflict) { ApplyToAll = true };
        ConflictAnswer? answer = null;
        viewModel.Answered += a => answer = a;

        viewModel.KeepBothCommand.Execute(null);

        Assert.Equal(new ConflictAnswer(ConflictAction.KeepBoth, ApplyToAll: true), answer);
        Assert.Equal("The new file is more recent", viewModel.Comparison);
    }
}
