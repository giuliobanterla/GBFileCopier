using System.Reflection;

namespace GBFileCopier.Core;

/// <summary>Product name and version, shared by the CLI and the UI.</summary>
public static class AppInfo
{
    public const string Name = "GBFileCopier";

    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";
}
