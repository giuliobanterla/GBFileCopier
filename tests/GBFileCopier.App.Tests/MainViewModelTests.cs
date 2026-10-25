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

        var job = Assert.Single(viewModel.Jobs);
        Assert.Same(job, viewModel.SelectedJob);
        Assert.True(File.Exists(_temp.Combine("dst", "b.bin")));
        Assert.Equal("Done", job.StatusText);
        Assert.Equal(100, job.ProgressPercent);
        Assert.StartsWith("Copied: 2", job.Summary, StringComparison.Ordinal);
        Assert.Contains("verified: 2", job.Summary, StringComparison.Ordinal);
        Assert.True(job.ShowAllResults); // no problems: all files are listed
        Assert.All(job.Results, r => Assert.Equal("Verified", r.Status));
        Assert.True(job.IsFinished);
        Assert.False(viewModel.HasActiveJobs);
        Assert.Empty(viewModel.Sources); // ready for the next job
    }

    [AvaloniaFact]
    public async Task Copy_WithProblems_ListsOnlyThem()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSources([_temp.CreateFile("src/ok.txt"), _temp.Combine("src", "missing.txt")]);
        viewModel.Destination = _temp.Combine("dst");

        await viewModel.CopyCommand.ExecuteAsync(null);

        var job = Assert.Single(viewModel.Jobs);
        Assert.Equal("Finished with errors", job.StatusText);
        Assert.Equal(1, job.ProblemCount);
        Assert.Equal("Failed", Assert.Single(job.Results).Status);

        job.ShowAllResults = true;
        Assert.Equal(2, job.Results.Count);
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
    public async Task MoveAfterCut_KeepsTheClipboardIfSomethingElseWasCopiedMeanwhile()
    {
        var file = _temp.CreateFile("src/a.txt");
        _ui.Clipboard = new ClipboardFiles([file], IsCut: true);
        var viewModel = CreateViewModel();
        viewModel.Paste(move: false);
        viewModel.Destination = _temp.Combine("dst");
        _ui.Clipboard = new ClipboardFiles([_temp.CreateFile("other.txt")], IsCut: false);

        await viewModel.MoveCommand.ExecuteAsync(null);

        Assert.False(File.Exists(file));
        Assert.False(_ui.ClipboardCleared);
    }

    /// <summary>Starts a copy to <paramref name="destination"/> that stops on a conflict until <see cref="FakeUiServices.ConflictGate"/> opens.</summary>
    private Task StartBlockedCopy(MainViewModel viewModel, string destination)
    {
        _temp.CreateFile(Path.Combine(destination, "a.txt"), text: "old");
        _ui.ConflictGate ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.AddSources([_temp.CreateFile("src1/a.txt", text: "new")]);
        viewModel.Destination = _temp.Combine(destination);
        return viewModel.CopyCommand.ExecuteAsync(null);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }

    [AvaloniaFact]
    public async Task SecondJobToTheSameDrive_WaitsForTheFirst()
    {
        var viewModel = CreateViewModel();
        var first = StartBlockedCopy(viewModel, "dst1");
        await WaitUntil(() => _ui.ConflictsAsked == 1);

        // While the first job runs, the window takes the next one
        Assert.True(viewModel.CopyCommand.CanExecute(null) is false); // nothing to copy yet
        viewModel.AddSources([_temp.CreateFile("src2/b.txt")]);
        viewModel.Destination = _temp.Combine("dst2");
        var second = viewModel.CopyCommand.ExecuteAsync(null);

        var waiting = viewModel.Jobs[1];
        Assert.True(waiting.IsWaiting);
        Assert.StartsWith("Waiting", waiting.StatusText, StringComparison.Ordinal);
        Assert.False(File.Exists(_temp.Combine("dst2", "b.txt")));
        Assert.True(viewModel.HasActiveJobs);

        _ui.ConflictGate!.SetResult();
        await Task.WhenAll(first, second);

        Assert.All(viewModel.Jobs, j => Assert.Equal("Done", j.StatusText));
        Assert.True(File.Exists(_temp.Combine("dst2", "b.txt")));
        Assert.False(viewModel.HasActiveJobs);
    }

    [AvaloniaFact]
    public async Task CancelWhileWaiting_RemovesTheJobFromTheQueue()
    {
        var viewModel = CreateViewModel();
        var first = StartBlockedCopy(viewModel, "dst1");
        await WaitUntil(() => _ui.ConflictsAsked == 1);
        viewModel.AddSources([_temp.CreateFile("src2/b.txt")]);
        viewModel.Destination = _temp.Combine("dst2");
        var second = viewModel.CopyCommand.ExecuteAsync(null);

        viewModel.Jobs[1].CancelCommand.Execute(null);
        await second;

        Assert.Equal("Cancelled", viewModel.Jobs[1].StatusText);
        Assert.True(viewModel.Jobs[1].IsFinished);
        Assert.True(viewModel.Jobs[0].IsActive);

        _ui.ConflictGate!.SetResult();
        await first;
        Assert.False(File.Exists(_temp.Combine("dst2", "b.txt")));
    }

    [AvaloniaFact]
    public async Task ExternalRequest_WhileAJobRuns_FillsInTheNextJob()
    {
        var viewModel = CreateViewModel();
        var first = StartBlockedCopy(viewModel, "dst1");
        await WaitUntil(() => _ui.ConflictsAsked == 1);

        viewModel.Apply(new ExternalRequest([_temp.CreateFile("other/c.txt")], null, PasteFromClipboard: false, Move: true));

        Assert.Single(viewModel.Sources);
        Assert.True(viewModel.PreferMove);
        _ui.ConflictGate!.SetResult();
        await first;
    }

    [AvaloniaFact]
    public async Task ClearFinished_KeepsTheActiveJobs()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSources([_temp.CreateFile("src0/z.txt")]);
        viewModel.Destination = _temp.Combine("dst0");
        await viewModel.CopyCommand.ExecuteAsync(null);
        var running = StartBlockedCopy(viewModel, "dst1");
        await WaitUntil(() => _ui.ConflictsAsked == 1);

        Assert.True(viewModel.ClearFinishedCommand.CanExecute(null));
        viewModel.ClearFinishedCommand.Execute(null);

        Assert.True(Assert.Single(viewModel.Jobs).IsActive);
        Assert.False(viewModel.ClearFinishedCommand.CanExecute(null));
        _ui.ConflictGate!.SetResult();
        await running;
    }

    [AvaloniaFact]
    public async Task CancelAll_StopsEveryJob()
    {
        var viewModel = CreateViewModel();
        var running = StartBlockedCopy(viewModel, "dst1");
        await WaitUntil(() => _ui.ConflictsAsked == 1);
        viewModel.AddSources([_temp.CreateFile("src2/b.txt")]);
        viewModel.Destination = _temp.Combine("dst2");
        _ = viewModel.CopyCommand.ExecuteAsync(null);

        var cancelled = viewModel.CancelAllAsync();
        _ui.ConflictGate!.SetResult(); // the dialog closes; the engine then sees the cancellation
        await cancelled;

        Assert.False(viewModel.HasActiveJobs);
        Assert.Equal("Cancelled", viewModel.Jobs[1].StatusText); // never started
        viewModel.AddSources([_temp.CreateFile("src3/c.txt")]);
        Assert.False(viewModel.CopyCommand.CanExecute(null)); // the window is closing
        Assert.False(File.Exists(_temp.Combine("dst2", "b.txt")));
        await running;
    }

    [Theory]
    [InlineData("/", "the startup disk")]
    [InlineData("/Volumes/Backup", "Backup")]
    [InlineData("/Volumes/My Passport", "My Passport")]
    public void DriveName_IsFriendlyOnMacOS(string driveKey, string expected)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "macOS mount points");
        Assert.Equal(expected, JobViewModel.DriveName(driveKey));
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
