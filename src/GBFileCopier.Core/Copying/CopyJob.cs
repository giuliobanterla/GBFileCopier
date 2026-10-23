using GBFileCopier.Core.Hashing;

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
    private int _started;

    public CopyJob(CopyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfZero(request.Sources.Count, nameof(request));
        request.Options.Validate();
        Request = request;
    }

    public CopyRequest Request { get; }

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

        results.AddRange(plan.Errors.Select(e => new FileCopyResult
        {
            SourcePath = e.SourcePath,
            DestinationPath = e.DestinationPath,
            Status = FileCopyStatus.Failed,
            Error = e.Message,
        }));

        var tracker = new ProgressTracker(progress, plan.TotalBytes * Passes, plan.Files.Count);
        CreateDirectories(plan, results);

        var cancelled = false;
        foreach (var item in plan.Files)
        {
            if (cancelled)
            {
                results.Add(CreateItemResult(item, item.DestinationPath, FileCopyStatus.Cancelled));
                continue;
            }

            try
            {
                await _pause.WaitIfPausedAsync(cancellationToken);
                results.Add(await ProcessItemAsync(item, tracker, conflictResolver, cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                results.Add(CreateItemResult(item, item.DestinationPath, FileCopyStatus.Cancelled));
            }

            tracker.CompleteFile();
        }

        if (!cancelled)
        {
            FinishDirectories(plan);
        }

        tracker.Finish();
        return CreateResult(results, tracker.Elapsed, cancelled);
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

    private static void CreateDirectories(CopyPlan plan, List<FileCopyResult> results)
    {
        foreach (var directory in plan.Directories)
        {
            try
            {
                Directory.CreateDirectory(directory.DestinationPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                results.Add(CreateItemResult(directory, directory.DestinationPath, FileCopyStatus.Failed, error: ex.Message));
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
                    return CreateItemResult(item, target, FileCopyStatus.Skipped, error: "The destination already exists");
            }
        }

        tracker.StartFile(item.SourcePath, CopyPhase.Copying);
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

            var (bytes, hash) = await FilePipeline.CopyAsync(
                item.SourcePath, temp, metadata.Length, Options, _pause, onBytes, cancellationToken);

            source.Refresh();
            if (bytes != metadata.Length || source.Length != metadata.Length || source.LastWriteTimeUtc != metadata.LastWriteTimeUtc)
            {
                throw new IOException("The source file changed while it was being copied");
            }

            var verified = false;
            if (Options.Verify)
            {
                tracker.SetPhase(CopyPhase.Verifying);
                var copyHash = await FilePipeline.HashFileAsync(
                    temp, Options.EffectiveHashAlgorithm, Options.BufferSize, _pause, onBytes, cancellationToken);
                if (!copyHash.AsSpan().SequenceEqual(hash))
                {
                    throw new IOException("Verification failed: the copy does not match the source");
                }

                verified = true;
            }

            if (overwrite)
            {
                ClearReadOnly(target);
            }

            File.Move(temp, target, overwrite);
            temp = null;

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
        if (overwrite)
        {
            if (Directory.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) == 0)
            {
                throw new IOException("A folder with the same name already exists");
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
            Try("permissions", () => File.SetUnixFileMode(target, mode));
        }

        if (Options.PreserveTimestamps)
        {
            Try("modified time", () => File.SetLastWriteTimeUtc(target, metadata.LastWriteTimeUtc));
            if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            {
                Try("creation time", () => File.SetCreationTimeUtc(target, metadata.CreationTimeUtc));
            }
        }

        // Last, because read-only would block the changes above on Windows
        if (Options.PreserveAttributes && OperatingSystem.IsWindows())
        {
            const FileAttributes copied = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive;
            var attributes = metadata.Attributes & copied;
            Try("attributes", () => File.SetAttributes(target, attributes == 0 ? FileAttributes.Normal : attributes));
        }

        return problems.Count == 0 ? null : "Not all metadata could be copied (" + string.Join("; ", problems) + ")";
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
                Error = $"Copied, but the source could not be deleted: {ex.Message}",
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
