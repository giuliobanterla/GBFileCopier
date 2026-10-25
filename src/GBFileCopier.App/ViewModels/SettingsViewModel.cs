using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using GBFileCopier.App.Resources;
using GBFileCopier.App.Settings;

namespace GBFileCopier.App.ViewModels;

public sealed record LanguageOption(string Code, string Label);

public sealed record ThemeOption(ThemeChoice Theme, string Label);

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsStore _store;
    private readonly string _initialLanguage;
    private readonly Action<ThemeChoice> _applyTheme;
    private readonly bool _initialized;

    /// <param name="store">Where the choices are saved.</param>
    /// <param name="location">Settings file path, shown to the user.</param>
    /// <param name="applyTheme">Switches the theme right away; App.ApplyTheme in the app.</param>
    public SettingsViewModel(ISettingsStore store, string? location = null, Action<ThemeChoice>? applyTheme = null)
    {
        _store = store;
        _applyTheme = applyTheme ?? (_ => { });
        var settings = store.Load();
        _initialLanguage = settings.Language;
        SelectedLanguage = Languages.FirstOrDefault(l => l.Code == _initialLanguage) ?? Languages[0];
        SelectedTheme = Themes.FirstOrDefault(t => t.Theme == settings.Theme) ?? Themes[0];
        CheckForUpdates = settings.CheckForUpdates == true;
        Location = location is null ? null : string.Format(CultureInfo.CurrentCulture, Strings.SettingsLocation, location);
        _initialized = true; // from now on, changes are the user's: save them
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

    public IReadOnlyList<ThemeOption> Themes { get; } =
    [
        new(ThemeChoice.System, Strings.ThemeSystem),
        new(ThemeChoice.Light, Strings.ThemeLight),
        new(ThemeChoice.Dark, Strings.ThemeDark),
    ];

    [ObservableProperty]
    public partial ThemeOption SelectedTheme { get; set; }

    /// <summary>Automatic update checks (GitHub, once a day); takes effect within the hour.</summary>
    [ObservableProperty]
    public partial bool CheckForUpdates { get; set; }

    /// <summary>The texts are read when the windows are created: a new language needs a restart.</summary>
    public bool NeedsRestart => SelectedLanguage.Code != _initialLanguage;

    public string? Location { get; }

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        if (!_initialized)
        {
            return;
        }

        _store.Save(_store.Load() with { Language = value.Code });
    }

    partial void OnCheckForUpdatesChanged(bool value)
    {
        if (_initialized)
        {
            _store.Save(_store.Load() with { CheckForUpdates = value });
        }
    }

    partial void OnSelectedThemeChanged(ThemeOption value)
    {
        if (!_initialized)
        {
            return;
        }

        _store.Save(_store.Load() with { Theme = value.Theme });
        _applyTheme(value.Theme);
    }
}
