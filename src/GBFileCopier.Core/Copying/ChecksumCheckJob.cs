using System.Globalization;
using GBFileCopier.Core.Hashing;
using GBFileCopier.Core.Resources;

namespace GBFileCopier.Core.Copying;

/// <summary>
/// Reads back the files listed in a checksum file and compares their checksums. Each file is
/// reported like a copy: succeeded and verified, or failed (different, missing, unreadable).
/// </summary>
public sealed class ChecksumCheckJob : IFileJob
{
    private const int BufferSize = 1024 * 1024;

    private readonly PauseController _pause = new();
    private readonly IFileSystemPlatform _platform;
    private readonly HashAlgorithmKind? _algorithm;
    private int _started;

    /// <param name="checksumFilePath">The .sha256, .md5, .sfv… file; the paths in it are relative to its folder.</param>
    /// <param name="platform">Reads bypassing the cache where possible.</param>
    /// <param name="algorithm">Overrides the algorithm given by the extension or the file.</param>
    public ChecksumCheckJob(string checksumFilePath, IFileSystemPlatform? platform = null, HashAlgorithmKind? algorithm = null)
    {
        ChecksumFilePath = Path.GetFullPath(checksumFilePath);
        _platform = platform ?? ManagedFileSystemPlatform.Instance;
        _algorithm = algorithm;
    }

    public string ChecksumFilePath { get; }

    public Action<IReadOnlyList<PlannedFile>>? Planned { get; set; }

    public Action<FileCopyResult>? FileFinished { get; set; }

    public void Pause() => _pause.Pause();

    public void Resume() => _pause.Resume();

    public Task<CopyJobResult> RunAsync(
        IProgress<CopyProgress>? progress = null,
        ConflictResolver? conflictResolver = null,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("A job can run only once");
        }

        return Task.Run(() => RunCoreAsync(progress, cancellationToken), CancellationToken.None);
    }

    private async Task<CopyJobResult> RunCoreAsync(IProgress<CopyProgress>? progress, CancellationToken cancellationToken)
    {
        var results = new List<FileCopyResult>();
        void Add(FileCopyResult result)
        {
            results.Add(result);
            FileFinished?.Invoke(result);
        }

        progress?.Report(new CopyProgress(CopyPhase.Scanning, null, 0, 0, 0, 0, TimeSpan.Zero));

        ChecksumList list;
        try
        {
            list = ChecksumFile.Read(ChecksumFilePath, _algorithm);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Add(new FileCopyResult { SourcePath = ChecksumFilePath, DestinationPath = ChecksumFilePath, Status = FileCopyStatus.Failed, Error = ex.Message });
            return CreateResult(results, list: null, TimeSpan.Zero, wasCancelled: false);
        }

        var folder = Path.GetDirectoryName(ChecksumFilePath)!;
        var files = list.Entries.Select(e => (Entry: e, Path: FullPath(folder, e.Path))).ToList();
        var lengths = files.Select(f => LengthOf(f.Path)).ToList();
        Planned?.Invoke([.. files.Select((f, i) => new PlannedFile(CopyItemKind.File, f.Path, f.Path, lengths[i]))]);

        var tracker = new ProgressTracker(progress, lengths.Sum(), files.Count);
        var cancelled = false;
        for (var i = 0; i < files.Count; i++)
        {
            var (entry, path) = files[i];
            var result = new FileCopyResult { SourcePath = path, DestinationPath = path, Kind = CopyItemKind.File, Length = lengths[i] };
            if (cancelled || cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                Add(result with { Status = FileCopyStatus.Cancelled });
                continue;
            }

            tracker.StartFile(path, CopyPhase.Verifying);
            try
            {
                await _pause.WaitIfPausedAsync(cancellationToken);
                if (!File.Exists(path))
                {
                    Add(result with { Status = FileCopyStatus.Failed, Error = Messages.ChecksumFileMissing });
                }
                else
                {
                    var hash = StreamingHasher.ToHex(await FilePipeline.HashFileAsync(
                        path, list.Algorithm, BufferSize, _platform, _pause, tracker.AddBytes, cancellationToken));
                    Add(string.Equals(hash, entry.Hash, StringComparison.OrdinalIgnoreCase)
                        ? result with { Status = FileCopyStatus.Succeeded, Hash = hash, Verified = true, Attempts = 1 }
                        : result with
                        {
                            Status = FileCopyStatus.Failed,
                            Hash = hash,
                            Attempts = 1,
                            Error = string.Format(CultureInfo.CurrentCulture, Messages.ChecksumMismatch, entry.Hash, hash),
                        });
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                Add(result with { Status = FileCopyStatus.Cancelled });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Add(result with { Status = FileCopyStatus.Failed, Error = ex.Message, Attempts = 1 });
            }

            tracker.CompleteFile();
        }

        tracker.Finish();
        return CreateResult(results, list, tracker.Elapsed, cancelled);
    }

    private static CopyJobResult CreateResult(List<FileCopyResult> results, ChecksumList? list, TimeSpan elapsed, bool wasCancelled) => new()
    {
        Files = results,
        HashAlgorithm = list?.Algorithm ?? HashAlgorithmKind.None,
        Mode = CopyMode.Copy,
        Elapsed = elapsed,
        WasCancelled = wasCancelled,
    };

    /// <summary>A path of the list, relative to the checksum file's folder unless absolute; '/' and, on Windows, '\' separate folders.</summary>
    internal static string FullPath(string folder, string path) =>
        Path.GetFullPath(Path.Combine(folder, path.Replace('/', Path.DirectorySeparatorChar)));

    private static long LengthOf(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? file.Length : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return 0;
        }
    }
}
