using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GBFileCopier.App.Resources;
using GBFileCopier.Core;

namespace GBFileCopier.App.Views;

public partial class AboutWindow : Window
{
    public static Uri Website { get; } = new("https://github.com/giuliobanterla/GBFileCopier");

    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = string.Format(CultureInfo.CurrentCulture, Strings.AboutVersion, AppInfo.Version.Split('+')[0]);
    }

    private async void OnWebsite(object? sender, RoutedEventArgs e) => await Launcher.LaunchUriAsync(Website);

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
