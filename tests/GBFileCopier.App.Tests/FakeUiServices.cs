using GBFileCopier.App.Services;
using GBFileCopier.Core.Copying;
using GBFileCopier.Platform.Clipboard;

namespace GBFileCopier.App.Tests;

internal sealed class FakeUiServices : IUiServices
{
    public IFileSystemPlatform Platform => ManagedFileSystemPlatform.Instance;

    public ClipboardFiles? Clipboard { get; set; }

    public bool ClipboardCleared { get; private set; }

    public Func<FileConflict, ConflictAnswer> OnConflict { get; set; } = _ => new(ConflictAction.Skip, ApplyToAll: false);

    public int ConflictsAsked { get; private set; }

    public Task<IReadOnlyList<string>> PickFilesAsync() => Task.FromResult<IReadOnlyList<string>>([]);

    public Task<IReadOnlyList<string>> PickFoldersAsync(string title, bool allowMultiple) => Task.FromResult<IReadOnlyList<string>>([]);

    public Task<ConflictAnswer> AskConflictAsync(FileConflict conflict)
    {
        ConflictsAsked++;
        return Task.FromResult(OnConflict(conflict));
    }

    public ClipboardFiles? ReadClipboard() => Clipboard;

    public void ClearClipboard()
    {
        ClipboardCleared = true;
        Clipboard = null;
    }
}
