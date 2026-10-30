using System.Globalization;
using System.Text;
using GBFileCopier.Core.Hashing;
using GBFileCopier.Core.Resources;
using Microsoft.Win32.SafeHandles;

namespace GBFileCopier.Core.Copying;

/// <summary>
/// One copy or move operation. Each file is written to a temporary name next to its
/// destination, checked, and only then renamed into place: an interrupted or failed copy
/// never leaves a partial file and never damages an existing one. Errors on one file do not
/// stop the others; they are collected in the result.
/// </summary>
public sealed class CopyJob : IFileJob
{
    private const string TempSuffix = ".gbcopy-tmp";

    private readonly PauseController _pause = new();
    private readonly IFileSystemPlatform _platform;
    private readonly CopyOptions _pipelineOptions;

    // Per drive, the newest files written with only a write barrier: one per drive is flushed at the end.
    // The destination tree can reach other drives through links or mount points: they are flushed too.
    private readonly Dictionary<string, List<string>> _awaitingFlush = new(DestinationDrive.KeyComparer);
    private readonly Dictionary<string, string> _driveOfFolder = new(DestinationDrive.KeyComparer);
    private string? _flushWarning;

    // Names that "keep both" must not take: every planned destination, and the names already given.
    // Only touched while holding the conflict gate.
    private HashSet<string> _reservedNames = new(FileNameComparer.Instance);
    private readonly Lock _flushLock = new();

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
    /// cancelled), also for the problems found while scanning. Called on thread pool threads, several
    /// at once and not in plan order when small files are copied in parallel; a source planned twice
    /// is always copied in plan order.
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
        // Results of the planned files go into their slot, so that the report keeps the plan's order
        // whatever order the parallel copies finish in; the others (scan problems, folders) come first
        var results = new List<FileCopyResult>();
        FileCopyResult?[] fileResults = [];
        var resultLock = new Lock();
        void Add(FileCopyResult result, int slot = -1)
        {
            lock (resultLock)
            {
                if (slot >= 0)
                {
                    fileResults[slot] = result;
                }
                else
                {
                    results.Add(result);
                }
            }

            FileFinished?.Invoke(result);
        }

        List<FileCopyResult> AllResults()
        {
            lock (resultLock)
            {
                return [.. results, .. fileResults.OfType<FileCopyResult>()];
            }
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

        fileResults = new FileCopyResult?[plan.Files.Count];
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
        CreateDirectories(plan, result => Add(result));

        // Conflicts are resolved one at a time, also with files copied in parallel: one dialog at a
        // time, and an answer "for all" applies to the next ones
        using var conflictGate = new SemaphoreSlim(1, 1);
        var cancelled = 0;
        async Task CopyOneAsync(int slot)
        {
            var item = plan.Files[slot];
            // Also renames and links, which never wait on the token: a cancelled job stops there too
            if (Volatile.Read(ref cancelled) != 0 || cancellationToken.IsCancellationRequested)
            {
                Volatile.Write(ref cancelled, 1);
                Add(CreateItemResult(item, item.DestinationPath, FileCopyStatus.Cancelled), slot);
                return;
            }

            try
            {
                await _pause.WaitIfPausedAsync(cancellationToken);
                Add(await ProcessItemAsync(item, tracker, conflictResolver, conflictGate, cancellationToken), slot);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Volatile.Write(ref cancelled, 1);
                Add(CreateItemResult(item, item.DestinationPath, FileCopyStatus.Cancelled), slot);
            }

            tracker.CompleteFile();
        }

        try
        {
            // Runs of small files go in parallel, each big file alone, in the plan's order
            var parallel = new ParallelOptions { MaxDegreeOfParallelism = Options.ParallelFiles };
            var oneAtATime = OneAtATime(plan);
            _reservedNames = new HashSet<string>(plan.Files.Select(f => f.DestinationPath), FileNameComparer.Instance);
            var run = new List<int>();
            for (var slot = 0; slot <= plan.Files.Count; slot++)
            {
                var item = slot < plan.Files.Count ? plan.Files[slot] : null;
                if (item is not null && Options.ParallelFiles > 1 && IsSmall(item) && !oneAtATime.Contains(slot))
                {
                    run.Add(slot);
                    continue;
                }

                if (run.Count > 0)
                {
                    await Parallel.ForEachAsync(run, parallel, async (index, _) => await CopyOneAsync(index));
                    run.Clear();
                }

                if (item is not null)
                {
                    await CopyOneAsync(slot);
                }
            }

            if (cancelled == 0)
            {
                FinishDirectories(plan);
            }
        }
        finally
        {
            // Also when something fails on the way: what was copied is forced to disk
            FlushAwaitingFiles();
        }

        var all = AllResults();
        string? checksumFile = null;
        if (Options.WriteChecksumFile && cancelled == 0 && all.TrueForAll(r => r.Status != FileCopyStatus.Failed))
        {
            checksumFile = await WriteChecksumFileAsync(fileResults, tracker, cancellationToken);
        }

        tracker.Finish();
        return CreateResult(all, tracker.Elapsed, cancelled != 0) with { ChecksumFilePath = checksumFile };
    }

