using Avalonia.Controls;
using Avalonia.Input;
using GBFileCopier.App.Services;
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
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || ViewModel is not { IsIdle: true } viewModel)
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
        if (ViewModel is { IsIdle: true } viewModel && e.DataTransfer.TryGetFiles() is { } items)
        {
            viewModel.AddSources(WindowUiServices.LocalPaths(items));
        }
    }

    private void OnDropDestination(object? sender, DragEventArgs e)
    {
        var folder = e.DataTransfer.TryGetFiles() is { } items
            ? WindowUiServices.LocalPaths(items).FirstOrDefault(Directory.Exists)
            : null;
        if (ViewModel is { IsIdle: true } viewModel && folder is not null)
        {
            viewModel.Destination = folder;
            e.Handled = true;
        }
    }

    /// <summary>Closing during a copy cancels it first, so that no temporary files are left behind.</summary>
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closingAfterCancel || ViewModel is not { IsRunning: true, RunningTask: { } running } viewModel)
        {
            return;
        }

        e.Cancel = true;
        _closingAfterCancel = true;
        viewModel.CancelCommand.Execute(null);
        try
        {
            await running;
        }
        finally
        {
            Close();
        }
    }
}
