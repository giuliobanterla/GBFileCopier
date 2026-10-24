using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GBFileCopier.App.Services;
using GBFileCopier.Core;
using GBFileCopier.Core.Copying;

namespace GBFileCopier.App.ViewModels;

public partial class ConflictViewModel(FileConflict conflict) : ViewModelBase
{
    public string FileName { get; } = Path.GetFileName(conflict.DestinationPath);

    public string Folder { get; } = Path.GetDirectoryName(conflict.DestinationPath) ?? string.Empty;

    public string ExistingDetails { get; } = Describe(conflict.DestinationLength, conflict.DestinationLastWriteTimeUtc);

    public string NewDetails { get; } = Describe(conflict.SourceLength, conflict.SourceLastWriteTimeUtc);

    public string Comparison { get; } =
        conflict.SourceLastWriteTimeUtc > conflict.DestinationLastWriteTimeUtc ? "The new file is more recent"
        : conflict.SourceLastWriteTimeUtc < conflict.DestinationLastWriteTimeUtc ? "The existing file is more recent"
        : "Same modification date";

    [ObservableProperty]
    public partial bool ApplyToAll { get; set; }

    /// <summary>Set when a button is pressed; the window closes with it.</summary>
    public event Action<ConflictAnswer>? Answered;

    [RelayCommand]
    private void Overwrite() => Answer(ConflictAction.Overwrite);

    [RelayCommand]
    private void Skip() => Answer(ConflictAction.Skip);

    [RelayCommand]
    private void KeepBoth() => Answer(ConflictAction.KeepBoth);

    private void Answer(ConflictAction action) => Answered?.Invoke(new ConflictAnswer(action, ApplyToAll));

    private static string Describe(long length, DateTime lastWriteUtc) =>
        $"{ByteSize.Format(length)}  ·  modified {lastWriteUtc.ToLocalTime():g}";
}
