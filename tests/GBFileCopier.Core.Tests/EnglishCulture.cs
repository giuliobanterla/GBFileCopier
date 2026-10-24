using System.Globalization;
using System.Runtime.CompilerServices;

namespace GBFileCopier.Tests;

/// <summary>Tests check English texts: run them in English whatever the machine's language.</summary>
internal static class EnglishCulture
{
    [ModuleInitializer]
    internal static void Apply()
    {
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
    }

    /// <summary>Switches the UI language for the duration of a test.</summary>
    internal static IDisposable Use(string name)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
        return new Restore(previous);
    }

    private sealed class Restore(CultureInfo previous) : IDisposable
    {
        public void Dispose() => CultureInfo.CurrentUICulture = previous;
    }
}
