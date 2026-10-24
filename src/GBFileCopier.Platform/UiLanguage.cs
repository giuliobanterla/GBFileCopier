using System.Globalization;
using System.Runtime.InteropServices;

namespace GBFileCopier.Platform;

/// <summary>The language the user wants the interface in.</summary>
public static partial class UiLanguage
{
    /// <summary>Environment variable that forces a language, for example "it" or "en".</summary>
    public const string OverrideVariable = "GBFILECOPIER_LANG";

    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint kCFStringEncodingUTF8 = 0x08000100;

    /// <summary>
    /// The UI culture: the override variable, then the language chosen in the settings, then the
    /// system one. On macOS .NET derives CurrentUICulture from the region format
    /// (English with Italian region gives it-IT), so the first preferred language of the system
    /// settings is read instead. Elsewhere CurrentUICulture already is the UI language.
    /// </summary>
    /// <param name="configured">Language chosen in the app settings; empty to follow the system.</param>
    public static CultureInfo GetPreferred(string? configured = null)
    {
        var name = Environment.GetEnvironmentVariable(OverrideVariable);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = configured;
        }

        if (string.IsNullOrWhiteSpace(name) && OperatingSystem.IsMacOS())
        {
            name = GetMacPreferredLanguage();
        }

        try
        {
            return string.IsNullOrWhiteSpace(name) ? CultureInfo.CurrentUICulture : CultureInfo.GetCultureInfo(name);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.CurrentUICulture;
        }
    }

    /// <summary>Sets the language of every thread, the copy threads included.</summary>
    public static void Apply(CultureInfo culture)
    {
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    private static string? GetMacPreferredLanguage()
    {
        var languages = CFLocaleCopyPreferredLanguages();
        if (languages == 0)
        {
            return null;
        }

        try
        {
            if (CFArrayGetCount(languages) == 0)
            {
                return null;
            }

            var buffer = new byte[64];
            return CFStringGetCString(CFArrayGetValueAtIndex(languages, 0), buffer, buffer.Length, kCFStringEncodingUTF8) != 0
                ? System.Text.Encoding.UTF8.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0))
                : null;
        }
        finally
        {
            CFRelease(languages);
        }
    }

    [LibraryImport(CoreFoundation)]
    private static partial nint CFLocaleCopyPreferredLanguages();

    [LibraryImport(CoreFoundation)]
    private static partial nint CFArrayGetCount(nint array);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFArrayGetValueAtIndex(nint array, nint index);

    [LibraryImport(CoreFoundation)]
    private static partial byte CFStringGetCString(nint text, [Out] byte[] buffer, nint bufferSize, uint encoding);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(nint value);
}
