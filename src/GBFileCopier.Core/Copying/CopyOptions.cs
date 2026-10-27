using GBFileCopier.Core.Hashing;

namespace GBFileCopier.Core.Copying;

public enum CopyMode
{
    Copy,

    /// <summary>Copy, then delete each source file once its copy has completed (and been verified, if enabled).</summary>
    Move,
}

/// <summary>What to do when the destination file already exists.</summary>
public enum ConflictPolicy
{
    /// <summary>Ask through the conflict resolver; skip if there is none.</summary>
    Ask,
    Overwrite,

    /// <summary>Overwrite only if the source was modified more recently than the destination.</summary>
    OverwriteIfNewer,
    Skip,

    /// <summary>Keep the existing file and save the new one as "name (2).ext".</summary>
    KeepBoth,
}

/// <summary>Answer of the conflict resolver for a single file.</summary>
public enum ConflictAction
{
    Overwrite,
    Skip,
    KeepBoth,
}

/// <summary>A file that already exists at the destination.</summary>
public sealed record FileConflict(
    string SourcePath,
    string DestinationPath,
    long SourceLength,
    DateTime SourceLastWriteTimeUtc,
    long DestinationLength,
    DateTime DestinationLastWriteTimeUtc);

/// <summary>
/// Called for each conflict when the policy is <see cref="ConflictPolicy.Ask"/>.
/// It runs on a thread pool thread: a UI must dispatch to its own thread.
/// </summary>
public delegate ValueTask<ConflictAction> ConflictResolver(FileConflict conflict, CancellationToken cancellationToken);

public sealed record CopyOptions
{
    /// <summary>Checksum computed while copying. <see cref="HashAlgorithmKind.None"/> disables it, unless <see cref="Verify"/> is on.</summary>
    public HashAlgorithmKind HashAlgorithm { get; init; } = HashAlgorithmKind.XxHash3;

    /// <summary>After copying, read the destination back and compare its checksum with the source's.</summary>
    public bool Verify { get; init; }

    public ConflictPolicy ConflictPolicy { get; init; } = ConflictPolicy.Ask;

    /// <summary>Copy the last write and creation times of files and folders.</summary>
    public bool PreserveTimestamps { get; init; } = true;

    /// <summary>Copy the file attributes (Windows) or the permissions (macOS/Linux).</summary>
    public bool PreserveAttributes { get; init; } = true;

    /// <summary>Force the data to disk before the file is considered copied.</summary>
    public bool FlushToDisk { get; init; } = true;

    /// <summary>
    /// Also force each file to disk when the destination is a network share. Off by default: the
    /// server then has to write the file to its disks before answering, which made copies of many
    /// small files to a NAS several times slower (137 s instead of 28 s for 150 files of 2 MB).
    /// The server still writes its cache out by itself within seconds.
    /// </summary>
    public bool FlushOnNetworkShares { get; init; }

    /// <summary>
    /// Set by the job for copies: a write barrier per file where the platform has one, and one flush
    /// of the whole drive at the end of the job, instead of a full flush per file.
    /// </summary>
    internal bool BarrierThenFinalFlush { get; init; }

    /// <summary>How many times a file is retried after a transient error.</summary>
    public int RetryCount { get; init; } = 2;

    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Size of each block read and written, a multiple of 4096 bytes.</summary>
    public int BufferSize { get; init; } = 1024 * 1024;

    /// <summary>Blocks that can be in flight between the reader and the writer.</summary>
    public int BufferCount { get; init; } = 4;

    /// <summary>The algorithm actually used: verification needs one even if none was chosen.</summary>
    public HashAlgorithmKind EffectiveHashAlgorithm =>
        HashAlgorithm == HashAlgorithmKind.None && Verify ? HashAlgorithmKind.XxHash3 : HashAlgorithm;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(BufferSize, FilePipeline.Alignment, nameof(BufferSize));
        if (BufferSize % FilePipeline.Alignment != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(BufferSize), BufferSize, $"Must be a multiple of {FilePipeline.Alignment}");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(BufferCount, 1, nameof(BufferCount));
        ArgumentOutOfRangeException.ThrowIfNegative(RetryCount, nameof(RetryCount));
        ArgumentOutOfRangeException.ThrowIfLessThan(RetryDelay, TimeSpan.Zero, nameof(RetryDelay));
    }
}

/// <summary>What to copy and where: each source (file or folder) ends up inside <see cref="Destination"/>.</summary>
public sealed record CopyRequest
{
    public required IReadOnlyList<string> Sources { get; init; }

    /// <summary>Destination folder, created if missing.</summary>
    public required string Destination { get; init; }

    public CopyMode Mode { get; init; } = CopyMode.Copy;

    public CopyOptions Options { get; init; } = new();
}
