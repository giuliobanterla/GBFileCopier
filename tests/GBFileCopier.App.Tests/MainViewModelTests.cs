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

    [Theory]
    [InlineData(50_000_000, 100_000_000, 1_000_000, "50%  ·  976.6 KB/s  ·  0:50 left")]
    [InlineData(0, 100_000_000, 0, "0%  ·  0 B/s")] // no time left before there is a speed
    [InlineData(1, 108_000_000_001, 1_000_000, "0%  ·  976.6 KB/s  ·  30:00:00 left")] // more than a day: hours go on
    public void Transfer_ShowsPercentSpeedAndTimeLeft(long done, long total, double speed, string expected)
    {
        var progress = new CopyProgress(CopyPhase.Copying, "a.bin", done, total, 0, 1, TimeSpan.FromSeconds(10));
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture; // "976.6", whatever the machine's region
        try
        {
            Assert.Equal(expected, JobViewModel.FormatTransfer(progress, speed));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
    }

    [AvaloniaFact]
    public void Title_ShowsTheVersion()
    {
        var viewModel = CreateViewModel();

        Assert.Equal($"GBFileCopier {viewModel.Version}", viewModel.Title);
        Assert.DoesNotContain("+", viewModel.Version, StringComparison.Ordinal); // no commit hash
        Assert.Matches(@"^\d+\.\d+\.\d+", viewModel.Version);
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
    public async Task Copy_WithChecksumFile_SavesItAndNamesItInTheSummary()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSources([_temp.CreateFile("src/a.bin")]);
        viewModel.Destination = _temp.Combine("dst");
        viewModel.WriteChecksumFile = true;

        await viewModel.CopyCommand.ExecuteAsync(null);

        Assert.True(File.Exists(_temp.Combine("dst", "a.bin.xxh3")));
        Assert.Contains("checksums in a.bin.xxh3", Assert.Single(viewModel.Jobs).Summary, StringComparison.Ordinal);
        Assert.True(_settings.Load().WriteChecksumFile); // remembered for the next time
    }

    [AvaloniaFact]
    public async Task VerifyChecksumFile_ChecksEveryFileAndShowsTheProblems()
    {
        _temp.CreateFile("Photos/ok.txt", text: "hello\n");
        _temp.CreateFile("Photos/changed.txt", text: "hello!\n");
        const string Hash = "5891b5b522d5df086d0ff0b110fbd9d21bb4fc7163af34d08286a2e846f6be03";
        _ui.ChecksumFile = _temp.Combine("Photos.sha256");
        File.WriteAllText(_ui.ChecksumFile, $"{Hash}  Photos/ok.txt\n{Hash}  Photos/changed.txt\n");
        var viewModel = CreateViewModel();

        await viewModel.VerifyChecksumFileCommand.ExecuteAsync(null);

        var job = Assert.Single(viewModel.Jobs);
        Assert.True(job.IsVerification);
        Assert.Equal("Verify " + _ui.ChecksumFile, job.Title);
        Assert.Equal("Finished with errors", job.StatusText);
        Assert.StartsWith("verified: 1, failed: 1", job.Summary, StringComparison.Ordinal);
        Assert.Equal(JobFileFilter.Problems, job.SelectedFilter.Filter);
        var problem = Assert.Single(job.Files);
        Assert.Equal("changed.txt", problem.Name);
        Assert.Contains("does not match", problem.Message, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task VerifyChecksumFile_NothingChosen_NoJob()
    {
        var viewModel = CreateViewModel();

        await viewModel.VerifyChecksumFileCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.Jobs);
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
        Assert.Equal(JobFileFilter.All, job.SelectedFilter.Filter); // no problems: all files are listed
        Assert.Equal(2, job.Files.Count);
        Assert.All(job.Files, r => Assert.Equal("Verified", r.Status));
        Assert.Equal((0, 0L, 2, 0), (job.QueuedCount, job.QueuedBytes, job.DoneCount, job.ProblemCount));
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
        Assert.Equal(JobFileFilter.Problems, job.SelectedFilter.Filter);
        Assert.Equal("Failed", Assert.Single(job.Files).Status);

        job.SelectedFilter = job.FilterOptions.First(o => o.Filter == JobFileFilter.All);
        Assert.Equal(2, job.Files.Count);
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
    public async Task RunningJob_ListsTheQueuedFilesAndWhereTheyGo()
    {
        _temp.CreateFile("dst/b.txt", text: "old"); // the second file stops on a conflict
        _ui.ConflictGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = CreateViewModel();
        viewModel.AddSources([_temp.CreateFile("src/a.txt", 3000), _temp.CreateFile("src/b.txt", 2000), _temp.CreateFile("src/c.txt", 1000)]);
        viewModel.Destination = _temp.Combine("dst");
        var copy = viewModel.CopyCommand.ExecuteAsync(null);
        await WaitUntil(() => _ui.ConflictsAsked == 1);
        // Small files are copied in parallel: a and c finish while b waits for the answer
        await WaitUntil(() => viewModel.Jobs[0].DoneCount == 2);
        var job = viewModel.Jobs[0];

        Assert.True(job.HasFiles);
        Assert.Equal(3, job.Files.Count);
        Assert.Equal(_temp.Combine("dst", "b.txt"), job.Files[1].DestinationPath); // where it goes, before it is copied
        Assert.False(job.Files[1].IsDone);
        Assert.True(job.Files[0].IsOk);
        Assert.True(job.Files[2].IsOk);
        Assert.Equal((1, 2000L), (job.QueuedCount, job.QueuedBytes));
        Assert.StartsWith("Queued: 1 (", job.FilesSummary, StringComparison.Ordinal);

        job.SelectedFilter = job.FilterOptions.First(o => o.Filter == JobFileFilter.Queued);
        Assert.Equal(["b.txt"], job.Files.Select(f => f.Name));

        _ui.ConflictGate.SetResult();
        await copy;
        Assert.Equal((0, 3, 1), (job.QueuedCount, job.DoneCount, job.ProblemCount)); // b skipped (the fake answers Skip)
        Assert.Equal(JobFileFilter.Problems, job.SelectedFilter.Filter); // at the end, the problems first
        Assert.Equal("b.txt", Assert.Single(job.Files).Name);
    }

    [AvaloniaFact]
    public void ManyFiles_ListAndUpdateQuickly()
    {
        // The list side only (the copy itself is measured elsewhere): 20,000 planned files, all finished
        const int count = 20_000;
        var request = new CopyRequest { Sources = ["/src"], Destination = "/dst" };
        using var job = new JobViewModel(request, "/", _ui, new DriveScheduler(), (_, _) => Task.FromResult(new ConflictAnswer(ConflictAction.Skip, false)));
        var planned = Enumerable.Range(0, count).Select(i => new PlannedFile(CopyItemKind.File, $"/src/f{i}", $"/dst/f{i}", 10)).ToList();
        var started = System.Diagnostics.Stopwatch.StartNew();

        job.OnPlanned(planned);
        job.SelectedFilter = job.FilterOptions.First(o => o.Filter == JobFileFilter.Queued); // the slowest: rows leave the list
        foreach (var file in planned)
        {
            job.Apply(new FileCopyResult { SourcePath = file.SourcePath, DestinationPath = file.DestinationPath, Length = 10 });
        }

        Assert.Equal((0, count), (job.QueuedCount, job.DoneCount));
        Assert.Empty(job.Files);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(20), $"took {started.Elapsed}");
    }

    [AvaloniaFact]
    public async Task PasteInto_CopiesAtOnceAndLeavesTheListAlone()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSources([_temp.CreateFile("being-prepared.txt")]);
        var cut = _temp.CreateFile("src/a.txt");
        _ui.Clipboard = new ClipboardFiles([cut], IsCut: true); // as Explorer leaves it after Ctrl+X

        await viewModel.PasteInto(_temp.Combine("dst"), _ui.Clipboard);

        var job = Assert.Single(viewModel.Jobs);
        Assert.Equal(CopyMode.Move, job.Mode); // cut in Explorer: moved
        Assert.True(File.Exists(_temp.Combine("dst", "a.txt")));
        Assert.False(File.Exists(cut));
        Assert.Single(viewModel.Sources); // what the user was preparing stays
        Assert.True(_ui.ClipboardCleared); // like Explorer after a cut and paste
    }

    [AvaloniaFact]
    public async Task SameFileTwice_BothRowsFinish()
    {
        // A file, and the folder that contains it: two copies of the same source
        var file = _temp.CreateFile("src/Photos/a.jpg");
        var viewModel = CreateViewModel();
        viewModel.AddSources([file, _temp.Combine("src", "Photos")]);
        viewModel.Destination = _temp.Combine("dst");

        await viewModel.CopyCommand.ExecuteAsync(null);

        var job = Assert.Single(viewModel.Jobs);
        Assert.Equal((0, 0L, 2), (job.QueuedCount, job.QueuedBytes, job.DoneCount));
        Assert.All(job.Files, f => Assert.True(f.IsOk));
        Assert.Equal([_temp.Combine("dst", "a.jpg"), _temp.Combine("dst", "Photos", "a.jpg")], job.Files.Select(f => f.DestinationPath));
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
