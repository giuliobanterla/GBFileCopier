using System.Diagnostics;

namespace GBFileCopier.Core.Copying;

public enum CopyPhase
{
    Scanning,
    Copying,
    Verifying,
    Finished,
}

/// <summary>Snapshot of a job's progress.</summary>
/// <param name="ProcessedBytes">Bytes copied plus bytes verified: verification counts as a second pass.</param>
/// <param name="TotalBytes">Total work in bytes (twice the data size when verifying).</param>
public sealed record CopyProgress(
    CopyPhase Phase,
    string? CurrentFile,
    long ProcessedBytes,
    long TotalBytes,
    int CompletedFiles,
    int TotalFiles,
    TimeSpan Elapsed)
{
    public double Fraction => TotalBytes > 0
        ? Math.Clamp((double)ProcessedBytes / TotalBytes, 0, 1)
        : TotalFiles > 0 ? (double)CompletedFiles / TotalFiles : 1;

    public double BytesPerSecond => Elapsed.TotalSeconds > 0 ? ProcessedBytes / Elapsed.TotalSeconds : 0;
}

/// <summary>Collects progress from the pipeline threads and reports it at most every <see cref="Interval"/>.</summary>
internal sealed class ProgressTracker(IProgress<CopyProgress>? progress, long totalBytes, int totalFiles)
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);

    private readonly Lock _lock = new();
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private TimeSpan? _lastReport;
    private long _processedBytes;
    private int _completedFiles;
    private CopyPhase _phase = CopyPhase.Copying;
    private string? _currentFile;

    public TimeSpan Elapsed => _stopwatch.Elapsed;

    public void StartFile(string path, CopyPhase phase)
    {
        lock (_lock)
        {
            _currentFile = path;
            _phase = phase;
        }

        Report(force: true);
    }

    public void SetPhase(CopyPhase phase)
    {
        lock (_lock)
        {
            _phase = phase;
        }

        Report(force: true);
    }

    public void AddBytes(long bytes)
    {
        Interlocked.Add(ref _processedBytes, bytes);
        Report(force: false);
    }

    public void CompleteFile()
    {
        Interlocked.Increment(ref _completedFiles);
        Report(force: true);
    }

    public void Finish()
    {
        lock (_lock)
        {
            _phase = CopyPhase.Finished;
            _currentFile = null;
        }

        Report(force: true);
    }

    private void Report(bool force)
    {
        if (progress is null)
        {
            return;
        }

        CopyProgress snapshot;
        lock (_lock)
        {
            var now = _stopwatch.Elapsed;
            if (!force && now - _lastReport < Interval) // false when never reported
            {
                return;
            }

            _lastReport = now;
            snapshot = new CopyProgress(
                _phase,
                _currentFile,
                Interlocked.Read(ref _processedBytes),
                totalBytes,
                Volatile.Read(ref _completedFiles),
                totalFiles,
                now);
        }

        progress.Report(snapshot);
    }
}
