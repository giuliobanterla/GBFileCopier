using System.Globalization;

namespace GBFileCopier.App.Services;

/// <summary>
/// Optional log of what the app receives from the system (links, files dropped on the Dock).
/// Off unless GBFILECOPIER_LOG names a file: open --env GBFILECOPIER_LOG=/tmp/gbfc.log -a GBFileCopier
/// </summary>
internal static class DiagnosticLog
{
    private static readonly string? FilePath = Environment.GetEnvironmentVariable("GBFILECOPIER_LOG");

    public static void Write(string message)
    {
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            return;
        }

        try
        {
            File.AppendAllText(FilePath, $"{DateTime.Now.ToString("O", CultureInfo.InvariantCulture)} {message}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
