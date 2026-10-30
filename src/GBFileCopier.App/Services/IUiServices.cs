using GBFileCopier.Core.Copying;
using GBFileCopier.Platform.Clipboard;

namespace GBFileCopier.App.Services;

/// <summary>Answer of the conflict dialog.</summary>
public sealed record ConflictAnswer(ConflictAction Action, bool ApplyToAll);

/// <summary>
/// What the view models need from the window and the operating system. The window implements it;
/// tests use a fake.
/// </summary>
public interface IUiServices
{
    IFileSystemPlatform Platform { get; }

    Task<IReadOnlyList<string>> PickFilesAsync();

    Task<IReadOnlyList<string>> PickFoldersAsync(string title, bool allowMultiple);

    /// <summary>A .sha256, .md5, .sfv… file to verify; null if none was chosen.</summary>
    Task<string?> PickChecksumFileAsync();

    /// <summary>Asks what to do with a file that already exists. Called on the UI thread.</summary>
    Task<ConflictAnswer> AskConflictAsync(FileConflict conflict);

    /// <summary>Files copied or cut in the Finder or Explorer; null if none or unsupported.</summary>
    ClipboardFiles? ReadClipboard();

    void ClearClipboard();
}
