namespace GBFileCopier.Platform;

/// <summary>Detects the operating system the app is running on.</summary>
public static class PlatformInfo
{
    public static bool IsWindows => OperatingSystem.IsWindows();

    public static bool IsMacOS => OperatingSystem.IsMacOS();

    public static string Description =>
        $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription} " +
        $"({System.Runtime.InteropServices.RuntimeInformation.OSArchitecture})";
}
