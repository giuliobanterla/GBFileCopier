using Avalonia;
using GBFileCopier.App.Services;
using GBFileCopier.Platform;

namespace GBFileCopier.App;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Windows: Explorer starts the app once per selected item; only the first start opens the
        // window, the others hand their arguments over to it. (macOS already has one app instance.)
        if (OperatingSystem.IsWindows())
        {
            App.SingleInstance = SingleInstance.TryAcquire();
            if (App.SingleInstance is null)
            {
                WindowsForeground.AllowAnyProcess(); // let the running app come to the front
                if (SingleInstance.Forward(args, Environment.CurrentDirectory, TimeSpan.FromSeconds(10)))
                {
                    return;
                }
            }
        }

        UiLanguage.Apply(UiLanguage.GetPreferred(App.SettingsStore.Load().Language));
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
