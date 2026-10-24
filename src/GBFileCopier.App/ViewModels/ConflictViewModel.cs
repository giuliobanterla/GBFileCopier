using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GBFileCopier.App.Resources;
using GBFileCopier.App.Services;
using GBFileCopier.Core;
using GBFileCopier.Core.Copying;

namespace GBFileCopier.App.ViewModels;

public partial class ConflictViewModel(FileConflict conflict) : ViewModelBase
{
    public string FileName { get; } = Path.GetFileName(conflict.DestinationPath);

    public string AlreadyExistsIn { get; } =
        string.Format(CultureInfo.CurrentCulture, Strings.AlreadyExistsIn, Path.GetDirectoryName(conflict.DestinationPath));

    public string ExistingDetails { get; } = Describe(conflict.DestinationLength, conflict.DestinationLastWriteTimeUtc);

    public string NewDetails { get; } = Describe(conflict.SourceLength, conflict.SourceLastWriteTimeUtc);

    public string Comparison { get; } =
        conflict.SourceLastWriteTimeUtc > conflict.DestinationLastWriteTimeUtc ? Strings.NewIsMoreRecent
        : conflict.SourceLastWriteTimeUtc < conflict.DestinationLastWriteTimeUtc ? Strings.ExistingIsMoreRecent
        : Strings.SameModificationDate;

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
        string.Format(CultureInfo.CurrentCulture, Strings.SizeAndModified,
            ByteSize.Format(length, CultureInfo.CurrentCulture), lastWriteUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
}
