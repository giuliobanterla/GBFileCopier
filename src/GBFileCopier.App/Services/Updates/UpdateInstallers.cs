using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
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
    /// <exception cref="UpdateException">The package must not be installed.</exception>
    Task<PreparedUpdate> PrepareAsync(string packagePath, UpdateVersion version, CancellationToken cancellationToken);

    /// <summary>
    /// Checks the prepared update again (it may have waited for a while in a temporary folder) and
    /// starts the installation.
    /// </summary>
    /// <returns>True if the app must quit now to be replaced.</returns>
    /// <exception cref="UpdateException">The update is gone or changed, or the installation did not complete.</exception>
    Task<bool> LaunchAsync(PreparedUpdate update, CancellationToken cancellationToken);

    /// <summary>The installer for this system; null where updates are not installed automatically.</summary>
    public static IUpdateInstaller? ForThisSystem() =>
        OperatingSystem.IsWindows() ? new WindowsUpdateInstaller()
        : OperatingSystem.IsMacOS() ? new MacUpdateInstaller()
        : null;
}

/// <summary>
/// Windows: runs the new setup silently over the installed app. Setup keeps the previous install
/// mode (this user, or all users after asking for administrator rights), closes the app through the
/// Restart Manager and restarts it (/RELAUNCH=1, see GBFileCopier.iss). The app does not quit by
/// itself: if setup does not run (administrator rights refused, for example), it stays open.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsUpdateInstaller(string? appFolder = null, string? appPath = null) : IUpdateInstaller
{
    /// <summary>Silent; force-closes the app if needed (no job is running: the app checked).</summary>
    public const string SetupArguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS /RELAUNCH=1";

    private readonly string _appFolder = appFolder ?? AppContext.BaseDirectory;
    private readonly string? _appPath = appPath ?? Environment.ProcessPath;

    public string PackageName(UpdateVersion version) =>
        string.Create(CultureInfo.InvariantCulture, $"GBFileCopier-{version}-windows-x64-setup.exe");

    /// <summary>Inno Setup leaves its uninstaller next to the app; the portable zip has none.</summary>
    public bool CanInstall => File.Exists(Path.Combine(_appFolder, "unins000.exe"));

    public async Task<PreparedUpdate> PrepareAsync(string packagePath, UpdateVersion version, CancellationToken cancellationToken)
    {
        CheckPublisher(packagePath);
        await using var stream = File.OpenRead(packagePath);
        return new PreparedUpdate(version, packagePath, Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken)));
    }

    public async Task<bool> LaunchAsync(PreparedUpdate update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        Process? setup;
        // Held open (others can only read) from the check until setup has started: it cannot be swapped
        await using (var held = OpenUnchanged(update))
        {
            CheckPublisher(update.Path);
            // Shell execute: for an install for all users, setup asks for administrator rights (UAC)
            setup = Process.Start(new ProcessStartInfo(update.Path, SetupArguments) { UseShellExecute = true });
        }

        using (setup)
        {
            if (setup is not null)
            {
                await setup.WaitForExitAsync(cancellationToken);
            }

            // Setup closes this app before replacing it: still here means it did not install
            throw new UpdateException(string.Format(CultureInfo.CurrentCulture, Strings.UpdateNotCompleted, setup?.ExitCode ?? -1));
        }
    }

    private static FileStream OpenUnchanged(PreparedUpdate update)
    {
        FileStream stream;
        try
        {
            stream = new FileStream(update.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UpdateException(Strings.UpdateFailedGeneric, ex);
        }

        if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(stream)), update.Sha256, StringComparison.Ordinal))
        {
            stream.Dispose();
            throw new UpdateException(Strings.UpdateChecksumMismatch);
        }

        return stream;
    }

    // Once the releases are signed, a new version must come from the same publisher as this one.
    // Unsigned releases (so far) rely on HTTPS from GitHub and the checksum.
    private void CheckPublisher(string packagePath)
    {
        if (_appPath is not null && WindowsAuthenticode.TrustedSigner(_appPath) is { } publisher
            && !string.Equals(WindowsAuthenticode.TrustedSigner(packagePath), publisher, StringComparison.Ordinal))
        {
            throw new UpdateException(Strings.UpdateNotSigned);
        }
    }
}

