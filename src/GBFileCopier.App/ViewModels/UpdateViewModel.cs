using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GBFileCopier.App.Resources;
using GBFileCopier.App.Services.Updates;
using GBFileCopier.App.Settings;

namespace GBFileCopier.App.ViewModels;

public enum UpdateState
{
    Hidden,

    /// <summary>First start: may the app look for updates by itself?</summary>
    AskAutomatic,
    Checking,
    UpToDate,
    Available,
    Downloading,
    Installing,
    Failed,
}

/// <summary>
/// The update bar at the top of the window. The app contacts GitHub only after the user agreed to
/// automatic checks, or chose "Check for Updates…"; installing always needs a click.
/// </summary>
public sealed partial class UpdateViewModel : ViewModelBase, IDisposable
{
    /// <summary>Automatic checks happen at most this often.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromDays(1);

    private readonly IUpdateService _service;
    private readonly ISettingsStore _settings;
    private readonly Func<bool> _canRestart;
    private readonly Action _quit;
    private readonly Action<Uri> _openPage;
    private readonly Func<DateTime> _now;
    private ReleaseInfo? _release;
    private PreparedUpdate? _prepared;
    private CancellationTokenSource? _download;
    private DateTime? _lastCheck;

    /// <param name="service">GitHub, download and installation.</param>
    /// <param name="settings">Where the answer about automatic checks is kept.</param>
    /// <param name="canRestart">False while jobs are running: the app cannot quit to install.</param>
    /// <param name="quit">Quits the app, so that the installer can replace it.</param>
    /// <param name="openPage">Opens a web page in the browser.</param>
    /// <param name="now">The clock (for tests).</param>
    public UpdateViewModel(IUpdateService service, ISettingsStore settings, Func<bool> canRestart, Action quit, Action<Uri> openPage, Func<DateTime>? now = null)
    {
        _service = service;
        _settings = settings;
        _canRestart = canRestart;
        _quit = quit;
        _openPage = openPage;
        _now = now ?? (() => DateTime.UtcNow);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible), nameof(IsAsking), nameof(IsAvailable), nameof(IsDownloading), nameof(CanClose))]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    public partial UpdateState State { get; private set; }

    public bool IsVisible => State != UpdateState.Hidden;

    public bool IsAsking => State == UpdateState.AskAutomatic;

    public bool IsAvailable => State == UpdateState.Available;

    public bool IsDownloading => State == UpdateState.Downloading;

    /// <summary>Messages that only need a Close button.</summary>
    public bool CanClose => State is UpdateState.UpToDate or UpdateState.Failed;

    [ObservableProperty]
    public partial string Message { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial double ProgressPercent { get; private set; }

    /// <summary>"Install and restart", or "Download…" where the app cannot replace itself (portable copy).</summary>
    public string InstallLabel => _service.CanInstall ? Strings.UpdateInstall : Strings.UpdateDownloadPage;

    /// <summary>The update is ready but jobs are running.</summary>
    public bool WaitsForJobs => IsAvailable && _service.CanInstall && !_canRestart();

    public void Dispose() => _download?.Dispose();

    /// <summary>At startup: ask about automatic checks the first time, otherwise check if allowed.</summary>
    public Task StartAsync()
    {
        switch (_settings.Load().CheckForUpdates)
        {
            case null:
                Message = Strings.UpdateAskAutomatic;
                State = UpdateState.AskAutomatic;
                return Task.CompletedTask;
            case true:
                return CheckAsync(manual: false);
            default:
                return Task.CompletedTask;
        }
    }

    /// <summary>Called by a timer: checks again if automatic checks are on and the last one is old enough.</summary>
    public Task TickAsync() =>
        _settings.Load().CheckForUpdates == true && State == UpdateState.Hidden && (_lastCheck is null || _now() - _lastCheck >= CheckInterval)
            ? CheckAsync(manual: false)
            : Task.CompletedTask;

    /// <summary>Jobs started or ended: the Install button follows.</summary>
    public void NotifyJobsChanged()
    {
        OnPropertyChanged(nameof(WaitsForJobs));
        InstallCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private Task EnableAutomaticAsync()
    {
        _settings.Save(_settings.Load() with { CheckForUpdates = true });
        State = UpdateState.Hidden;
        return CheckAsync(manual: false);
    }

    [RelayCommand]
    private void DisableAutomatic()
    {
        _settings.Save(_settings.Load() with { CheckForUpdates = false });
        State = UpdateState.Hidden;
    }

    /// <summary>Help → Check for Updates…: always answers, also when there is nothing new.</summary>
    [RelayCommand]
    public Task CheckNowAsync() => CheckAsync(manual: true);

    private async Task CheckAsync(bool manual)
    {
        if (State is UpdateState.Checking or UpdateState.Downloading or UpdateState.Installing)
        {
            return;
        }

        if (manual)
        {
            Message = Strings.UpdateChecking;
            State = UpdateState.Checking;
        }

        _lastCheck = _now();
        try
        {
            _release = await _service.GetLatestAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UpdateException)
        {
            // Automatic checks fail silently (offline, for example); they are tried again later
            ShowIf(manual, UpdateState.Failed, string.Format(CultureInfo.CurrentCulture, Strings.UpdateCheckFailed, ex.Message));
            return;
        }

        if (_release is { } release && release.Version > _service.CurrentVersion)
        {
            _prepared = null;
            Message = string.Format(CultureInfo.CurrentCulture, Strings.UpdateAvailable, release.Version, _service.CurrentVersion);
            State = UpdateState.Available;
            OnPropertyChanged(nameof(InstallLabel));
            OnPropertyChanged(nameof(WaitsForJobs));
        }
        else
        {
            ShowIf(manual, UpdateState.UpToDate, string.Format(CultureInfo.CurrentCulture, Strings.UpdateUpToDate, _service.CurrentVersion));
        }
    }

    private void ShowIf(bool manual, UpdateState state, string message)
    {
        if (manual)
        {
            Message = message;
            State = state;
        }
        else if (State == UpdateState.Checking)
        {
            State = UpdateState.Hidden;
        }
    }

    [RelayCommand(CanExecute = nameof(CanInstallNow))]
    private async Task InstallAsync()
    {
        if (_release is not { } release)
        {
            return;
        }

        if (!_service.CanInstall)
        {
            _openPage(release.Page);
            return;
        }

        if (_prepared is null)
        {
            _download = new CancellationTokenSource();
            Message = string.Format(CultureInfo.CurrentCulture, Strings.UpdateDownloading, release.Version);
            ProgressPercent = 0;
            State = UpdateState.Downloading;
            try
            {
                _prepared = await _service.DownloadAsync(release, new Progress<double>(p => ProgressPercent = p * 100), _download.Token);
            }
            catch (OperationCanceledException) when (_download.IsCancellationRequested)
            {
                Message = string.Format(CultureInfo.CurrentCulture, Strings.UpdateAvailable, release.Version, _service.CurrentVersion);
                State = UpdateState.Available;
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UpdateException or IOException or UnauthorizedAccessException)
            {
                Message = string.Format(CultureInfo.CurrentCulture, Strings.UpdateFailed, ex.Message);
                State = UpdateState.Failed;
                return;
            }
            finally
            {
                _download.Dispose();
                _download = null;
            }
        }

        if (!_canRestart())
        {
            // Jobs started during the download: install when they are over
            Message = string.Format(CultureInfo.CurrentCulture, Strings.UpdateAvailable, release.Version, _service.CurrentVersion);
            State = UpdateState.Available;
            OnPropertyChanged(nameof(WaitsForJobs));
            return;
        }

        Message = string.Format(CultureInfo.CurrentCulture, Strings.UpdateInstalling, release.Version);
        State = UpdateState.Installing;
        bool quit;
        try
        {
            quit = await _service.InstallAsync(_prepared);
        }
        catch (Exception ex) when (ex is UpdateException or InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            // For example: the administrator rights were refused, or the download was removed meanwhile.
            // The next click downloads and checks it again.
            _prepared = null;
            Message = string.Format(CultureInfo.CurrentCulture, Strings.UpdateFailed, ex.Message);
            State = UpdateState.Failed;
            return;
        }

        if (quit)
        {
            _quit();
        }
    }

    private bool CanInstallNow() => State == UpdateState.Available && (!_service.CanInstall || _canRestart());

    [RelayCommand]
    private void CancelDownload() => _download?.Cancel();

    [RelayCommand]
    private void WhatsNew()
    {
        if (_release is { } release)
        {
            _openPage(release.Page);
        }
    }

    /// <summary>Later / Close: hides the bar until the next check.</summary>
    [RelayCommand]
    private void Dismiss() => State = UpdateState.Hidden;
}
