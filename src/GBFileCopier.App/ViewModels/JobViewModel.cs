using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GBFileCopier.App.Resources;
using GBFileCopier.App.Services;
using GBFileCopier.Core;
using GBFileCopier.Core.Copying;

namespace GBFileCopier.App.ViewModels;

/// <summary>
/// One copy or move in the jobs list: it waits until no other job writes to its destination drive,
/// runs, then keeps its results.
/// </summary>
public sealed partial class JobViewModel : ViewModelBase, IDisposable
{
    private readonly CopyRequest _request;
    private readonly IUiServices _ui;
    private readonly DriveScheduler _scheduler;
    private readonly Func<FileConflict, CancellationToken, Task<ConflictAnswer>> _askConflict;
    private readonly IReadOnlyList<string>? _cutPaths;
    private readonly List<ResultItem> _allResults = [];
    private readonly CancellationTokenSource _cancellation = new();
    private readonly SpeedMeter _speed = new();
    private CopyJob? _job;
    private ConflictAction? _conflictAnswerForAll;

    /// <param name="request">What to copy.</param>
    /// <param name="driveKey">The destination drive, from <see cref="DestinationDrive.KeyFor(string)"/>.</param>
    /// <param name="ui">Platform and clipboard.</param>
    /// <param name="scheduler">Shared by all the jobs.</param>
    /// <param name="askConflict">Shows the conflict dialog; called on the UI thread.</param>
    /// <param name="cutPaths">The sources were cut in Explorer: emptied from the clipboard after a successful move.</param>
    public JobViewModel(
        CopyRequest request,
        string driveKey,
        IUiServices ui,
        DriveScheduler scheduler,
        Func<FileConflict, CancellationToken, Task<ConflictAnswer>> askConflict,
        IReadOnlyList<string>? cutPaths = null)
    {
        _request = request;
        _ui = ui;
        _scheduler = scheduler;
        _askConflict = askConflict;
        _cutPaths = cutPaths;
        DriveKey = driveKey;
        Title = string.Format(CultureInfo.CurrentCulture, request.Mode == CopyMode.Move ? Strings.JobMoveTo : Strings.JobCopyTo, request.Destination);
        ItemsText = string.Format(CultureInfo.CurrentCulture, Strings.JobItems, request.Sources.Count);
    }

    public string Title { get; }

    public string ItemsText { get; }

    public string DriveKey { get; }

    public CopyMode Mode => _request.Mode;

    /// <summary>Waiting or running: it can be cancelled.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool IsActive { get; private set; }

    /// <summary>Another job is writing to the same drive.</summary>
    [ObservableProperty]
    public partial bool IsWaiting { get; private set; }

    /// <summary>Copying: it can be paused.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TogglePauseCommand))]
    [NotifyPropertyChangedFor(nameof(StatusLine))]
    public partial bool IsRunning { get; private set; }

