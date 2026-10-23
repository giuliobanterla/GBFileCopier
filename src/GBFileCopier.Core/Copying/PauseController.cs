namespace GBFileCopier.Core.Copying;

/// <summary>Lets the copy pipeline stop between blocks until it is resumed.</summary>
public sealed class PauseController
{
    private readonly Lock _lock = new();
    private TaskCompletionSource? _resumed;

    public bool IsPaused
    {
        get
        {
            lock (_lock)
            {
                return _resumed is not null;
            }
        }
    }

    public void Pause()
    {
        lock (_lock)
        {
            _resumed ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void Resume()
    {
        TaskCompletionSource? resumed;
        lock (_lock)
        {
            resumed = _resumed;
            _resumed = null;
        }

        resumed?.TrySetResult();
    }

    public Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        Task? resumed;
        lock (_lock)
        {
            resumed = _resumed?.Task;
        }

        return resumed is null ? Task.CompletedTask : resumed.WaitAsync(cancellationToken);
    }
}
