using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using GBFileCopier.App.Resources;
using GBFileCopier.Core;
using GBFileCopier.Core.Copying;
using GBFileCopier.Core.Hashing;

namespace GBFileCopier.App.ViewModels;

/// <summary>A file or folder to copy.</summary>
public sealed class SourceItem
{
    public SourceItem(string path)
    {
        Path = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
        Name = System.IO.Path.GetFileName(Path) is { Length: > 0 } name ? name : Path;
        Folder = System.IO.Path.GetDirectoryName(Path) ?? string.Empty;
        IsFolder = Directory.Exists(Path);
        Details = IsFolder ? Strings.Folder
            : File.Exists(Path) ? ByteSize.Format(new FileInfo(Path).Length, CultureInfo.CurrentCulture)
            : Strings.NotFound;
    }

    public string Path { get; }

    public string Name { get; }

    /// <summary>The folder that contains it.</summary>
    public string Folder { get; }

    public bool IsFolder { get; }

    public string Details { get; }
}

/// <summary>
/// One file of a job: waiting, being copied, then its result. The row of the job's file list.
/// </summary>
public sealed partial class JobFileItem : ObservableObject
{
    private JobFileItem(string sourcePath, string destinationPath, long length)
    {
        SourcePath = sourcePath;
        Name = Path.GetFileName(sourcePath) is { Length: > 0 } name ? name : sourcePath;
        DestinationPath = destinationPath;
        Length = length;
        Status = Strings.StatusQueued;
        Message = destinationPath;
    }

    /// <summary>A file of the plan, still to copy.</summary>
    public static JobFileItem Queued(PlannedFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return new JobFileItem(file.SourcePath, file.DestinationPath, file.Length) { IsQueued = true };
    }

    /// <summary>A result with no planned file before it: a problem found while scanning, a folder.</summary>
    public static JobFileItem Finished(FileCopyResult result, CopyMode mode)
    {
        ArgumentNullException.ThrowIfNull(result);
        var item = new JobFileItem(result.SourcePath, result.DestinationPath, result.Length);
        item.Complete(result, mode);
        return item;
    }

    public string Name { get; }

    public string SourcePath { get; }

    public long Length { get; }

    /// <summary>Where it goes; it changes when a conflict is solved by keeping both files.</summary>
    [ObservableProperty]
    public partial string DestinationPath { get; private set; }

    [ObservableProperty]
    public partial string Status { get; private set; }

    /// <summary>The destination while waiting; then the error or warning, or the destination and the checksum.</summary>
    [ObservableProperty]
    public partial string Message { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProblem))]
    public partial bool IsQueued { get; private set; }

    [ObservableProperty]
    public partial bool IsCopying { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProblem))]
    public partial bool IsFailed { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProblem))]
    public partial bool IsWarning { get; private set; }

    public bool IsProblem => IsFailed || IsWarning;

    public bool IsDone => !IsQueued && !IsCopying;

    /// <summary>Done without problems: shown in green.</summary>
    public bool IsOk => IsDone && !IsProblem;

    public void StartCopying()
    {
        IsQueued = false;
        IsCopying = true;
        Status = Strings.StatusCopying;
        OnPropertyChanged(nameof(IsDone));
        OnPropertyChanged(nameof(IsOk));
    }

    public void Complete(FileCopyResult result, CopyMode mode)
    {
        ArgumentNullException.ThrowIfNull(result);
        DestinationPath = result.DestinationPath;
        Status = result.Status switch
        {
            FileCopyStatus.Succeeded when result.Renamed => Strings.StatusMoved,
            FileCopyStatus.Succeeded when result.Verified => mode == CopyMode.Move ? Strings.StatusMovedVerified : Strings.StatusVerified,
            FileCopyStatus.Succeeded => mode == CopyMode.Move ? Strings.StatusMoved : Strings.StatusCopied,
            FileCopyStatus.Skipped => Strings.StatusSkipped,
            FileCopyStatus.Failed => Strings.StatusFailed,
            _ => Strings.StatusNotProcessed,
        };
        Message = result.Error ?? result.Warning
            ?? (result.Hash is null ? result.DestinationPath : $"{result.DestinationPath}  ·  {result.Hash}");
        IsFailed = result.Status == FileCopyStatus.Failed;
        IsWarning = result.Status is FileCopyStatus.Skipped or FileCopyStatus.Cancelled || result.Warning is not null;
        IsQueued = false;
        IsCopying = false;
        OnPropertyChanged(nameof(IsDone));
        OnPropertyChanged(nameof(IsOk));
    }
}

/// <summary>Which files of a job the list shows.</summary>
public enum JobFileFilter
{
    All,
    Queued,
    Done,
    Problems,
}

public sealed record JobFileFilterOption(JobFileFilter Filter, string Label)
{
    /// <summary>A new list each time: the labels follow the current UI language.</summary>
    public static IReadOnlyList<JobFileFilterOption> CreateAll() =>
    [
        new(JobFileFilter.All, Strings.FilterAll),
        new(JobFileFilter.Queued, Strings.FilterQueued),
        new(JobFileFilter.Done, Strings.FilterDone),
        new(JobFileFilter.Problems, Strings.FilterProblems),
    ];
}

public sealed record HashOption(HashAlgorithmKind Kind, string Label)
{
    /// <summary>A new list each time: the labels follow the current UI language.</summary>
    public static IReadOnlyList<HashOption> CreateAll() =>
    [
        new(HashAlgorithmKind.XxHash3, Strings.HashFastest),
        new(HashAlgorithmKind.XxHash128, "xxHash128"),
        new(HashAlgorithmKind.Crc32, "CRC32"),
        new(HashAlgorithmKind.Md5, "MD5"),
        new(HashAlgorithmKind.Sha1, "SHA-1"),
        new(HashAlgorithmKind.Sha256, "SHA-256"),
        new(HashAlgorithmKind.Sha512, "SHA-512"),
        new(HashAlgorithmKind.None, Strings.HashNone),
    ];
}

public sealed record ConflictOption(ConflictPolicy Policy, string Label)
{
    /// <summary>A new list each time: the labels follow the current UI language.</summary>
    public static IReadOnlyList<ConflictOption> CreateAll() =>
    [
        new(ConflictPolicy.Ask, Strings.ConflictAsk),
        new(ConflictPolicy.OverwriteIfNewer, Strings.ConflictOverwriteIfNewer),
        new(ConflictPolicy.Overwrite, Strings.ConflictOverwrite),
        new(ConflictPolicy.Skip, Strings.ConflictSkip),
        new(ConflictPolicy.KeepBoth, Strings.ConflictKeepBoth),
    ];
}
