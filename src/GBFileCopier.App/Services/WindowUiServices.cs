using Avalonia.Controls;
using Avalonia.Platform.Storage;
using GBFileCopier.App.ViewModels;
using GBFileCopier.App.Views;
using GBFileCopier.Core.Copying;
using GBFileCopier.Platform;
using GBFileCopier.Platform.Clipboard;

namespace GBFileCopier.App.Services;

internal sealed class WindowUiServices(Window window) : IUiServices
{
    public IFileSystemPlatform Platform => PlatformFileSystem.Current;

    public async Task<IReadOnlyList<string>> PickFilesAsync()
    {
        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add files",
            AllowMultiple = true,
        });
        return LocalPaths(files);
    }

    public async Task<IReadOnlyList<string>> PickFoldersAsync(string title, bool allowMultiple)
    {
        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = allowMultiple,
        });
        return LocalPaths(folders);
    }

    public async Task<ConflictAnswer> AskConflictAsync(FileConflict conflict)
    {
        var dialog = new ConflictWindow { DataContext = new ConflictViewModel(conflict) };
        return await dialog.ShowDialog<ConflictAnswer?>(window) ?? new ConflictAnswer(ConflictAction.Skip, ApplyToAll: false);
    }

    public ClipboardFiles? ReadClipboard()
    {
        if (!FileClipboard.IsSupported)
        {
            return null;
        }

        try
        {
            return FileClipboard.Read();
        }
        catch (IOException)
        {
            return null; // clipboard busy: like an empty one
        }
    }

    public void ClearClipboard()
    {
        try
        {
            FileClipboard.Clear();
        }
        catch (IOException)
        {
        }
    }

    internal static List<string> LocalPaths(IEnumerable<IStorageItem> items) =>
        [.. items.Select(i => i.TryGetLocalPath()).OfType<string>()];
}
