using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
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

    /// <summary>Set by Program on Windows when this is the first running instance.</summary>
    public static SingleInstance? SingleInstance { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

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

        BringToFront(window);
    }

    /// <summary>Command line of a later start of the app, forwarded by <see cref="Services.SingleInstance"/>.</summary>
    private static void OnForwarded(IReadOnlyList<string> arguments, string workingDirectory, Window window, MainViewModel viewModel)
    {
        DiagnosticLog.Write($"Received: {string.Join(' ', arguments)}");
        if (ExternalRequest.FromArguments(arguments, workingDirectory) is { } request)
        {
            viewModel.Apply(request);
            DiagnosticLog.Write($"Applied: sources={viewModel.Sources.Count} destination={viewModel.Destination} move={viewModel.PreferMove}");
        }

        BringToFront(window);
    }

    private static void BringToFront(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }

    /// <summary>Switches the theme of every open window at once, no restart needed.</summary>
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
            // Command line: files and folders, or the Explorer context menu (--add, --paste, --move)
            if (ExternalRequest.FromArguments(desktop.Args ?? [], Environment.CurrentDirectory) is { } request)
            {
                viewModel.Apply(request);
            }
            if (viewModel.Settings is { WindowWidth: > 0, WindowHeight: > 0 } settings)
            {
                window.Width = settings.WindowWidth;
                window.Height = settings.WindowHeight;
            }

            window.DataContext = viewModel;
            desktop.MainWindow = window;

            SingleInstance?.Listen((arguments, directory) =>
                Dispatcher.UIThread.Post(() => OnForwarded(arguments, directory, window, viewModel)));

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
