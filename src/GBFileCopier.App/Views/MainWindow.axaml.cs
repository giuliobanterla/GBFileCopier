using Avalonia.Controls;
using Avalonia.Input;
using GBFileCopier.App.Resources;
using GBFileCopier.App.Services;
using GBFileCopier.App.Settings;
using GBFileCopier.App.ViewModels;

namespace GBFileCopier.App.Views;

public partial class MainWindow : Window
{
    private bool _closingAfterCancel;

    public MainWindow()
    {
        InitializeComponent();

        DragDrop.AddDragOverHandler(SourcesDropArea, OnDragOver);
        DragDrop.AddDropHandler(SourcesDropArea, OnDropSources);
        DragDrop.AddDragOverHandler(DestinationBox, OnDragOver);
        DragDrop.AddDropHandler(DestinationBox, OnDropDestination);

        NativeMenu.SetMenu(this, CreateMenu());
    }

    /// <summary>Where the Settings window reads and writes; the real settings file unless replaced.</summary>
    public ISettingsStore SettingsStore { get; set; } = App.SettingsStore;

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    /// <summary>⌘ on macOS, Ctrl elsewhere.</summary>
    private static KeyModifiers CommandKey => OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    public async void ShowSettings()
    {
        var location = SettingsStore is SettingsStore file ? file.FilePath : null;
        await new SettingsWindow { DataContext = new SettingsViewModel(SettingsStore, location, App.ApplyTheme, App.ApplyExplorerPaste) }.ShowDialog(this);
    }

    public async void ShowAbout() => await new AboutWindow().ShowDialog(this);

    public void CheckForUpdates() => _ = ViewModel?.Updates?.CheckNowAsync();

    /// <summary>
    /// File and Help menus. On macOS they go to the system menu bar, after the application menu
    /// (which has About, Settings and Quit, see App); elsewhere the NativeMenuBar shows them in the window.
    /// Paste has no shortcut here on purpose: a menu shortcut would take ⌘V away from the text boxes.
    /// </summary>
    private NativeMenu CreateMenu()
    {
        var file = new NativeMenu
        {
            Item(Strings.AddFiles, () => ViewModel?.AddFilesCommand.Execute(null), new KeyGesture(Key.O, CommandKey)),
            Item(Strings.AddFolder, () => ViewModel?.AddFolderCommand.Execute(null), new KeyGesture(Key.O, CommandKey | KeyModifiers.Shift)),
            new NativeMenuItemSeparator(),
            Item(Strings.MenuPasteFiles, () => ViewModel?.Paste(move: false)),
            Item(Strings.MenuPasteFilesToMove, () => ViewModel?.Paste(move: true)),
            new NativeMenuItemSeparator(),
            Item(Strings.MenuVerifyChecksumFile, () => ViewModel?.VerifyChecksumFileCommand.Execute(null)),
        };
        var help = new NativeMenu
        {
            Item(Strings.MenuWebsite, () => _ = Launcher.LaunchUriAsync(AboutWindow.Website)),
            Item(Strings.MenuDonate, () => _ = Launcher.LaunchUriAsync(AboutWindow.DonationPage)),
        };
        if (!OperatingSystem.IsMacOS())
        {
            help.Add(Item(Strings.MenuCheckForUpdates, CheckForUpdates));
        }

        if (!OperatingSystem.IsMacOS())
        {
            file.Add(new NativeMenuItemSeparator());
            file.Add(Item(Strings.MenuSettings, ShowSettings));
            file.Add(new NativeMenuItemSeparator());
            file.Add(Item(Strings.MenuExit, Close));
            help.Add(new NativeMenuItemSeparator());
            help.Add(Item(Strings.MenuAbout, ShowAbout));
        }

        return
        [
            new NativeMenuItem(Strings.MenuFile) { Menu = file },
            new NativeMenuItem(Strings.MenuHelp) { Menu = help },
        ];
    }

    internal static NativeMenuItem Item(string header, Action action, KeyGesture? gesture = null)
    {
        var item = new NativeMenuItem(header) { Gesture = gesture };
        item.Click += (_, _) => action();
        return item;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || ViewModel is not { } viewModel)
        {
            return;
        }

        // ⌘ on macOS, Ctrl elsewhere. In a text box ⌘V must paste text, as usual.
        var command = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        var inTextBox = FocusManager?.GetFocusedElement() is TextBox;

        if (e.Key == Key.V && e.KeyModifiers.HasFlag(command) && !inTextBox)
        {
            // ⌥⌘V moves, like in the Finder
            viewModel.Paste(move: e.KeyModifiers.HasFlag(KeyModifiers.Alt));
            e.Handled = true;
        }
        else if (e.Key is Key.Delete or Key.Back && SourcesList.IsKeyboardFocusWithin)
        {
            viewModel.RemoveSelectedCommand.Execute(null);
            e.Handled = true;
        }
    }

    private static void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;

    private void OnDropSources(object? sender, DragEventArgs e)
    {
        if (ViewModel is { } viewModel && e.DataTransfer.TryGetFiles() is { } items)
        {
            viewModel.AddSources(WindowUiServices.LocalPaths(items));
        }
    }

    private void OnDropDestination(object? sender, DragEventArgs e)
    {
        var folder = e.DataTransfer.TryGetFiles() is { } items
            ? WindowUiServices.LocalPaths(items).FirstOrDefault(Directory.Exists)
            : null;
        if (ViewModel is { } viewModel && folder is not null)
        {
            viewModel.Destination = folder;
            e.Handled = true;
        }
    }

    /// <summary>Closing with jobs waiting or running cancels them first, so that no temporary files are left behind.</summary>
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        // Ctrl+V in Explorer needs the app running: closing the window only hides it (Exit in the
        // notification area quits). Windows shutting down or an installer closing the app still quit.
        if (App.RunsInBackground && !App.IsQuitting && e.CloseReason == WindowCloseReason.WindowClosing)
        {
            e.Cancel = true;
            ViewModel?.SaveSettings(Width, Height);
            Hide();
            return;
        }

        if (!_closingAfterCancel)
        {
            ViewModel?.SaveSettings(Width, Height);
        }

        if (_closingAfterCancel || ViewModel is not { HasActiveJobs: true } viewModel)
        {
            return;
        }

        e.Cancel = true;
        _closingAfterCancel = true;
        try
        {
            await viewModel.CancelAllAsync();
        }
        finally
        {
            Close();
        }
    }
}
