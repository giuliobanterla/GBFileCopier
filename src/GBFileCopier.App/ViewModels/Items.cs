using System.Globalization;
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

/// <summary>One row of the results list.</summary>
public sealed class ResultItem(FileCopyResult result, CopyMode mode)
{
    public string Name { get; } = Path.GetFileName(result.SourcePath);

    public string SourcePath { get; } = result.SourcePath;

    public string DestinationPath { get; } = result.DestinationPath;

    public string Status { get; } = result.Status switch
    {
        FileCopyStatus.Succeeded when result.Renamed => Strings.StatusMoved,
        FileCopyStatus.Succeeded when result.Verified => mode == CopyMode.Move ? Strings.StatusMovedVerified : Strings.StatusVerified,
        FileCopyStatus.Succeeded => mode == CopyMode.Move ? Strings.StatusMoved : Strings.StatusCopied,
        FileCopyStatus.Skipped => Strings.StatusSkipped,
        FileCopyStatus.Failed => Strings.StatusFailed,
        _ => Strings.StatusNotProcessed,
    };

    /// <summary>The error or warning, otherwise the destination and the checksum.</summary>
    public string Message { get; } = result.Error ?? result.Warning
        ?? (result.Hash is null ? result.DestinationPath : $"{result.DestinationPath}  ·  {result.Hash}");

    public bool IsFailed { get; } = result.Status == FileCopyStatus.Failed;

    public bool IsWarning { get; } = result.Status is FileCopyStatus.Skipped or FileCopyStatus.Cancelled || result.Warning is not null;

    public bool IsProblem => IsFailed || IsWarning;
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
