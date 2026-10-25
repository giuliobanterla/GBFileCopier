using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using GBFileCopier.App.Resources;
using GBFileCopier.Platform;

namespace GBFileCopier.App.Services.Updates;

/// <summary>Replaces the running app with a downloaded release, the way each system needs.</summary>
public interface IUpdateInstaller
{
    /// <summary>The release file for this system, for example GBFileCopier-0.4.0-macos.dmg.</summary>
    string PackageName(UpdateVersion version);

    /// <summary>
    /// This copy of the app can be replaced: installed by the installer on Windows, a writable .app
    /// on macOS. Otherwise (portable zip, the disk image itself, a build) the user downloads by hand.
    /// </summary>
    bool CanInstall { get; }

    /// <summary>Checks the downloaded package beyond its checksum (who signed it) and gets it ready.</summary>
    /// <returns>What <see cref="Launch"/> installs.</returns>
    /// <exception cref="UpdateException">The package must not be installed.</exception>
    Task<string> PrepareAsync(string packagePath, UpdateVersion version, CancellationToken cancellationToken);

    /// <summary>Starts the replacement; the app must quit right after, so that its files can be replaced.</summary>
    void Launch(string preparedPath);

    /// <summary>The installer for this system; null where updates are not installed automatically.</summary>
    public static IUpdateInstaller? ForThisSystem() =>
        OperatingSystem.IsWindows() ? new WindowsUpdateInstaller()
        : OperatingSystem.IsMacOS() ? new MacUpdateInstaller()
        : null;
}

/// <summary>
/// Windows: runs the new setup silently over the installed app (same scope: this user or all
/// users); setup closes and restarts GBFileCopier (/RELAUNCH=1, see GBFileCopier.iss).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsUpdateInstaller(string? appFolder = null, string? appPath = null) : IUpdateInstaller
{
    private readonly string _appFolder = appFolder ?? AppContext.BaseDirectory;
    private readonly string? _appPath = appPath ?? Environment.ProcessPath;

    public string PackageName(UpdateVersion version) =>
        string.Create(CultureInfo.InvariantCulture, $"GBFileCopier-{version}-windows-x64-setup.exe");

    /// <summary>Inno Setup leaves its uninstaller next to the app; the portable zip has none.</summary>
    public bool CanInstall => File.Exists(Path.Combine(_appFolder, "unins000.exe"));

    /// <summary>Installed under Program Files: an install for all users, which needs administrator rights.</summary>
    public bool IsInstalledForAllUsers =>
        IsUnder(_appFolder, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles))
        || IsUnder(_appFolder, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));

    public Task<string> PrepareAsync(string packagePath, UpdateVersion version, CancellationToken cancellationToken)
    {
        // Once the releases are signed, a new version must come from the same publisher as this one.
        // Unsigned releases (so far) rely on HTTPS from GitHub and the checksum.
        if (_appPath is not null && WindowsAuthenticode.TrustedSigner(_appPath) is { } publisher
            && !string.Equals(WindowsAuthenticode.TrustedSigner(packagePath), publisher, StringComparison.Ordinal))
        {
            throw new UpdateException(Strings.UpdateNotSigned);
        }

        return Task.FromResult(packagePath);
    }

    /// <summary>The setup options: silent, the same scope as the installed app, restart afterwards.</summary>
    public IReadOnlyList<string> SetupArguments() =>
        ["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS", IsInstalledForAllUsers ? "/ALLUSERS" : "/CURRENTUSER", "/RELAUNCH=1"];

    public void Launch(string preparedPath)
    {
        // Shell execute: an install for all users asks for administrator rights (UAC)
        var start = new ProcessStartInfo(preparedPath) { UseShellExecute = true };
        foreach (var argument in SetupArguments())
        {
            start.ArgumentList.Add(argument);
        }

        Process.Start(start)?.Dispose();
    }

    private static bool IsUnder(string path, string folder) =>
        folder.Length > 0 && Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// macOS: takes the app out of the disk image, checks that it is signed with the project's
