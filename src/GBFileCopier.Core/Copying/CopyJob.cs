using System.Globalization;
using GBFileCopier.Core.Hashing;
using GBFileCopier.Core.Resources;

namespace GBFileCopier.Core.Copying;

/// <summary>
/// One copy or move operation. Each file is written to a temporary name next to its
/// destination, checked, and only then renamed into place: an interrupted or failed copy
/// never leaves a partial file and never damages an existing one. Errors on one file do not
/// stop the others; they are collected in the result.
/// </summary>
public sealed class CopyJob
{
    private const string TempSuffix = ".gbcopy-tmp";

    private readonly PauseController _pause = new();
    private readonly IFileSystemPlatform _platform;
    private readonly CopyOptions _pipelineOptions;

    // Files written with only a write barrier, newest last: one of them is flushed at the end of the job
    private readonly List<string> _awaitingFlush = [];
    private int _started;

    /// <param name="request">What to copy and how.</param>
    /// <param name="platform">Native file system operations; the portable implementation if null.</param>
    public CopyJob(CopyRequest request, IFileSystemPlatform? platform = null)
        : this(request, platform, destinationOnNetwork: null)
    {
    }

    /// <param name="destinationOnNetwork">Overrides the detection of network shares (for tests).</param>
    internal CopyJob(CopyRequest request, IFileSystemPlatform? platform, bool? destinationOnNetwork)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfZero(request.Sources.Count, nameof(request));
        request.Options.Validate();
        Request = request;
        _platform = platform ?? ManagedFileSystemPlatform.Instance;
        var onNetwork = destinationOnNetwork ?? DestinationDrive.IsNetwork(request.Destination);
        FlushesEachFile = request.Options.FlushToDisk && (request.Options.FlushOnNetworkShares || !onNetwork);
        // Copies: a barrier per file and one flush at the end. Moves across volumes keep a full flush per
        // file: the source is deleted right after, on another disk that the barrier does not order.
        _pipelineOptions = request.Options with
        {
            FlushToDisk = FlushesEachFile,
            BarrierThenFinalFlush = request.Mode == CopyMode.Copy,
        };
    }

    public CopyRequest Request { get; }

    /// <summary>Each file is forced to disk: asked for, and the destination is not a network share (unless asked for there too).</summary>
    public bool FlushesEachFile { get; }

    /// <summary>
    /// Called once the sources have been scanned, with the files to copy in copy order (and where
    /// each one goes). Called on a thread pool thread: a UI must dispatch to its own thread.
    /// </summary>
    public Action<IReadOnlyList<PlannedFile>>? Planned { get; set; }

    /// <summary>
    /// Called for each file, link or folder as soon as it is done (copied, skipped, failed or
    /// cancelled), also for the problems found while scanning. Called on a thread pool thread.
    /// </summary>
    public Action<FileCopyResult>? FileFinished { get; set; }

    private CopyOptions Options => Request.Options;

    private int Passes => Options.Verify ? 2 : 1;

    public bool IsPaused => _pause.IsPaused;

    public void Pause() => _pause.Pause();

    public void Resume() => _pause.Resume();

    /// <summary>Runs the job on the thread pool. A job can run only once.</summary>
    /// <param name="progress">Called from thread pool threads, about ten times per second.</param>
    /// <param name="conflictResolver">Used when the policy is <see cref="ConflictPolicy.Ask"/>.</param>
    /// <param name="cancellationToken">Stops the job; the result then has <see cref="CopyJobResult.WasCancelled"/> set.</param>
    public Task<CopyJobResult> RunAsync(
        IProgress<CopyProgress>? progress = null,
        ConflictResolver? conflictResolver = null,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("A copy job can run only once");
        }

        return Task.Run(() => RunCoreAsync(progress, conflictResolver, cancellationToken), CancellationToken.None);
    }

    private async Task<CopyJobResult> RunCoreAsync(
        IProgress<CopyProgress>? progress,
        ConflictResolver? conflictResolver,
        CancellationToken cancellationToken)
    {
        var results = new List<FileCopyResult>();
        void Add(FileCopyResult result)
        {
            results.Add(result);
            FileFinished?.Invoke(result);
        }

        progress?.Report(new CopyProgress(CopyPhase.Scanning, null, 0, 0, 0, 0, TimeSpan.Zero));

        CopyPlan plan;
        try
        {
            plan = CopyPlanner.Create(Request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CreateResult(results, TimeSpan.Zero, wasCancelled: true);
        }

        Planned?.Invoke([.. plan.Files.Select(f => new PlannedFile(f.Kind, f.SourcePath, f.DestinationPath, f.Length))]);
        foreach (var error in plan.Errors)
        {
            Add(new FileCopyResult
            {
                SourcePath = error.SourcePath,
                DestinationPath = error.DestinationPath,
                Status = FileCopyStatus.Failed,
                Error = error.Message,
            });
        }

        var tracker = new ProgressTracker(progress, plan.TotalBytes * Passes, plan.Files.Count);
        CreateDirectories(plan, Add);

        var cancelled = false;
        foreach (var item in plan.Files)
        {
            if (cancelled)
            {
                Add(CreateItemResult(item, item.DestinationPath, FileCopyStatus.Cancelled));
                continue;
            }

            try
            {
                await _pause.WaitIfPausedAsync(cancellationToken);
                Add(await ProcessItemAsync(item, tracker, conflictResolver, cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                Add(CreateItemResult(item, item.DestinationPath, FileCopyStatus.Cancelled));
            }

            tracker.CompleteFile();
        }

        if (!cancelled)
        {
            FinishDirectories(plan);
        }

        FlushAwaitingFiles();
        tracker.Finish();
        return CreateResult(results, tracker.Elapsed, cancelled);
    }

    /// <summary>
    /// The end of the job, also when cancelled: one full flush makes the drive write its whole cache,
    /// so every file written with only a barrier is on permanent storage before the job reports.
    /// Through the newest of those files that can still be opened (the user may delete some meanwhile).
    /// </summary>
    private void FlushAwaitingFiles()
    {
        for (var index = _awaitingFlush.Count - 1; index >= 0; index--)
        {
            try
            {
                using var handle = File.OpenHandle(_awaitingFlush[index], FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                _platform.FlushToDisk(handle);
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Try an older one
            }
        }

        _awaitingFlush.Clear();
    }

    private CopyJobResult CreateResult(List<FileCopyResult> results, TimeSpan elapsed, bool wasCancelled) => new()
    {
        Files = results,
        HashAlgorithm = Options.EffectiveHashAlgorithm,
        Mode = Request.Mode,
        Elapsed = elapsed,
        WasCancelled = wasCancelled,
        CopiedBytes = results.Where(r => r.Status == FileCopyStatus.Succeeded).Sum(r => r.Length),
    };

    private static void CreateDirectories(CopyPlan plan, Action<FileCopyResult> add)
    {
        foreach (var directory in plan.Directories)
        {
            try
            {
                Directory.CreateDirectory(directory.DestinationPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                add(CreateItemResult(directory, directory.DestinationPath, FileCopyStatus.Failed, error: ex.Message));
            }
        }
    }

    /// <summary>Folder timestamps go last, because creating files inside a folder changes them.</summary>
    private void FinishDirectories(CopyPlan plan)
    {
        for (var i = plan.Directories.Count - 1; i >= 0; i--)
        {
            var directory = plan.Directories[i];
            try
            {
                if (Options.PreserveTimestamps && Directory.Exists(directory.DestinationPath))
                {
                    Directory.SetLastWriteTimeUtc(directory.DestinationPath, directory.LastWriteTimeUtc);
                }

                // Only empty folders are removed: anything skipped or failed stays where it was
                if (Request.Mode == CopyMode.Move)
                {
                    Directory.Delete(directory.SourcePath, recursive: false);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Not worth failing the job for
            }
        }
    }

    private async Task<FileCopyResult> ProcessItemAsync(
        CopyItem item,
        ProgressTracker tracker,
        ConflictResolver? conflictResolver,
        CancellationToken cancellationToken)
    {
        var workload = item.Length * Passes;
        var target = item.DestinationPath;
        var overwrite = false;

        if (PathExists(target))
        {
            switch (await ResolveConflictAsync(item, target, conflictResolver, cancellationToken))
            {
                case ConflictAction.Overwrite:
                    overwrite = true;
                    break;
                case ConflictAction.KeepBoth:
                    target = GetAvailableName(target);
                    break;
                default:
                    tracker.AddBytes(workload);
                    return CreateItemResult(item, target, FileCopyStatus.Skipped, error: Messages.DestinationExists);
            }
        }

        tracker.StartFile(item.SourcePath, CopyPhase.Copying);
        if (Request.Mode == CopyMode.Move && TryMoveWithinVolume(item, target, overwrite))
        {
            tracker.AddBytes(workload);
            return CreateItemResult(item, target, FileCopyStatus.Succeeded, attempts: 1) with { Renamed = true };
        }

        long fileProgress = 0;
        void OnBytes(long bytes)
        {
            fileProgress += bytes;
            tracker.AddBytes(bytes);
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var (hash, verified, warning) = item.Kind == CopyItemKind.SymbolicLink
                    ? CopySymbolicLink(item, target, overwrite)
                    : await CopyFileAsync(item, target, overwrite, tracker, OnBytes, cancellationToken);
                tracker.AddBytes(workload - fileProgress);

                var result = CreateItemResult(item, target, FileCopyStatus.Succeeded, hash, verified, warning: warning, attempts: attempt);
                return Request.Mode == CopyMode.Move ? DeleteSource(item, result) : result;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                tracker.AddBytes(-fileProgress);
                fileProgress = 0;

                if (attempt <= Options.RetryCount && IsTransient(ex))
                {
                    await Task.Delay(Options.RetryDelay, cancellationToken);
                    tracker.StartFile(item.SourcePath, CopyPhase.Copying);
                    continue;
                }

                tracker.AddBytes(workload);
                return CreateItemResult(item, target, FileCopyStatus.Failed, error: ex.Message, attempts: attempt);
            }
        }
    }

    /// <summary>
    /// On the same volume a move is just a rename: instant, and the data is never rewritten,
    /// so there is nothing to verify. Any error falls back to the normal copy, which has retries
    /// and reports the error if it persists.
    /// </summary>
    private bool TryMoveWithinVolume(CopyItem item, string target, bool overwrite)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (overwrite)
            {
                ClearReadOnly(target);
            }

            return _platform.TryMoveWithinVolume(item.SourcePath, target, overwrite);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async ValueTask<ConflictAction> ResolveConflictAsync(
        CopyItem item,
        string target,
        ConflictResolver? conflictResolver,
        CancellationToken cancellationToken)
    {
        switch (Options.ConflictPolicy)
        {
            case ConflictPolicy.Overwrite:
                return ConflictAction.Overwrite;
            case ConflictPolicy.KeepBoth:
                return ConflictAction.KeepBoth;
            case ConflictPolicy.OverwriteIfNewer:
                return item.LastWriteTimeUtc > File.GetLastWriteTimeUtc(target) ? ConflictAction.Overwrite : ConflictAction.Skip;
            case ConflictPolicy.Ask when conflictResolver is not null:
                var existing = new FileInfo(target);
                var conflict = new FileConflict(
                    item.SourcePath,
                    target,
                    item.Length,
                    item.LastWriteTimeUtc,
                    existing.Exists ? existing.Length : 0,
                    existing.LastWriteTimeUtc);
                return await conflictResolver(conflict, cancellationToken);
            default:
                return ConflictAction.Skip;
        }
    }

    private async Task<(string? Hash, bool Verified, string? Warning)> CopyFileAsync(
        CopyItem item,
        string target,
        bool overwrite,
        ProgressTracker tracker,
        Action<long> onBytes,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(directory);
        string? temp = GetTempPath(target);

        try
        {
            var source = new FileInfo(item.SourcePath);
            var metadata = SourceMetadata.Read(source);

            var (bytes, hash, onlyOrdered) = await FilePipeline.CopyAsync(
                item.SourcePath, temp, metadata.Length, _pipelineOptions, _platform, _pause, onBytes, cancellationToken);

            source.Refresh();
            if (bytes != metadata.Length || source.Length != metadata.Length || source.LastWriteTimeUtc != metadata.LastWriteTimeUtc)
            {
                throw new IOException(Messages.SourceChanged);
            }

            var verified = false;
            if (Options.Verify)
            {
                tracker.SetPhase(CopyPhase.Verifying);
                var copyHash = await FilePipeline.HashFileAsync(
                    temp, Options.EffectiveHashAlgorithm, Options.BufferSize, _platform, _pause, onBytes, cancellationToken);
                if (!copyHash.AsSpan().SequenceEqual(hash))
                {
                    throw new IOException(Messages.VerificationFailed);
                }

                verified = true;
            }

            if (overwrite)
            {
                ClearReadOnly(target);
            }

            File.Move(temp, target, overwrite);
            temp = null;
            if (onlyOrdered)
            {
                _awaitingFlush.Add(target);
            }

            var warning = ApplyMetadata(target, metadata);
            return (hash is null ? null : StreamingHasher.ToHex(hash), verified, warning);
        }
        finally
        {
            if (temp is not null)
            {
                TryDeleteFile(temp);
            }
        }
    }

    private static (string? Hash, bool Verified, string? Warning) CopySymbolicLink(CopyItem item, string target, bool overwrite)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (overwrite)
        {
            if (Directory.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) == 0)
            {
                throw new IOException(Messages.FolderExistsWithSameName);
            }

            File.Delete(target);
        }

        // Directory.Exists follows the link: it tells whether the link points to a folder
        if (Directory.Exists(item.SourcePath))
        {
            Directory.CreateSymbolicLink(target, item.LinkTarget!);
        }
        else
        {
            File.CreateSymbolicLink(target, item.LinkTarget!);
        }

        return (null, false, null);
    }

    private string? ApplyMetadata(string target, SourceMetadata metadata)
    {
        var problems = new List<string>();
        void Try(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                problems.Add($"{what}: {ex.Message}");
            }
        }

        if (Options.PreserveAttributes && !OperatingSystem.IsWindows() && metadata.UnixMode is { } mode)
        {
            Try(Messages.MetadataPermissions, () => File.SetUnixFileMode(target, mode));
        }

        if (Options.PreserveTimestamps)
        {
            Try(Messages.MetadataModifiedTime, () => File.SetLastWriteTimeUtc(target, metadata.LastWriteTimeUtc));
            if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            {
                Try(Messages.MetadataCreationTime, () => File.SetCreationTimeUtc(target, metadata.CreationTimeUtc));
            }
        }

        // Last, because read-only would block the changes above on Windows
        if (Options.PreserveAttributes && OperatingSystem.IsWindows())
        {
            const FileAttributes copied = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive;
            var attributes = metadata.Attributes & copied;
            Try(Messages.MetadataAttributes, () => File.SetAttributes(target, attributes == 0 ? FileAttributes.Normal : attributes));
        }

        return problems.Count == 0 ? null : string.Format(CultureInfo.CurrentCulture, Messages.MetadataNotCopied, string.Join("; ", problems));
    }

    private static FileCopyResult DeleteSource(CopyItem item, FileCopyResult result)
    {
        try
        {
            ClearReadOnly(item.SourcePath);
            File.Delete(item.SourcePath);
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return result with
            {
                Status = FileCopyStatus.Failed,
                Error = string.Format(CultureInfo.CurrentCulture, Messages.SourceNotDeleted, ex.Message),
            };
        }
    }

    private static FileCopyResult CreateItemResult(
        CopyItem item,
        string target,
        FileCopyStatus status,
        string? hash = null,
        bool verified = false,
        string? error = null,
        string? warning = null,
        int attempts = 0) => new()
        {
            SourcePath = item.SourcePath,
            DestinationPath = target,
            Kind = item.Kind,
            Length = item.Length,
            Status = status,
            Hash = hash,
            Verified = verified,
            Error = error,
            Warning = warning,
            Attempts = attempts,
        };

    /// <summary>Errors that may go away by trying again, such as a file locked by another program.</summary>
    private static bool IsTransient(Exception ex) =>
        ex is IOException and not (FileNotFoundException or DirectoryNotFoundException or PathTooLongException);

    /// <summary>True also for a symbolic link whose target does not exist.</summary>
    private static bool PathExists(string path)
    {
        if (Path.Exists(path))
        {
            return true;
        }

        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>"name (2).ext", "name (3).ext"... the first one that is free.</summary>
    internal static string GetAvailableName(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var n = 2; ; n++)
        {
            var candidate = Path.Combine(directory, $"{name} ({n}){extension}");
            if (!PathExists(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>Hidden temporary name in the same folder, so that the final rename is atomic.</summary>
    private static string GetTempPath(string target)
    {
        var random = Guid.NewGuid().ToString("N");
        var name = $".{Path.GetFileName(target)}.{random[..8]}{TempSuffix}";
        if (name.Length > 200)
        {
            name = $".{random}{TempSuffix}";
        }

        return Path.Combine(Path.GetDirectoryName(target)!, name);
    }

    private static void ClearReadOnly(string path)
    {
        if (OperatingSystem.IsWindows() && File.Exists(path))
        {
            var attributes = File.GetAttributes(path);
            if (attributes.HasFlag(FileAttributes.ReadOnly))
            {
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left behind: the hidden name and the suffix make it recognizable
        }
    }

    private sealed record SourceMetadata(
        long Length,
        DateTime LastWriteTimeUtc,
        DateTime CreationTimeUtc,
        FileAttributes Attributes,
        UnixFileMode? UnixMode)
    {
        public static SourceMetadata Read(FileInfo file) => new(
            file.Length,
            file.LastWriteTimeUtc,
            file.CreationTimeUtc,
            file.Attributes,
            OperatingSystem.IsWindows() ? null : file.UnixFileMode);
    }
}
