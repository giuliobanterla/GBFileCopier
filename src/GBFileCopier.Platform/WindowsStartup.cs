using System.Runtime.Versioning;
using Microsoft.Win32;

namespace GBFileCopier.Platform;

/// <summary>Starting the app when the user signs in to Windows (the current user's Run key).</summary>
[SupportedOSPlatform("windows")]
public static class WindowsStartup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "GBFileCopier";

    /// <summary>The command line Windows runs at sign-in, or null if the app does not start then.</summary>
    public static string? Command
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) as string;
        }
    }

    /// <summary>Starts <paramref name="executable"/> with <paramref name="argument"/> at sign-in, or stops doing it.</summary>
    public static void Set(bool enabled, string executable, string argument)
    {
        ArgumentException.ThrowIfNullOrEmpty(executable);
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{executable}\" {argument}");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