    [ObservableProperty]
    public partial bool IsFinished { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PauseLabel))]
    public partial bool IsPaused { get; private set; }

    public string PauseLabel => IsPaused ? Strings.Resume : Strings.Pause;

    [ObservableProperty]
    public partial double ProgressPercent { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLine))]
    public partial string StatusText { get; private set; } = string.Empty;

    /// <summary>While copying: "File 3 of 120".</summary>
    [ObservableProperty]
    public partial string FilesText { get; private set; } = string.Empty;

    /// <summary>While copying: "42%  ·  85.3 MB/s  ·  12:40 left", on a line of its own so that it is never cut.</summary>
    [ObservableProperty]
    public partial string TransferText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLine))]
    public partial string? Summary { get; private set; }

    /// <summary>The line under the progress bar: the state (the file being copied), then the summary at the end.</summary>
    public string StatusLine =>
        IsRunning || string.IsNullOrEmpty(Summary) ? StatusText : StatusText + "  ·  " + Summary;

    public ObservableCollection<ResultItem> Results { get; } = [];

    [ObservableProperty]
    public partial bool ShowAllResults { get; set; }

    [ObservableProperty]
    public partial int ProblemCount { get; private set; }

    /// <summary>Completes when the job is over (also when cancelled); null before <see cref="Start"/>.</summary>
    public Task? Completion { get; private set; }

    public void Dispose() => _cancellation.Dispose();

    /// <summary>Queues the job for its drive and runs it. Call on the UI thread, once.</summary>
    public Task Start()
    {
        Completion ??= RunAsync();
        return Completion;
    }

    partial void OnShowAllResultsChanged(bool value) => RefreshResults();

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

    [RelayCommand(CanExecute = nameof(IsActive))]
    public void Cancel()
    {
        if (IsActive)
        {
            StatusText = Strings.Cancelling;
            _cancellation.Cancel();
        }
    }

    private async Task RunAsync()
    {
        IsActive = true;
        var token = _cancellation.Token;
        var turn = _scheduler.WaitForTurnAsync(DriveKey, token);
        if (!turn.IsCompleted)
        {
            IsWaiting = true;
            StatusText = string.Format(CultureInfo.CurrentCulture, Strings.WaitingForDrive, DriveName(DriveKey));
        }

        IDisposable driveTurn;
        try
        {
            driveTurn = await turn;
        }
        catch (OperationCanceledException)
        {
            IsWaiting = false;
            IsActive = false;
            StatusText = Strings.Cancelled;
            IsFinished = true;
            return;
        }

        using (driveTurn)
        {
            IsWaiting = false;
            StatusText = Strings.Scanning;
            IsRunning = true;
            try
            {
                _job = new CopyJob(_request, _ui.Platform);
                // Created on the UI thread: reports from the copy threads are posted back to it
                var progress = new Progress<CopyProgress>(OnProgress);
                var result = await _job.RunAsync(progress, ResolveConflictAsync, token);
                IsRunning = false; // before ShowResult: late progress reports are then ignored
                ShowResult(result);
            }
            finally
            {
                IsRunning = false;
                IsPaused = false;
                IsActive = false;
                IsFinished = true;
                _job = null;
            }
        }
    }

    private void OnProgress(CopyProgress progress)
    {
        if (!IsRunning)
        {
            return;
        }

        ProgressPercent = progress.Fraction * 100;
        if (IsPaused || _cancellation.IsCancellationRequested)
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

        FilesText = string.Format(CultureInfo.CurrentCulture, Strings.ProgressFiles, Math.Min(progress.CompletedFiles + 1, progress.TotalFiles), progress.TotalFiles);
        TransferText = FormatTransfer(progress, _speed.Add(progress.Elapsed, progress.ProcessedBytes));
    }

    private async ValueTask<ConflictAction> ResolveConflictAsync(FileConflict conflict, CancellationToken cancellationToken)
    {
        // Called on a copy thread; the engine waits for the answer before going on
        if (_conflictAnswerForAll is { } remembered)
        {
            return remembered;
        }

        var answer = await Dispatcher.UIThread.InvokeAsync(() => _askConflict(conflict, cancellationToken));
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
        FilesText = string.Empty;
        TransferText = string.Empty;
        if (!result.WasCancelled)
        {
            ProgressPercent = 100;
        }

        // Like Explorer: after a cut and paste the clipboard points to files that are gone.
        // Only if it still holds that cut: the job may have waited, and something else been copied since.
        if (result.Mode == CopyMode.Move && result.IsSuccess && _cutPaths is not null
            && _ui.ReadClipboard() is { IsCut: true } clipboard && clipboard.Paths.SequenceEqual(_cutPaths))
        {
            _ui.ClearClipboard();
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

    /// <summary>
    /// How to name a drive to the user: "Backup (D:)" on Windows, "Backup" for /Volumes/Backup on
    /// macOS, "the startup disk" for /; otherwise the mount point itself.
    /// </summary>
    public static string DriveName(string driveKey)
    {
        ArgumentNullException.ThrowIfNull(driveKey);
        if (driveKey == "/")
        {
            return Strings.StartupDisk;
        }

        if (driveKey.StartsWith("/Volumes/", StringComparison.Ordinal))
        {
            return Path.GetFileName(driveKey);
        }

        try
        {
            // On Unix the "label" is the mount point again; on Windows it is the name given to the disk
            var label = new DriveInfo(driveKey).VolumeLabel;
            return string.IsNullOrWhiteSpace(label) || label == driveKey ? driveKey : $"{label} ({driveKey.TrimEnd('\\')})";
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return driveKey; // a network share, or a drive that is not ready
        }
    }

    /// <summary>"42%  ·  85.3 MB/s  ·  12:40 left"; the time left once there is a speed to base it on.</summary>
    internal static string FormatTransfer(CopyProgress progress, double bytesPerSecond)
    {
        var percent = ((int)Math.Floor(progress.Fraction * 100)).ToString(CultureInfo.CurrentCulture) + "%";
        var text = percent + "  ·  " + ByteSize.Format(bytesPerSecond, CultureInfo.CurrentCulture) + "/s";
        if (bytesPerSecond > 0 && progress.ProcessedBytes > 0 && progress.TotalBytes > progress.ProcessedBytes)
        {
            var left = TimeSpan.FromSeconds(Math.Min((progress.TotalBytes - progress.ProcessedBytes) / bytesPerSecond, TimeSpan.FromDays(99).TotalSeconds));
            text += "  ·  " + string.Format(CultureInfo.CurrentCulture, Strings.TimeLeft, FormatDuration(left));
        }

        return text;
    }

    // Hours are not limited to 24: a transfer of 30 hours shows 30:00:00
    internal static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1 ? ((int)duration.TotalHours).ToString(CultureInfo.InvariantCulture) + duration.ToString(@"\:mm\:ss", CultureInfo.InvariantCulture)
        : duration.TotalSeconds >= 1 ? duration.ToString(@"m\:ss", CultureInfo.InvariantCulture)
        : "0:00";
}
