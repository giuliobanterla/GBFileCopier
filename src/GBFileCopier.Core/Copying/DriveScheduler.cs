namespace GBFileCopier.Core.Copying;

/// <summary>
/// Lets one job at a time write to each drive: jobs to the same drive wait for their turn, in the
/// order they asked; jobs to different drives run at the same time. Two copies to one disk at
/// once are slower than one after the other, because the disk keeps seeking between them.
/// </summary>
public sealed class DriveScheduler
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Task> _lastInLine = new(DestinationDrive.KeyComparer);

    /// <summary>
    /// Waits until no earlier job holds <paramref name="driveKey"/>. Dispose the result when the
    /// job is over, to let the next one start.
    /// </summary>
    /// <param name="driveKey">From <see cref="DestinationDrive.KeyFor"/>.</param>
    /// <exception cref="OperationCanceledException">Cancelled while waiting; the turn is given up and the jobs after it are not held.</exception>
    public async Task<IDisposable> WaitForTurnAsync(string driveKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(driveKey);
        var turn = new Turn();
        Task previous;
        lock (_lock)
        {
            previous = _lastInLine.GetValueOrDefault(driveKey, Task.CompletedTask);
            _lastInLine[driveKey] = turn.Done;
        }

        try
        {
            await previous.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The jobs after this one wait for its turn: hand it on when the previous one ends
            _ = previous.ContinueWith(_ => turn.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw;
        }

        return turn;
    }

    private sealed class Turn : IDisposable
    {
        private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Done => _done.Task;

        public void Dispose() => _done.TrySetResult();
    }
}
