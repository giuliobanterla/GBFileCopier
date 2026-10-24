using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GBFileCopier.App.Services;
using GBFileCopier.App.ViewModels;
using GBFileCopier.App.Views;

namespace GBFileCopier.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            var viewModel = new MainViewModel(new WindowUiServices(window));
            // Files and folders passed on the command line (and later by the shell integration)
            viewModel.AddSources(desktop.Args?.Where(Path.Exists) ?? []);
            window.DataContext = viewModel;
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
