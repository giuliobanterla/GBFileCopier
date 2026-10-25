using GBFileCopier.Core;

namespace GBFileCopier.App.Services.Updates;

/// <summary>A downloaded and checked update, ready to install.</summary>
/// <param name="Version">Its version.</param>
/// <param name="Path">The setup (Windows) or the .app taken out of the disk image (macOS).</param>
/// <param name="Sha256">The file's checksum when it was checked, to check it again before running it.</param>
public sealed record PreparedUpdate(UpdateVersion Version, string Path, string? Sha256 = null);

/// <summary>What the update banner needs; the app uses <see cref="UpdateService"/>, tests a fake.</summary>
public interface IUpdateService
{
    UpdateVersion CurrentVersion { get; }

    /// <summary>The update can be installed from the app; otherwise the release page is opened instead.</summary>
    bool CanInstall { get; }

    /// <summary>The latest release; null if there is none.</summary>
    Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken);

    /// <summary>Downloads the package for this system, checks it and gets it ready to install.</summary>
    /// <param name="progress">From 0 to 1.</param>
    Task<PreparedUpdate> DownloadAsync(ReleaseInfo release, IProgress<double> progress, CancellationToken cancellationToken);

    /// <summary>Starts the installation.</summary>
    /// <returns>True if the app must quit now (macOS); on Windows setup closes the app itself.</returns>
    /// <exception cref="UpdateException">The installation did not happen.</exception>
    Task<bool> InstallAsync(PreparedUpdate update);
}

public sealed class UpdateService : IUpdateService, IDisposable
{
    private readonly HttpClient _http;
    private readonly GitHubReleases _releases;
    private readonly UpdateDownloader _downloader;
    private readonly IUpdateInstaller? _installer;

    public UpdateService(IUpdateInstaller? installer = null, HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromMinutes(10);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppInfo.Name}/{UpdateVersion.Current}"); // required by the GitHub API
        _releases = new GitHubReleases(_http);
        _downloader = new UpdateDownloader(_http);
        _installer = installer ?? IUpdateInstaller.ForThisSystem();
    }

    public UpdateVersion CurrentVersion => UpdateVersion.Current;

    public bool CanInstall => _installer?.CanInstall == true;

    /// <summary>Where the downloads go; removed by the installation (macOS) or at the next download.</summary>
    public static string DownloadFolder { get; } = Path.Combine(Path.GetTempPath(), "GBFileCopier-update");

    public void Dispose() => _http.Dispose();

    public Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken) => _releases.GetLatestAsync(cancellationToken);

    public async Task<PreparedUpdate> DownloadAsync(ReleaseInfo release, IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        var installer = _installer ?? throw new UpdateException(Resources.Strings.UpdateFailedGeneric);
        var asset = release.Find(installer.PackageName(release.Version)) ?? throw new UpdateException(Resources.Strings.UpdateNoPackage);
        CleanDownloadFolder();
        var package = await _downloader.DownloadAsync(release, asset, DownloadFolder, progress, cancellationToken);
        return await installer.PrepareAsync(package, release.Version, cancellationToken);
    }

    public Task<bool> InstallAsync(PreparedUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        return (_installer ?? throw new UpdateException(Resources.Strings.UpdateFailedGeneric)).LaunchAsync(update, CancellationToken.None);
    }

    private static void CleanDownloadFolder()
    {
        try
        {
            if (Directory.Exists(DownloadFolder))
            {
                Directory.Delete(DownloadFolder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A previous download still in use: the new one overwrites its files
        }
    }
}
