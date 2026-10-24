using Avalonia;
using Avalonia.Headless;
using GBFileCopier.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

namespace GBFileCopier.App.Tests;

/// <summary>The real app, rendered with Skia but without a screen.</summary>
public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
