using System.Net;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Headless.XUnit;
using GBFileCopier.App.Services.Updates;
using GBFileCopier.App.ViewModels;
using GBFileCopier.Tests;

namespace GBFileCopier.App.Tests;

public sealed class UpdateTests : IDisposable
{
    private const string Repo = "giuliobanterla/GBFileCopier";
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // Versions

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("0.3.1+8d85fea805b6", "0.3.1")]
    [InlineData("1.2", "1.2.0")]
    [InlineData("v0.4.0-beta.1", "0.4.0-beta.1")]
    public void Version_Parse(string text, string expected) =>
        Assert.Equal(expected, UpdateVersion.Parse(text)!.ToString());

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData(null)]
    public void Version_Parse_RejectsNonVersions(string? text) => Assert.Null(UpdateVersion.Parse(text));

    [Theory]
    [InlineData("0.3.1", "0.4.0")]
    [InlineData("0.3.1", "0.3.2")]
    [InlineData("0.9.0", "0.10.0")] // numbers, not text
    [InlineData("0.4.0-beta.1", "0.4.0")]
    [InlineData("0.4.0-beta.1", "0.4.0-beta.2")]
    public void Version_Order(string older, string newer)
    {
        Assert.True(UpdateVersion.Parse(newer)! > UpdateVersion.Parse(older)!);
        Assert.True(UpdateVersion.Parse(older)! < UpdateVersion.Parse(newer)!);
    }

    [Fact]
    public void Version_BuildMetadataDoesNotCount() =>
        Assert.Equal(0, UpdateVersion.Parse("0.3.1+abc")!.CompareTo(UpdateVersion.Parse("v0.3.1")));

    // GitHub

    private static string ReleaseJson(string tag = "v0.4.0", bool draft = false, string? extraAssetUrl = null) => $$"""
        {
          "tag_name": "{{tag}}", "draft": {{(draft ? "true" : "false")}}, "prerelease": false,
          "html_url": "https://github.com/{{Repo}}/releases/tag/{{tag}}",
          "assets": [
            { "name": "GBFileCopier-0.4.0-macos.dmg", "size": 10,
              "browser_download_url": "https://github.com/{{Repo}}/releases/download/{{tag}}/GBFileCopier-0.4.0-macos.dmg" },
            { "name": "SHA256SUMS.txt", "size": 1,
              "browser_download_url": "https://github.com/{{Repo}}/releases/download/{{tag}}/SHA256SUMS.txt" },
            { "name": "evil.exe", "size": 1, "browser_download_url": "{{extraAssetUrl ?? "https://example.com/evil.exe"}}" }
          ]
        }
        """;

    [Fact]
    public async Task Releases_ReadsTheLatestRelease()
    {
        var handler = new FakeHttp { [$"https://api.github.com/repos/{Repo}/releases/latest"] = Ok(ReleaseJson()) };
        using var http = new HttpClient(handler);

        var release = await new GitHubReleases(http).GetLatestAsync(Token);

        Assert.NotNull(release);
        Assert.Equal("0.4.0", release.Version.ToString());
        Assert.Equal(new Uri($"https://github.com/{Repo}/releases/tag/v0.4.0"), release.Page);
        Assert.NotNull(release.Find("GBFileCopier-0.4.0-macos.dmg"));
        Assert.Null(release.Find("evil.exe")); // files outside this repository's releases are ignored
    }

    [Fact]
    public async Task Releases_NoReleaseOrDraft_IsNull()
    {
        using var none = new HttpClient(new FakeHttp());
        Assert.Null(await new GitHubReleases(none).GetLatestAsync(Token));

        using var draft = new HttpClient(new FakeHttp { [$"https://api.github.com/repos/{Repo}/releases/latest"] = Ok(ReleaseJson(draft: true)) });
        Assert.Null(await new GitHubReleases(draft).GetLatestAsync(Token));
    }

    // Download

    private static ReleaseInfo Release(params (string Name, string Url)[] assets) =>
        new(UpdateVersion.Parse("0.4.0")!, new Uri("https://github.com/x"), [.. assets.Select(a => new ReleaseAsset(a.Name, new Uri(a.Url), 0))]);

    [Fact]
    public async Task Download_ChecksTheChecksum()
    {
        var content = Encoding.UTF8.GetBytes("the new version");
        var sum = Convert.ToHexStringLower(SHA256.HashData(content));
        var handler = new FakeHttp
        {
            ["https://dl/SHA256SUMS.txt"] = Ok($"{new string('0', 64)}  other.zip\n{sum}  app.dmg\n"),
            ["https://dl/app.dmg"] = Ok(content),
        };
        using var http = new HttpClient(handler);
        var release = Release(("SHA256SUMS.txt", "https://dl/SHA256SUMS.txt"), ("app.dmg", "https://dl/app.dmg"));
        var progress = new List<double>();

        var path = await new UpdateDownloader(http).DownloadAsync(release, release.Find("app.dmg")!, _temp.Combine("dl"), new SyncProgress(progress.Add), Token);

        Assert.Equal(content, await File.ReadAllBytesAsync(path, Token));
        Assert.Equal(1, progress[^1]);
    }

    [Fact]
    public async Task Download_WrongChecksum_DeletesTheFile()
    {
        var handler = new FakeHttp
        {
            ["https://dl/SHA256SUMS.txt"] = Ok($"{new string('a', 64)}  app.dmg\n"),
            ["https://dl/app.dmg"] = Ok("tampered"u8.ToArray()),
        };
        using var http = new HttpClient(handler);
        var release = Release(("SHA256SUMS.txt", "https://dl/SHA256SUMS.txt"), ("app.dmg", "https://dl/app.dmg"));

        await Assert.ThrowsAsync<UpdateException>(() =>
            new UpdateDownloader(http).DownloadAsync(release, release.Find("app.dmg")!, _temp.Combine("dl"), null, Token));

        Assert.False(File.Exists(_temp.Combine("dl", "app.dmg")));
    }

    [Fact]
    public async Task Download_WithoutChecksums_IsRefused()
    {
        using var http = new HttpClient(new FakeHttp());
        var release = Release(("app.dmg", "https://dl/app.dmg"));

        await Assert.ThrowsAsync<UpdateException>(() =>
            new UpdateDownloader(http).DownloadAsync(release, release.Find("app.dmg")!, _temp.Combine("dl"), null, Token));
    }

    [Fact]
    public void Checksums_ParseSha256sumOutput()
    {
        var sums = UpdateDownloader.ParseChecksums($"{new string('a', 64)}  GBFileCopier-0.4.0-macos.dmg\r\n{new string('b', 64)} *gbcopy.tar.gz\n\nnot a line\n");

        Assert.Equal(new string('a', 64), sums["GBFileCopier-0.4.0-macos.dmg"]);
        Assert.Equal(new string('b', 64), sums["gbcopy.tar.gz"]);
        Assert.Equal(2, sums.Count);
    }

    // Installers

    [Fact]
    public void Windows_SetupRunsSilentlyAndRestartsTheApp()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var folder = _temp.Combine("app");
        Directory.CreateDirectory(folder);
        var installer = new WindowsUpdateInstaller(folder);
        Assert.False(installer.CanInstall); // portable: no uninstaller next to the app
        File.WriteAllText(Path.Combine(folder, "unins000.exe"), string.Empty);
        Assert.True(installer.CanInstall);

        Assert.Equal("GBFileCopier-0.4.0-windows-x64-setup.exe", installer.PackageName(UpdateVersion.Parse("0.4.0")!));
        // No /CURRENTUSER or /ALLUSERS: setup keeps the previous install mode (UsePreviousPrivileges)
        Assert.Equal("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS /RELAUNCH=1", WindowsUpdateInstaller.SetupArguments);
    }

    [Fact]
    public void Mac_FindsTheBundleAndNamesThePackage()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "macOS only");
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        Assert.Equal("/Applications/GBFileCopier.app", MacUpdateInstaller.FindBundle("/Applications/GBFileCopier.app/Contents/MacOS/GBFileCopier"));
        Assert.Null(MacUpdateInstaller.FindBundle("/usr/local/bin/GBFileCopier"));
        Assert.Equal("GBFileCopier-0.4.0-macos.dmg", new MacUpdateInstaller("/x/GBFileCopier.app").PackageName(UpdateVersion.Parse("0.4.0")!));
        Assert.False(new MacUpdateInstaller("/System/Applications/GBFileCopier.app").CanInstall); // read-only system volume
    }

    [Fact]
    public async Task Mac_InstallScript_SwapsTheAppAndKeepsTheOldOneIfTheCopyFails()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "macOS only");
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var target = _temp.CreateFile("Applications/GBFileCopier.app/Contents/version", text: "old");
        var newApp = _temp.CreateFile("work/GBFileCopier.app/Contents/version", text: "new");
        var script = _temp.CreateFile("work/install.sh", text: ScriptForTests);

        await RunScript(script, _temp.Combine("Applications", "GBFileCopier.app"), _temp.Combine("work", "GBFileCopier.app"), _temp.Combine("work"));

        Assert.Equal("new", File.ReadAllText(target));
        Assert.False(Directory.Exists(_temp.Combine("work")));
        Assert.False(Directory.Exists(_temp.Combine("Applications", "GBFileCopier.app.gbcopy-old")));

        // A missing new app: the installed one stays
        var script2 = _temp.CreateFile("work2/install.sh", text: ScriptForTests);
        await RunScript(script2, _temp.Combine("Applications", "GBFileCopier.app"), _temp.Combine("work2", "missing.app"), _temp.Combine("work2"));
        Assert.Equal("new", File.ReadAllText(target));
        _ = newApp;
    }

    /// <summary>The install script without its effects outside the test folder (open, the Finder extension).</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    private static string ScriptForTests => MacUpdateInstaller.InstallScript
        .Replace("/usr/bin/open", "true", StringComparison.Ordinal)
        .Replace("/usr/bin/pluginkit", "true", StringComparison.Ordinal)
        .Replace("/usr/bin/pkill", "true", StringComparison.Ordinal);

    [AvaloniaFact]
    public async Task Install_NotCompleted_StaysOpenAndDownloadsAgainNextTime()
    {
        var h = new Harness();
        h.Service.InstallError = new UpdateException("the installation did not complete (code 5)");
        var bar = h.Create();
        await bar.CheckNowAsync();

        await bar.InstallCommand.ExecuteAsync(null);

        Assert.False(h.Quit);
        Assert.Equal(UpdateState.Failed, bar.State);
        Assert.Contains("code 5", bar.Message, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Install_OnWindows_SetupClosesTheApp()
    {
        var h = new Harness();
        h.Service.QuitsTheApp = false;
        var bar = h.Create();
        await bar.CheckNowAsync();

        await bar.InstallCommand.ExecuteAsync(null);

        Assert.False(h.Quit); // setup closes it through the Restart Manager
        Assert.Equal(UpdateState.Installing, bar.State);
    }

    private static async Task RunScript(string script, string target, string newApp, string work)
    {
        var start = new System.Diagnostics.ProcessStartInfo("/bin/sh") { UseShellExecute = false };
        foreach (var argument in new[] { script, "999999", target, newApp, work }) // a process id that does not exist
        {
            start.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(start)!;
        await process.WaitForExitAsync(Token);
    }

    // The update bar

    private sealed class FakeUpdateService : IUpdateService
    {
        public UpdateVersion CurrentVersion { get; set; } = UpdateVersion.Parse("0.3.1")!;

        public bool CanInstall { get; set; } = true;

        public ReleaseInfo? Latest { get; set; } = Release(("x", "https://dl/x"));

        public Exception? CheckError { get; set; }

        public int Checks { get; private set; }

        public PreparedUpdate? Installed { get; private set; }

        public Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken)
        {
            Checks++;
            return CheckError is null ? Task.FromResult(Latest) : Task.FromException<ReleaseInfo?>(CheckError);
        }

        public Task<PreparedUpdate> DownloadAsync(ReleaseInfo release, IProgress<double> progress, CancellationToken cancellationToken)
        {
            progress.Report(1);
            return Task.FromResult(new PreparedUpdate(release.Version, "/tmp/setup"));
        }

        public bool QuitsTheApp { get; set; } = true;

        public Exception? InstallError { get; set; }

        public Task<bool> InstallAsync(PreparedUpdate update)
        {
            Installed = update;
            return InstallError is null ? Task.FromResult(QuitsTheApp) : Task.FromException<bool>(InstallError);
        }
    }

    private sealed class Harness
    {
        public FakeUpdateService Service { get; } = new();

        public FakeSettingsStore Settings { get; } = new();

        public bool JobsRunning { get; set; }

        public bool Quit { get; private set; }

        public List<Uri> Opened { get; } = [];

        public DateTime Now { get; set; } = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);

        public UpdateViewModel Create() => new(Service, Settings, () => !JobsRunning, () => Quit = true, Opened.Add, () => Now);
    }

    [AvaloniaFact]
    public async Task FirstStart_AsksBeforeContactingGitHub()
    {
        var h = new Harness();
        var bar = h.Create();

        await bar.StartAsync();

        Assert.Equal(UpdateState.AskAutomatic, bar.State);
        Assert.Equal(0, h.Service.Checks);

        bar.DisableAutomaticCommand.Execute(null);
        Assert.False(h.Settings.Load().CheckForUpdates);
        Assert.False(bar.IsVisible);
        await bar.TickAsync();
        Assert.Equal(0, h.Service.Checks); // never, until the user changes their mind
    }

    [AvaloniaFact]
    public async Task Accepting_ChecksAndShowsTheNewVersion()
    {
        var h = new Harness();
        var bar = h.Create();
        await bar.StartAsync();

        await bar.EnableAutomaticCommand.ExecuteAsync(null);

        Assert.True(h.Settings.Load().CheckForUpdates);
        Assert.Equal(UpdateState.Available, bar.State);
        Assert.Equal("GBFileCopier 0.4.0 is available (you have 0.3.1)", bar.Message);
        Assert.Equal("Install and restart", bar.InstallLabel);
    }

    [AvaloniaFact]
    public async Task AutomaticChecks_OncePerDay_AndSilentWhenNothingNew()
    {
        var h = new Harness();
        h.Settings.Save(new() { CheckForUpdates = true });
        h.Service.Latest = null;
        var bar = h.Create();

        await bar.StartAsync();
        await bar.TickAsync();
        Assert.Equal(1, h.Service.Checks);
        Assert.False(bar.IsVisible);

        h.Now += TimeSpan.FromHours(25);
        await bar.TickAsync();
        Assert.Equal(2, h.Service.Checks);
    }

    [AvaloniaFact]
    public async Task AutomaticCheck_Offline_StaysQuiet_ManualCheck_Explains()
    {
        var h = new Harness();
        h.Settings.Save(new() { CheckForUpdates = true });
        h.Service.CheckError = new HttpRequestException("No route to host");
        var bar = h.Create();

        await bar.StartAsync();
        Assert.False(bar.IsVisible);

        await bar.CheckNowAsync();
        Assert.Equal(UpdateState.Failed, bar.State);
        Assert.Contains("No route to host", bar.Message, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ManualCheck_UpToDate_SaysSo()
    {
        var h = new Harness();
        h.Service.CurrentVersion = UpdateVersion.Parse("0.4.0")!;
        var bar = h.Create();

        await bar.CheckNowAsync();

        Assert.Equal(UpdateState.UpToDate, bar.State);
        Assert.Equal("GBFileCopier 0.4.0 is the latest version", bar.Message);
        Assert.True(bar.CanClose);
    }

    [AvaloniaFact]
    public async Task Install_DownloadsInstallsAndQuits()
    {
        var h = new Harness();
        var bar = h.Create();
        await bar.CheckNowAsync();

        await bar.InstallCommand.ExecuteAsync(null);

        Assert.Equal("0.4.0", h.Service.Installed?.Version.ToString());
        Assert.True(h.Quit);
        Assert.Equal(UpdateState.Installing, bar.State);
    }

    [AvaloniaFact]
    public async Task Install_WaitsForTheJobs()
    {
        var h = new Harness { JobsRunning = true };
        var bar = h.Create();
        await bar.CheckNowAsync();

        Assert.False(bar.InstallCommand.CanExecute(null));
        Assert.True(bar.WaitsForJobs);

        h.JobsRunning = false;
        bar.NotifyJobsChanged();
        Assert.True(bar.InstallCommand.CanExecute(null));
        Assert.False(bar.WaitsForJobs);
    }

    [AvaloniaFact]
    public async Task PortableCopy_OpensTheReleasePageInstead()
    {
        var h = new Harness();
        h.Service.CanInstall = false;
        var bar = h.Create();
        await bar.CheckNowAsync();

        Assert.Equal("Download…", bar.InstallLabel);
        await bar.InstallCommand.ExecuteAsync(null);

        Assert.Equal(new Uri("https://github.com/x"), Assert.Single(h.Opened));
        Assert.Null(h.Service.Installed);
        Assert.False(h.Quit);
    }

    // Helpers

    private static HttpResponseMessage Ok(string text) => Ok(Encoding.UTF8.GetBytes(text));

    private static HttpResponseMessage Ok(byte[] content) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };

    /// <summary>Answers by URL; anything else is 404.</summary>
    private sealed class FakeHttp : HttpMessageHandler, IEnumerable<KeyValuePair<string, HttpResponseMessage>>
    {
        private readonly Dictionary<string, HttpResponseMessage> _responses = [];

        public HttpResponseMessage this[string url]
        {
            set => _responses[url] = value;
        }

        public IEnumerator<KeyValuePair<string, HttpResponseMessage>> GetEnumerator() => _responses.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responses.TryGetValue(request.RequestUri!.ToString(), out var response) ? response : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    /// <summary>Reports at once, unlike Progress&lt;T&gt; which posts to the synchronization context.</summary>
    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
