using System.Collections.ObjectModel;
using System.Globalization;
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
    private readonly DriveScheduler _scheduler = new();
    private readonly SemaphoreSlim _conflictDialog = new(1, 1);
    private IReadOnlyList<string>? _cutPaths;
    private bool _closing;

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
        Jobs.CollectionChanged += (_, _) => OnJobsChanged();
    }

    /// <summary>The installed version, without the commit: "0.4.0".</summary>
    public string Version { get; } = AppInfo.Version.Split('+')[0];

    /// <summary>The window title, with the version so that it is always in sight: "GBFileCopier 0.4.0".</summary>
    public string Title => $"{AppInfo.Name} {Version}";

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

    /// <summary>Messages about the files being prepared, for example after a paste.</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = Strings.IdleStatus;

    /// <summary>All the jobs of this session, oldest first: waiting, running and finished.</summary>
    public ObservableCollection<JobViewModel> Jobs { get; } = [];

    public bool HasJobs => Jobs.Count > 0;

    /// <summary>The job whose results are shown.</summary>
    [ObservableProperty]
    public partial JobViewModel? SelectedJob { get; set; }

    /// <summary>The update bar; set by the app (null in most tests).</summary>
    public UpdateViewModel? Updates { get; set; }

    /// <summary>Some job is waiting or running: closing the window cancels it.</summary>
    public bool HasActiveJobs => Jobs.Any(j => j.IsActive);

    /// <summary>Saved settings, for example the window size to restore.</summary>
    public AppSettings Settings => _settings;

    public void Dispose()
    {
        foreach (var job in Jobs)
        {
            job.Dispose();
        }

        _conflictDialog.Dispose();
    }

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
        _cutPaths = clipboard.IsCut ? clipboard.Paths : null;
        PreferMove = move || clipboard.IsCut;
        StatusText = string.Format(CultureInfo.CurrentCulture, PreferMove ? Strings.PastedThenMove : Strings.PastedThenCopy, clipboard.Paths.Count);
    }

    /// <summary>
    /// Applies a request from the Finder extension or a gbfilecopier:// link: fills in the window,
    /// never starts the copy. Running jobs go on meanwhile.
    /// </summary>
    public void Apply(ExternalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
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

    private bool CanRemove() => SelectedSource is not null;

    [RelayCommand]
    private void ClearSources()
    {
        Sources.Clear();
        PreferMove = false;
        _cutPaths = null;
    }

    /// <summary>Adds a copy job; it starts as soon as its destination drive is free.</summary>
    [RelayCommand(CanExecute = nameof(CanStart), AllowConcurrentExecutions = true)]
    private Task CopyAsync() => AddJob(CopyMode.Copy);

    [RelayCommand(CanExecute = nameof(CanStart), AllowConcurrentExecutions = true)]
    private Task MoveAsync() => AddJob(CopyMode.Move);

    private bool CanStart() => !_closing && Sources.Count > 0 && !string.IsNullOrWhiteSpace(Destination);

    private void NotifyCanStart()
    {
        CopyCommand.NotifyCanExecuteChanged();
        MoveCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(HasFinishedJobs))]
    private void ClearFinished()
    {
        foreach (var job in Jobs.Where(j => j.IsFinished).ToList())
        {
            Jobs.Remove(job);
            job.Dispose();
        }
    }

    private bool HasFinishedJobs() => Jobs.Any(j => j.IsFinished);

    /// <summary>
    /// The window is closing: cancels every job and waits until they have cleaned up (no temporary
    /// files left). No new job can start meanwhile.
    /// </summary>
    public async Task CancelAllAsync()
    {
        _closing = true;
        NotifyCanStart();
        foreach (var job in Jobs)
        {
            job.Cancel();
        }

        await Task.WhenAll(Jobs.Select(j => j.Completion).OfType<Task>());
    }

    /// <summary>
    /// Turns the files and the destination into a job and empties the list, ready for the next one.
    /// The returned task completes when the job is over.
    /// </summary>
    private Task AddJob(CopyMode mode)
    {
        var destination = Destination.Trim();
        var request = new CopyRequest
        {
            Sources = [.. Sources.Select(s => s.Path)],
            Destination = destination,
            Mode = mode,
            Options = new CopyOptions
            {
                HashAlgorithm = SelectedHash.Kind,
                Verify = Verify,
                ConflictPolicy = SelectedConflict.Policy,
            },
        };

        SaveSettings();
        var job = new JobViewModel(request, DestinationDrive.KeyFor(destination), _ui, _scheduler, AskConflictAsync,
            mode == CopyMode.Move ? _cutPaths : null);
        job.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(JobViewModel.IsActive) or nameof(JobViewModel.IsFinished))
            {
                OnJobsChanged();
            }
        };
        Jobs.Add(job);
        SelectedJob = job;

        Sources.Clear();
        PreferMove = false;
        _cutPaths = null;
        StatusText = Strings.JobAdded;
        return job.Start();
    }

    private void OnJobsChanged()
    {
        OnPropertyChanged(nameof(HasJobs));
        OnPropertyChanged(nameof(HasActiveJobs));
        ClearFinishedCommand.NotifyCanExecuteChanged();
        Updates?.NotifyJobsChanged();
    }

    /// <summary>One conflict dialog at a time, also when jobs to different drives run together.</summary>
    private async Task<ConflictAnswer> AskConflictAsync(FileConflict conflict, CancellationToken cancellationToken)
    {
        await _conflictDialog.WaitAsync(cancellationToken);
        try
        {
            return await _ui.AskConflictAsync(conflict);
        }
        finally
        {
            _conflictDialog.Release();
        }
    }
}
