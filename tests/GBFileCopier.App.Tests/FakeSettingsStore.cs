using GBFileCopier.App.Settings;

namespace GBFileCopier.App.Tests;

internal sealed class FakeSettingsStore : ISettingsStore
{
    public AppSettings Saved { get; set; } = new();

    public int SaveCount { get; private set; }

    public AppSettings Load() => Saved;

    public void Save(AppSettings settings)
    {
        Saved = settings;
        SaveCount++;
    }
}
