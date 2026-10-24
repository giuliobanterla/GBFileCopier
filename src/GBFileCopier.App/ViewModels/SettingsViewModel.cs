using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using GBFileCopier.App.Resources;
using GBFileCopier.App.Settings;

namespace GBFileCopier.App.ViewModels;

public sealed record LanguageOption(string Code, string Label);

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsStore _store;
    private readonly string _initialLanguage;

    public SettingsViewModel(ISettingsStore store, string? location = null)
    {
        _store = store;
        _initialLanguage = store.Load().Language;
        SelectedLanguage = Languages.FirstOrDefault(l => l.Code == _initialLanguage) ?? Languages[0];
        Location = location is null ? null : string.Format(CultureInfo.CurrentCulture, Strings.SettingsLocation, location);
    }

    /// <summary>Language names are written in their own language, as usual in language menus.</summary>
    public IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new(string.Empty, Strings.LanguageSystem),
        new("en", "English"),
        new("it", "Italiano"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsRestart))]
    public partial LanguageOption SelectedLanguage { get; set; }

    /// <summary>The texts are read when the windows are created: a new language needs a restart.</summary>
    public bool NeedsRestart => SelectedLanguage.Code != _initialLanguage;

    public string? Location { get; }

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        _store.Save(_store.Load() with { Language = value.Code });
    }
}
