using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
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

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
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