    /// <summary>
    /// Saves the checksums of the files copied next to what was copied: "Photos.sha256" for a folder
    /// Photos, "IMG.jpg.sha256" for a single file, "checksums.sha256" for several sources; a free name
    /// if it exists. The paths in it are relative to the destination folder. Problems become the job's warning.
    /// </summary>
    private async Task<string?> WriteChecksumFileAsync(FileCopyResult?[] fileResults, ProgressTracker tracker, CancellationToken cancellationToken)
    {
        var algorithm = Options.EffectiveHashAlgorithm;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Request.Destination));
        string? target = null;
        try
        {
            // Moved by a rename on the same volume: the data was never read, so it is hashed now, with progress
            tracker.AddToTotal(fileResults.Where(r => r is { Status: FileCopyStatus.Succeeded, Kind: CopyItemKind.File, Hash: null }).Sum(r => r!.Length));
            var entries = new List<ChecksumEntry>();
            for (var slot = 0; slot < fileResults.Length; slot++)
            {
                if (fileResults[slot] is not { Status: FileCopyStatus.Succeeded, Kind: CopyItemKind.File } result)
                {
                    continue;
                }

                var hash = result.Hash;
                if (hash is null)
                {
                    tracker.StartFile(result.DestinationPath, CopyPhase.Verifying);
                    hash = StreamingHasher.ToHex(await FilePipeline.HashFileAsync(
                        result.DestinationPath, algorithm, Options.BufferSize, _platform, _pause, tracker.AddBytes, cancellationToken));
                }

                entries.Add(new ChecksumEntry(Path.GetRelativePath(root, result.DestinationPath).Replace(Path.DirectorySeparatorChar, '/'), hash));
            }

            if (entries.Count == 0)
            {
                return null;
            }

            var name = Request.Sources.Count == 1
                ? Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Request.Sources[0])))
                : "checksums";
            target = Path.Combine(root, name + ChecksumFile.ExtensionFor(algorithm));
            if (PathExists(target))
            {
                target = GetAvailableName(target);
            }

            // Written aside and renamed: never a half-written checksum file
            var temp = GetTempPath(target);
            try
            {
                await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(ChecksumFile.Format(algorithm, entries));
                    await stream.WriteAsync(bytes, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                    if (FlushesEachFile)
                    {
                        _platform.FlushToDisk(stream.SafeFileHandle);
                    }
                }

                File.Move(temp, target);
            }
            finally
            {
                TryDeleteFile(temp);
            }

            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var warning = string.Format(CultureInfo.CurrentCulture, Messages.ChecksumFileNotWritten, ex.Message);
            _flushWarning = _flushWarning is null ? warning : _flushWarning + "; " + warning;
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private bool IsSmall(CopyItem item) => item.Kind == CopyItemKind.SymbolicLink || item.Length < Options.SmallFileLimit;

    /// <summary>
    /// The files that must not be copied in parallel: a destination planned more than once (the
    /// second one must find the first and go through the conflict policy), and a source planned more
    /// than once (a file and its folder too: its copies keep the plan's order, and a move has only
    /// one source to delete).
    /// </summary>
    private static HashSet<int> OneAtATime(CopyPlan plan)
    {
        var destinations = plan.Files.GroupBy(f => f.DestinationPath, FileNameComparer.Instance).Where(g => g.Count() > 1).Select(g => g.Key);
        var sources = plan.Files.GroupBy(f => f.SourcePath, FileNameComparer.Instance).Where(g => g.Count() > 1).Select(g => g.Key);
        var shared = new HashSet<string>(destinations.Concat(sources), FileNameComparer.Instance);
        return [.. Enumerable.Range(0, plan.Files.Count).Where(i => shared.Contains(plan.Files[i].DestinationPath) || shared.Contains(plan.Files[i].SourcePath))];
    }

    /// <summary>Keeps the target among the newest few of its drive, the drive found through any links.</summary>
    private void RememberForFlush(string target)
    {
        lock (_flushLock)
        {
            RememberForFlushLocked(target);
        }
    }

    private void RememberForFlushLocked(string target)
    {
        var folder = Path.GetDirectoryName(target)!;
        if (!_driveOfFolder.TryGetValue(folder, out var drive))
        {
            drive = DestinationDrive.KeyFor(ResolveLinks(folder));
            _driveOfFolder[folder] = drive;
        }

        if (!_awaitingFlush.TryGetValue(drive, out var files))
        {
            _awaitingFlush[drive] = files = [];
        }

        files.Add(target);
        if (files.Count > 4)
        {
            files.RemoveAt(0); // older ones are only a fallback if the newest are deleted meanwhile
        }
    }

    /// <summary>The path with every symbolic link along it followed: where the files really go.</summary>
    internal static string ResolveLinks(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? string.Empty;
        var resolved = root;
        foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            resolved = Path.Combine(resolved, part);
            try
            {
                if (new DirectoryInfo(resolved) is { LinkTarget: not null } link && link.ResolveLinkTarget(returnFinalTarget: true) is { } target)
                {
                    resolved = target.FullName;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Keep the path as it is
            }
        }

        return resolved;
    }

    /// <summary>
    /// The end of the job, also when cancelled or failed: one full flush per drive makes it write its
    /// whole cache, so the files written with only a barrier are on permanent storage before the job
    /// reports. Through the newest file of that drive that can still be opened; if the flush itself
    /// fails, the job gets a warning.
    /// </summary>
    private void FlushAwaitingFiles()
    {
        foreach (var (drive, files) in _awaitingFlush)
        {
            for (var index = files.Count - 1; index >= 0; index--)
            {
                SafeFileHandle handle;
                try
                {
                    handle = File.OpenHandle(files[index], FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue; // deleted or moved meanwhile: an older one
                }

                using (handle)
                {
                    try
                    {
                        _platform.FlushToDisk(handle);
                    }
                    catch (IOException ex)
                    {
                        _flushWarning = string.Format(CultureInfo.CurrentCulture, Messages.FilesNotFlushed, drive, ex.Message);
                    }
                }

                break;
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
        Warning = _flushWarning,
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
        SemaphoreSlim conflictGate,
        CancellationToken cancellationToken)
    {
        var workload = item.Length * Passes;
        var target = item.DestinationPath;
        var overwrite = false;

        if (PathExists(target))
        {
            ConflictAction action;
            await conflictGate.WaitAsync(cancellationToken);
            try
            {
                action = await ResolveConflictAsync(item, target, conflictResolver, cancellationToken);
                if (action == ConflictAction.KeepBoth)
                {
                    target = GetAvailableName(target, _reservedNames); // chosen while no other file is choosing one
                }
            }
            finally
            {
                conflictGate.Release();
            }

            switch (action)
            {
                case ConflictAction.Overwrite:
                    overwrite = true;
                    break;
                case ConflictAction.KeepBoth:
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
                RememberForFlush(target);
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
    internal static string GetAvailableName(string path, HashSet<string>? reserved = null)
    {
        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var n = 2; ; n++)
        {
            // Not existing, nor planned for another file being copied (maybe at this very moment)
            var candidate = Path.Combine(directory, $"{name} ({n}){extension}");
            if (!PathExists(candidate) && reserved?.Contains(candidate) != true)
            {
                reserved?.Add(candidate);
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