/// Developer ID and notarized, then a small script replaces this .app once the app has quit, and
/// opens the new one.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacUpdateInstaller(string? bundlePath = null) : IUpdateInstaller
{
    /// <summary>The Developer ID team that signs the releases (see packaging/macos/build.sh).</summary>
    public const string TeamId = "D3694MQ8D9";

    private const string AppName = "GBFileCopier.app";

    /// <summary>The running .app, from …/GBFileCopier.app/Contents/MacOS/GBFileCopier; null if not in a bundle.</summary>
    public string? BundlePath { get; } = bundlePath ?? FindBundle(Environment.ProcessPath);

    public string PackageName(UpdateVersion version) =>
        string.Create(CultureInfo.InvariantCulture, $"GBFileCopier-{version}-macos.dmg");

    /// <summary>In a bundle, and its folder (usually /Applications) can be written: the .app can be swapped.</summary>
    public bool CanInstall => BundlePath is { } bundle && Path.GetDirectoryName(bundle) is { } folder && IsWritable(folder);

    public async Task<string> PrepareAsync(string packagePath, UpdateVersion version, CancellationToken cancellationToken)
    {
        var work = Path.GetDirectoryName(packagePath)!;
        var mountPoint = Path.Combine(work, "mount");
        var app = Path.Combine(work, AppName);
        if (Directory.Exists(app))
        {
            Directory.Delete(app, recursive: true);
        }

        Directory.CreateDirectory(mountPoint);
        await RunAsync("/usr/bin/hdiutil", ["attach", packagePath, "-nobrowse", "-readonly", "-noautoopen", "-mountpoint", mountPoint], cancellationToken);
        try
        {
            await RunAsync("/usr/bin/ditto", [Path.Combine(mountPoint, AppName), app], cancellationToken);
        }
        finally
        {
            await RunAsync("/usr/bin/hdiutil", ["detach", mountPoint, "-force"], CancellationToken.None, check: false);
        }

        // Signed by the project's team, intact, and accepted by Gatekeeper (notarized)
        var signature = await RunAsync("/usr/bin/codesign", ["-dv", "--verbose=2", app], cancellationToken, check: false);
        var verified = await RunAsync("/usr/bin/codesign", ["--verify", "--strict", app], cancellationToken, check: false);
        var assessed = await RunAsync("/usr/sbin/spctl", ["--assess", "--type", "execute", "-vv", app], cancellationToken, check: false);
        if (verified.ExitCode != 0 || assessed.ExitCode != 0 || !signature.Output.Contains("TeamIdentifier=" + TeamId, StringComparison.Ordinal))
        {
            throw new UpdateException(Strings.UpdateNotSigned);
        }

        var plist = await RunAsync("/usr/libexec/PlistBuddy", ["-c", "Print :CFBundleShortVersionString", Path.Combine(app, "Contents", "Info.plist")], cancellationToken);
        if (UpdateVersion.Parse(plist.Output) is not { } found || found.Number != version.Number)
        {
            throw new UpdateException(Strings.UpdateWrongVersion);
        }

        return app;
    }

    public void Launch(string preparedPath)
    {
        var bundle = BundlePath ?? throw new InvalidOperationException("Not running from an app bundle");
        var work = Path.GetDirectoryName(preparedPath)!;
        var script = Path.Combine(work, "install.sh");
        File.WriteAllText(script, InstallScript);
        var start = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
        foreach (var argument in new[] { script, Environment.ProcessId.ToString(CultureInfo.InvariantCulture), bundle, preparedPath, work })
        {
            start.ArgumentList.Add(argument);
        }

        Process.Start(start)?.Dispose(); // keeps running after the app quits
    }

    /// <summary>
    /// Waits for the app to quit, copies the new .app next to the old one, swaps them (putting the
    /// old one back if that fails), opens the result and removes the downloads.
    /// </summary>
    internal const string InstallScript = """
        #!/bin/sh
        pid="$1"; target="$2"; new="$3"; work="$4"
        while kill -0 "$pid" 2>/dev/null; do sleep 0.2; done
        staged="$target.gbcopy-update"; old="$target.gbcopy-old"
        rm -rf "$staged" "$old"
        if /usr/bin/ditto "$new" "$staged" && mv "$target" "$old"; then
          if mv "$staged" "$target"; then rm -rf "$old"; else mv "$old" "$target"; fi
        fi
        rm -rf "$staged" "$work"
        /usr/bin/open "$target"
        """;

    internal static string? FindBundle(string? executable)
    {
        // …/GBFileCopier.app/Contents/MacOS/GBFileCopier
        var macOS = Path.GetDirectoryName(executable);
        var contents = Path.GetDirectoryName(macOS);
        var bundle = Path.GetDirectoryName(contents);
        return bundle is not null && bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(contents) == "Contents" && Path.GetFileName(macOS) == "MacOS" ? bundle : null;
    }

    private static bool IsWritable(string folder)
    {
        try
        {
            var probe = Path.Combine(folder, $".gbcopy-write-test-{Environment.ProcessId}");
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string tool, IEnumerable<string> arguments, CancellationToken cancellationToken, bool check = true)
    {
        var start = new ProcessStartInfo(tool) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new UpdateException(Strings.UpdateFailedGeneric);
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var text = (await output) + (await error);
        if (check && process.ExitCode != 0)
        {
            throw new UpdateException(Strings.UpdateFailedGeneric, new IOException($"{Path.GetFileName(tool)}: {text.Trim()}"));
        }

        return (process.ExitCode, text);
    }
}
