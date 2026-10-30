using System.Diagnostics.CodeAnalysis;

namespace GBFileCopier.Core.Copying;

/// <summary>A job that works through a list of files with progress and per-file results: a copy, or the check of a checksum file.</summary>
public interface IFileJob
{
    /// <summary>The list of files is known; called once, from a thread pool thread.</summary>
    Action<IReadOnlyList<PlannedFile>>? Planned { get; set; }

    /// <summary>A file is done; called from thread pool threads, possibly at the same time.</summary>
    Action<FileCopyResult>? FileFinished { get; set; }

    void Pause();

    [SuppressMessage("Naming", "CA1716", Justification = "Same name as CopyJob.Resume, public since 0.1; the project is C# only")]
    void Resume();

    /// <summary>Runs the job on the thread pool. A job can run only once.</summary>
    /// <param name="progress">Called from thread pool threads, about ten times per second.</param>
    /// <param name="conflictResolver">Used by copies when the policy is <see cref="ConflictPolicy.Ask"/>.</param>
    /// <param name="cancellationToken">Stops the job; the result then has <see cref="CopyJobResult.WasCancelled"/> set.</param>
    Task<CopyJobResult> RunAsync(
        IProgress<CopyProgress>? progress = null,
        ConflictResolver? conflictResolver = null,
        CancellationToken cancellationToken = default);
}
