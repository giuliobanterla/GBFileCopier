using System.Text.Json;
using GBFileCopier.Core;

namespace GBFileCopier.App.Settings;

public interface ISettingsStore
{
    AppSettings Load();

    void Save(AppSettings settings);
}

/// <summary>
/// settings.json in the per-user application data folder:
/// ~/Library/Application Support/GBFileCopier on macOS, %APPDATA%\GBFileCopier on Windows.
/// </summary>
public sealed class SettingsStore(string filePath) : ISettingsStore
{
    public static string DefaultPath { get; } = Path.Combine(
        OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppInfo.Name,
        "settings.json");

    public string FilePath { get; } = filePath;

    /// <summary>The saved settings; the defaults if there are none or the file is unreadable.</summary>
    public AppSettings Load()
    {
        try
        {
            using var stream = File.OpenRead(FilePath);
            return JsonSerializer.Deserialize(stream, AppSettingsJsonContext.Default.AppSettings) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    /// <summary>Writes a temporary file and renames it, so that a crash never leaves a truncated file.</summary>
    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temp = FilePath + ".tmp";
            using (var stream = File.Create(temp))
            {
                JsonSerializer.Serialize(stream, settings, AppSettingsJsonContext.Default.AppSettings);
            }

            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not being able to save the settings must never stop a copy
        }
    }
}