/// <summary>
/// macOS: takes the app out of the disk image, checks that it and everything inside it is signed
/// with the project's Developer ID and notarized, then a small script replaces this .app once the
/// app has quit, and opens the new one.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacUpdateInstaller(string? bundlePath = null) : IUpdateInstaller
{
    /// <summary>The Developer ID team that signs the releases (see packaging/macos/build.sh).</summary>
    public const string TeamId = "D3694MQ8D9";

    /// <summary>Code signing requirement: a Developer ID certificate of the project's team.</summary>
    public const string Requirement = $"=anchor apple generic and certificate leaf[subject.OU] = \"{TeamId}\"";

    private const string AppName = "GBFileCopier.app";

    /// <summary>The running .app, from …/GBFileCopier.app/Contents/MacOS/GBFileCopier; null if not in a bundle.</summary>
    public string? BundlePath { get; } = bundlePath ?? FindBundle(Environment.ProcessPath);

    public string PackageName(UpdateVersion version) =>
        string.Create(CultureInfo.InvariantCulture, $"GBFileCopier-{version}-macos.dmg");

    /// <summary>In a bundle, and its folder (usually /Applications) can be written: the .app can be swapped.</summary>
    public bool CanInstall => BundlePath is { } bundle && Path.GetDirectoryName(bundle) is { } folder && IsWritable(folder);

    public async Task<PreparedUpdate> PrepareAsync(string packagePath, UpdateVersion version, CancellationToken cancellationToken)
    {
        var work = Path.GetDirectoryName(packagePath)!;
        var mountPoint = Path.Combine(work, "mount");
        var app = Path.Combine(work, AppName);
        if (Directory.Exists(app))
        {
            Directory.Delete(app, recursive: true);
        }

        Directory.CreateDirectory(mountPoint);
        try
        {
            await RunAsync("/usr/bin/hdiutil", ["attach", packagePath, "-nobrowse", "-readonly", "-noautoopen", "-mountpoint", mountPoint], cancellationToken);
            await RunAsync("/usr/bin/ditto", [Path.Combine(mountPoint, AppName), app], cancellationToken);
        }
        finally
        {
            // Also after a cancellation or a failed attach: never leave the image mounted
            await RunAsync("/usr/bin/hdiutil", ["detach", mountPoint, "-force"], CancellationToken.None, check: false);
        }

        await CheckSignatureAsync(app, cancellationToken);
        var assessed = await RunAsync("/usr/sbin/spctl", ["--assess", "--type", "execute", app], cancellationToken, check: false);
        if (assessed.ExitCode != 0)
        {
            throw new UpdateException(Strings.UpdateNotSigned); // not notarized: Gatekeeper would refuse it
        }

        var plist = await RunAsync("/usr/libexec/PlistBuddy", ["-c", "Print :CFBundleShortVersionString", Path.Combine(app, "Contents", "Info.plist")], cancellationToken);
        if (UpdateVersion.Parse(plist.Output) is not { } found || found.Number != version.Number)
        {
            throw new UpdateException(Strings.UpdateWrongVersion);
        }

        return new PreparedUpdate(version, app);
    }

    public async Task<bool> LaunchAsync(PreparedUpdate update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        var bundle = BundlePath ?? throw new UpdateException(Strings.UpdateFailedGeneric);
        if (!Directory.Exists(update.Path))
        {
            throw new UpdateException(Strings.UpdateFailedGeneric); // removed from the temporary folder meanwhile
        }

        await CheckSignatureAsync(update.Path, cancellationToken);
        var work = Path.GetDirectoryName(update.Path)!;
        var script = Path.Combine(work, "install.sh");
        await File.WriteAllTextAsync(script, InstallScript, cancellationToken);
        var start = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
        foreach (var argument in new[] { script, Environment.ProcessId.ToString(CultureInfo.InvariantCulture), bundle, update.Path, work })
        {
            start.ArgumentList.Add(argument);
        }

        Process.Start(start)?.Dispose(); // keeps running after the app quits
        return true;
    }

    /// <summary>
    /// Waits for the app to quit, copies the new .app next to the old one, swaps them (putting the
    /// old one back if that fails), points the Finder at the new extension, opens the result and
    /// removes the downloads.
    /// </summary>
    internal const string InstallScript = """
        #!/bin/sh
        pid="$1"; target="$2"; new="$3"; work="$4"
        while kill -0 "$pid" 2>/dev/null; do sleep 0.2; done
        staged="$target.gbcopy-update"; old="$target.gbcopy-old"
        rm -rf "$staged" "$old"
        if /usr/bin/ditto "$new" "$staged" && mv "$target" "$old"; then
          if mv "$staged" "$target"; then
            rm -rf "$old"
            /usr/bin/pluginkit -a "$target/Contents/PlugIns/GBFileCopierFinder.appex" 2>/dev/null
            /usr/bin/pkill -x GBFileCopierFinder 2>/dev/null
          else
            mv "$old" "$target"
          fi
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

    /// <summary>The app and all the code inside it (libraries, Finder extension) intact and signed by the team.</summary>
    private static async Task CheckSignatureAsync(string app, CancellationToken cancellationToken)
    {
        var verified = await RunAsync("/usr/bin/codesign", ["--verify", "--deep", "--strict", "-R", Requirement, app], cancellationToken, check: false);
        if (verified.ExitCode != 0)
        {
            throw new UpdateException(Strings.UpdateNotSigned);
        }
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
        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        var text = (await output) + (await error);
        if (check && process.ExitCode != 0)
        {
            throw new UpdateException(Strings.UpdateFailedGeneric, new IOException($"{Path.GetFileName(tool)}: {text.Trim()}"));
        }

        return (process.ExitCode, text);
    }
}
