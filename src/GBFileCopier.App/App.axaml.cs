using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using GBFileCopier.App.Resources;
using GBFileCopier.App.Services;
using GBFileCopier.App.Services.Updates;
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

    private static Window? s_window;
    private static object? s_explorerPaste; // ExplorerPasteIntegration, Windows only

    /// <summary>Ctrl+V in Explorer is on: the app keeps running when its window is closed.</summary>
    public static bool RunsInBackground =>
        OperatingSystem.IsWindows() && s_explorerPaste is ExplorerPasteIntegration { IsEnabled: true };

    /// <summary>Exit was chosen: closing the window quits instead of hiding it.</summary>
    public static bool IsQuitting { get; private set; }

    /// <summary>Switches Ctrl+V in Explorer on or off (Settings), no restart needed.</summary>
    /// <returns>Whether it is on now: false if Windows refused it.</returns>
    public static bool ApplyExplorerPaste(bool enabled) =>
        OperatingSystem.IsWindows() && s_explorerPaste is ExplorerPasteIntegration integration && integration.Apply(enabled);

    private static bool s_windowOpened;

    /// <summary>
    /// Exit in the notification area: closes the window for real (running jobs are cancelled first);
    /// the app ends with its last window. A window never shown (started at sign-in) has nothing to close.
    /// </summary>
    public static void Quit()
    {
        IsQuitting = true;
        if (s_window is { } window && s_windowOpened)
        {
            window.Close();
        }
        else if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

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
        if (arguments.Contains(ExternalRequest.BackgroundArgument))
        {
            return; // started again at sign-in while already running: stay in the background
        }

        if (ExternalRequest.FromArguments(arguments, workingDirectory) is { } request)
        {
            viewModel.Apply(request);
            DiagnosticLog.Write($"Applied: sources={viewModel.Sources.Count} destination={viewModel.Destination} move={viewModel.PreferMove}");
        }

        BringToFront(window);
    }

    private static void BringToFront(Window window)
    {
        if (!window.IsVisible)
        {
            window.Show(); // hidden in the notification area, or started in the background
        }

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

            var updates = new UpdateViewModel(new UpdateService(), SettingsStore, () => !viewModel.HasActiveJobs,
                () => desktop.Shutdown(), uri => _ = window.Launcher.LaunchUriAsync(uri));
            viewModel.Updates = updates;
            window.DataContext = viewModel;
            s_window = window;
            window.Opened += (_, _) => s_windowOpened = true;

            // The app still ends with its last window: a window hidden in the notification area is not closed
            var background = false;
            if (OperatingSystem.IsWindows())
            {
                var integration = new ExplorerPasteIntegration(this, window, viewModel, () => BringToFront(window), Quit);
                s_explorerPaste = integration;
                // Started at sign-in (--background): only the notification area icon, the window opens when needed.
                // If the hook could not start there is no icon either: then the window shows, as usual.
                background = integration.Apply(SettingsStore.Load().ExplorerPaste)
                    && desktop.Args?.Contains(ExternalRequest.BackgroundArgument) == true;
            }

            if (!background)
            {
                desktop.MainWindow = window;
            }

            // Updates: the first check a few seconds after the start, then the timer checks once a day
            DispatcherTimer.RunOnce(() => _ = updates.StartAsync(), TimeSpan.FromSeconds(5));
            new DispatcherTimer(TimeSpan.FromHours(1), DispatcherPriority.Background, (_, _) => _ = updates.TickAsync()).Start();

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
                    MainWindow.Item(Strings.MenuCheckForUpdates, window.CheckForUpdates),
                    new NativeMenuItemSeparator(),
                    MainWindow.Item(Strings.MenuSettings, window.ShowSettings, new KeyGesture(Key.OemComma, KeyModifiers.Meta)),
                ]);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
