using GBFileCopier.Core.Hashing;

namespace GBFileCopier.Core.Copying;

public enum FileCopyStatus
{
    Succeeded,
    Skipped,
    Failed,

    /// <summary>Not completed because the job was cancelled.</summary>
    Cancelled,
}

public enum CopyItemKind
{
    File,
    Directory,
    SymbolicLink,
}

/// <summary>A file (or symbolic link) the job is going to copy, and where.</summary>
public sealed record PlannedFile(CopyItemKind Kind, string SourcePath, string DestinationPath, long Length);

public sealed record FileCopyResult
{
    public required string SourcePath { get; init; }

    public required string DestinationPath { get; init; }

    public CopyItemKind Kind { get; init; }

    public long Length { get; init; }

    public FileCopyStatus Status { get; init; }

    /// <summary>Checksum of the source, in hexadecimal; null if not computed.</summary>
    public string? Hash { get; init; }

    /// <summary>The destination was read back and matched the source.</summary>
    public bool Verified { get; init; }

    /// <summary>Moved by renaming it on the same volume: the data was not copied.</summary>
    public bool Renamed { get; init; }

    /// <summary>Why the file failed or was skipped.</summary>
    public string? Error { get; init; }

    /// <summary>A non-fatal problem, for example timestamps that could not be set.</summary>
    public string? Warning { get; init; }

    public int Attempts { get; init; }
}

public sealed record CopyJobResult
{
    public required IReadOnlyList<FileCopyResult> Files { get; init; }

    public HashAlgorithmKind HashAlgorithm { get; init; }

    public CopyMode Mode { get; init; }

    public TimeSpan Elapsed { get; init; }

    public bool WasCancelled { get; init; }

    /// <summary>Bytes written to the destination by successful copies.</summary>
    public long CopiedBytes { get; init; }

    /// <summary>A problem with the whole job rather than one file: the final flush to disk failed.</summary>
    public string? Warning { get; init; }

    public int SucceededCount => Files.Count(f => f.Status == FileCopyStatus.Succeeded);

    public int SkippedCount => Files.Count(f => f.Status == FileCopyStatus.Skipped);

    public int FailedCount => Files.Count(f => f.Status == FileCopyStatus.Failed);

    public int CancelledCount => Files.Count(f => f.Status == FileCopyStatus.Cancelled);

    public bool IsSuccess => !WasCancelled && FailedCount == 0;
}
