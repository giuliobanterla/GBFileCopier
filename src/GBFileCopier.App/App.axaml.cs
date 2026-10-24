using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using GBFileCopier.App.Resources;
using GBFileCopier.App.Services;
using GBFileCopier.App.Settings;
using GBFileCopier.App.ViewModels;
using GBFileCopier.App.Views;

namespace GBFileCopier.App;

public partial class App : Application
{
    /// <summary>The settings file; Program reads the language from it before the app starts.</summary>
    public static ISettingsStore SettingsStore { get; set; } = new SettingsStore(Settings.SettingsStore.DefaultPath);

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>Switches the theme of every open window at once, no restart needed.</summary>
    private static void OnActivated(ActivatedEventArgs e, Window window, MainViewModel viewModel)
    {
        DiagnosticLog.Write(e switch
        {
            ProtocolActivatedEventArgs p => $"Activated: {p.Uri}",
            FileActivatedEventArgs f => $"Activated: {f.Files.Count} file(s)",
            _ => $"Activated: {e.Kind}",
        });
        switch (e)
        {
            case ProtocolActivatedEventArgs { Uri: var uri } when ExternalRequest.Parse(uri) is { } request:
                viewModel.Apply(request);
                DiagnosticLog.Write($"Applied: sources={viewModel.Sources.Count} destination={viewModel.Destination} move={viewModel.PreferMove}");
                break;
            case FileActivatedEventArgs { Files: var files }:
                viewModel.AddSources(WindowUiServices.LocalPaths(files));
                break;
            default:
                return;
        }

        // Bring the window to the front, also if it was minimized
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }

    public static void ApplyTheme(ThemeChoice theme)
    {
        if (Current is { } app)
        {
            app.RequestedThemeVariant = theme switch
            {
                ThemeChoice.Light => ThemeVariant.Light,
                ThemeChoice.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default, // follows the system
            };
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            ApplyTheme(SettingsStore.Load().Theme);

            var window = new MainWindow();
            var viewModel = new MainViewModel(new WindowUiServices(window), SettingsStore);
            // Files and folders passed on the command line (and later by the shell integration)
            viewModel.AddSources(desktop.Args?.Where(Path.Exists) ?? []);
            if (viewModel.Settings is { WindowWidth: > 0, WindowHeight: > 0 } settings)
            {
                window.Width = settings.WindowWidth;
                window.Height = settings.WindowHeight;
            }

            window.DataContext = viewModel;
            desktop.MainWindow = window;

            // macOS: gbfilecopier:// links (from the Finder extension) and files dropped on the Dock icon
            if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
            {
                activatable.Activated += (_, e) => OnActivated(e, window, viewModel);
            }

            if (OperatingSystem.IsMacOS())
            {
                // The application menu: About and Settings above the items macOS adds (Services, Hide, Quit)
                NativeMenu.SetMenu(this,
                [
                    MainWindow.Item(Strings.MenuAbout, window.ShowAbout),
                    new NativeMenuItemSeparator(),
                    MainWindow.Item(Strings.MenuSettings, window.ShowSettings, new KeyGesture(Key.OemComma, KeyModifiers.Meta)),
                ]);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
