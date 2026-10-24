using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GBFileCopier.App.Resources;
using GBFileCopier.App.Services;
using GBFileCopier.App.Settings;
using GBFileCopier.Core;
using GBFileCopier.Core.Copying;

namespace GBFileCopier.App.ViewModels;

public sealed partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly IUiServices _ui;
    private readonly ISettingsStore _settingsStore;
    private AppSettings _settings;
    private readonly List<ResultItem> _allResults = [];
    private CopyJob? _job;
    private CancellationTokenSource? _cancellation;
    private ConflictAction? _conflictAnswerForAll;
    private bool _sourcesFromCut;

    public MainViewModel(IUiServices ui, ISettingsStore settingsStore)
    {
        _ui = ui;
        _settingsStore = settingsStore;

        // The options of the last run
        _settings = settingsStore.Load();
        Destination = _settings.LastDestination;
        Verify = _settings.Verify;
        SelectedHash = HashOptions.FirstOrDefault(o => o.Kind == _settings.HashAlgorithm) ?? HashOptions[0];
        SelectedConflict = ConflictOptions.FirstOrDefault(o => o.Policy == _settings.ConflictPolicy) ?? ConflictOptions[0];
        Sources.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasSources));
            NotifyCanStart();
        };
    }

    public string Title { get; } = AppInfo.Name;

    public string Version { get; } = AppInfo.Version.Split('+')[0];

    public ObservableCollection<SourceItem> Sources { get; } = [];

    public bool HasSources => Sources.Count > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedCommand))]
    public partial SourceItem? SelectedSource { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyCommand), nameof(MoveCommand))]
    public partial string Destination { get; set; } = string.Empty;

    public IReadOnlyList<HashOption> HashOptions { get; } = HashOption.CreateAll();

    public IReadOnlyList<ConflictOption> ConflictOptions { get; } = ConflictOption.CreateAll();

    [ObservableProperty]
    public partial bool Verify { get; set; }

    [ObservableProperty]
    public partial HashOption SelectedHash { get; set; }

    [ObservableProperty]
    public partial ConflictOption SelectedConflict { get; set; }

    /// <summary>The sources were cut (or pasted with ⌥⌘V): Move becomes the default button.</summary>
    [ObservableProperty]
    public partial bool PreferMove { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(CopyCommand), nameof(MoveCommand), nameof(TogglePauseCommand), nameof(CancelCommand), nameof(RemoveSelectedCommand))]
    public partial bool IsRunning { get; set; }

    public bool IsIdle => !IsRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PauseLabel))]
    public partial bool IsPaused { get; set; }

    public string PauseLabel => IsPaused ? Strings.Resume : Strings.Pause;

    /// <summary>A job has run: the progress and results panel is visible.</summary>
    [ObservableProperty]
    public partial bool HasRun { get; set; }

    [ObservableProperty]
    public partial double ProgressPercent { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = Strings.IdleStatus;

    [ObservableProperty]
    public partial string DetailText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? Summary { get; set; }

    public ObservableCollection<ResultItem> Results { get; } = [];

    [ObservableProperty]
    public partial bool ShowAllResults { get; set; }

    [ObservableProperty]
    public partial int ProblemCount { get; set; }

    /// <summary>The running job, so that the window can wait for it before closing.</summary>
    public Task? RunningTask { get; private set; }

    /// <summary>Saved settings, for example the window size to restore.</summary>
    public AppSettings Settings => _settings;

    public void Dispose() => _cancellation?.Dispose();

    /// <summary>Remembers the current options (and the window size, when closing).</summary>
    public void SaveSettings(double? windowWidth = null, double? windowHeight = null)
    {
        // Reload first: the language may have been changed in the Settings window meanwhile
        _settings = _settingsStore.Load() with
        {
            LastDestination = Destination.Trim(),
            Verify = Verify,
            HashAlgorithm = SelectedHash.Kind,
            ConflictPolicy = SelectedConflict.Policy,
            WindowWidth = windowWidth ?? _settings.WindowWidth,
            WindowHeight = windowHeight ?? _settings.WindowHeight,
        };
        _settingsStore.Save(_settings);
    }

    partial void OnShowAllResultsChanged(bool value) => RefreshResults();

    /// <summary>Adds files and folders, ignoring duplicates.</summary>
    public void AddSources(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            var item = new SourceItem(path);
            if (!Sources.Any(s => string.Equals(s.Path, item.Path, StringComparison.Ordinal)))
            {
                Sources.Add(item);
            }
        }
    }

    [RelayCommand]
    private async Task AddFilesAsync() => AddSources(await _ui.PickFilesAsync());

    [RelayCommand]
    private async Task AddFolderAsync() => AddSources(await _ui.PickFoldersAsync(Strings.PickFoldersTitle, allowMultiple: true));

    [RelayCommand]
    private async Task BrowseDestinationAsync()
    {
        var folders = await _ui.PickFoldersAsync(Strings.PickDestinationTitle, allowMultiple: false);
        if (folders.Count > 0)
        {
            Destination = folders[0];
        }
    }

    /// <summary>
    /// ⌘V / Ctrl+V: adds the files copied in the Finder or Explorer. With <paramref name="move"/>
    /// (⌥⌘V, like the Finder) or when they were cut in Explorer, Move becomes the default action.
    /// </summary>
    [RelayCommand]
    public void Paste(bool move)
    {
        var clipboard = _ui.ReadClipboard();
        if (clipboard is null)
        {
            StatusText = Strings.NoFilesOnClipboard;
            return;
        }

        AddSources(clipboard.Paths);
        _sourcesFromCut = clipboard.IsCut;
        PreferMove = move || clipboard.IsCut;
        StatusText = string.Format(CultureInfo.CurrentCulture, PreferMove ? Strings.PastedThenMove : Strings.PastedThenCopy, clipboard.Paths.Count);
    }

    /// <summary>
    /// Applies a request from the Finder extension or a gbfilecopier:// link: fills in the window,
    /// never starts the copy. Ignored while a copy is running.
    /// </summary>
    public void Apply(ExternalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (IsRunning)
        {
            return;
        }

        if (request.Destination is { } destination)
        {
            Destination = destination;
        }

        if (request.PasteFromClipboard)
        {
            Paste(request.Move);
        }
        else
        {
            AddSources(request.Sources);
            PreferMove = request.Move;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void RemoveSelected()
    {
        if (SelectedSource is { } selected)
        {
            var index = Sources.IndexOf(selected);
            Sources.Remove(selected);
            SelectedSource = Sources.Count == 0 ? null : Sources[Math.Min(index, Sources.Count - 1)];
        }
    }

    private bool CanRemove() => SelectedSource is not null && !IsRunning;

    [RelayCommand]
    private void ClearSources()
    {
        Sources.Clear();
        PreferMove = false;
        _sourcesFromCut = false;
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task CopyAsync() => RunAsync(CopyMode.Copy);

    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task MoveAsync() => RunAsync(CopyMode.Move);

    private bool CanStart() => !IsRunning && Sources.Count > 0 && !string.IsNullOrWhiteSpace(Destination);

    private void NotifyCanStart()
    {
        CopyCommand.NotifyCanExecuteChanged();
        MoveCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void TogglePause()
    {
        if (_job is null)
        {
            return;
        }

        if (IsPaused)
        {
            _job.Resume();
            IsPaused = false;
        }
        else
        {
            _job.Pause();
            IsPaused = true;
            StatusText = Strings.Paused;
        }
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Cancel()
    {
        StatusText = Strings.Cancelling;
        _cancellation?.Cancel();
    }

    private Task RunAsync(CopyMode mode)
    {
        RunningTask = RunCoreAsync(mode);
        return RunningTask;
    }

    private async Task RunCoreAsync(CopyMode mode)
    {
        var request = new CopyRequest
        {
            Sources = [.. Sources.Select(s => s.Path)],
            Destination = Destination.Trim(),
            Mode = mode,
            Options = new CopyOptions
            {
                HashAlgorithm = SelectedHash.Kind,
                Verify = Verify,
                ConflictPolicy = SelectedConflict.Policy,
            },
        };

        SaveSettings();
        _allResults.Clear();
        Results.Clear();
        Summary = null;
        ProblemCount = 0;
        ProgressPercent = 0;
        DetailText = string.Empty;
        StatusText = Strings.Scanning;
        _conflictAnswerForAll = null;
        HasRun = true;
        IsRunning = true;

        _job = new CopyJob(request, _ui.Platform);
        _cancellation = new CancellationTokenSource();
        // Created on the UI thread: reports from the copy threads are posted back to it
        var progress = new Progress<CopyProgress>(OnProgress);
        try
        {
            var result = await _job.RunAsync(progress, ResolveConflictAsync, _cancellation.Token);
            IsRunning = false; // before ShowResult: late progress reports are then ignored
            ShowResult(result);
        }
        finally
        {
            IsRunning = false;
            IsPaused = false;
            _job = null;
            _cancellation.Dispose();
            _cancellation = null;
        }
    }

    private void OnProgress(CopyProgress progress)
    {
        if (!IsRunning)
        {
            return;
        }

        ProgressPercent = progress.Fraction * 100;
        if (IsPaused)
        {
            return;
        }

        StatusText = progress.Phase switch
        {
            CopyPhase.Scanning => Strings.Scanning,
            CopyPhase.Verifying => string.Format(CultureInfo.CurrentCulture, Strings.VerifyingFile, Path.GetFileName(progress.CurrentFile)),
            CopyPhase.Finished => Strings.Finishing,
            _ => string.Format(CultureInfo.CurrentCulture, Strings.CopyingFile, Path.GetFileName(progress.CurrentFile)),
        };

        var speed = progress.BytesPerSecond;
        var remaining = speed > 0 ? TimeSpan.FromSeconds((progress.TotalBytes - progress.ProcessedBytes) / speed) : (TimeSpan?)null;
        DetailText = string.Format(CultureInfo.CurrentCulture, Strings.ProgressDetail, progress.CompletedFiles, progress.TotalFiles, ByteSize.Format(speed, CultureInfo.CurrentCulture)) +
                     (remaining is { } r && progress.ProcessedBytes > 0 ? "  ·  " + string.Format(CultureInfo.CurrentCulture, Strings.TimeLeft, FormatDuration(r)) : string.Empty);
    }

    private async ValueTask<ConflictAction> ResolveConflictAsync(FileConflict conflict, CancellationToken cancellationToken)
    {
        // Called on a copy thread; the engine waits for the answer before going on
        if (_conflictAnswerForAll is { } remembered)
        {
            return remembered;
        }

        var answer = await Dispatcher.UIThread.InvokeAsync(() => _ui.AskConflictAsync(conflict));
        if (answer.ApplyToAll)
        {
            _conflictAnswerForAll = answer.Action;
        }

        return answer.Action;
    }

    private void ShowResult(CopyJobResult result)
    {
        _allResults.AddRange(result.Files.Select(f => new ResultItem(f, result.Mode)));
        ProblemCount = _allResults.Count(r => r.IsProblem);
        ShowAllResults = ProblemCount == 0;
        RefreshResults();

        var verified = result.Files.Count(f => f.Verified);
        var parts = new List<string>
        {
            string.Format(CultureInfo.CurrentCulture, result.Mode == CopyMode.Move ? Strings.SummaryMoved : Strings.SummaryCopied,
                result.SucceededCount, ByteSize.Format(result.CopiedBytes, CultureInfo.CurrentCulture)),
        };
        void AddPart(int count, string format)
        {
            if (count > 0)
            {
                parts.Add(string.Format(CultureInfo.CurrentCulture, format, count));
            }
        }

        AddPart(verified, Strings.SummaryVerified);
        AddPart(result.SkippedCount, Strings.SummarySkipped);
        AddPart(result.FailedCount, Strings.SummaryFailed);
        AddPart(result.CancelledCount, Strings.SummaryNotProcessed);
        Summary = string.Join(", ", parts) + "  ·  " + FormatDuration(result.Elapsed);

        StatusText = result.WasCancelled ? Strings.Cancelled
            : result.FailedCount > 0 ? Strings.FinishedWithErrors
            : Strings.Done;
        DetailText = string.Empty;
        if (!result.WasCancelled)
        {
            ProgressPercent = 100;
        }

        if (result.Mode == CopyMode.Move)
        {
            // Moved sources are gone: keep in the list only what is left (failed or skipped)
            foreach (var source in Sources.Where(s => !Path.Exists(s.Path)).ToList())
            {
                Sources.Remove(source);
            }

            // Like Explorer: after a cut and paste the clipboard points to files that are gone
            if (_sourcesFromCut && result.IsSuccess)
            {
                _ui.ClearClipboard();
            }

            PreferMove = Sources.Count > 0 && PreferMove;
            _sourcesFromCut = false;
        }
    }

    private void RefreshResults()
    {
        Results.Clear();
        foreach (var item in ShowAllResults ? _allResults : _allResults.Where(r => r.IsProblem))
        {
            Results.Add(item);
        }
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1 ? duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
        : duration.TotalSeconds >= 1 ? duration.ToString(@"m\:ss", CultureInfo.InvariantCulture)
        : "0:00";
}
