namespace GBFileCopier.Core.Copying;

/// <summary>
/// Transfer speed over the last few seconds, from the progress reports. The average since the
/// start reacts too slowly when the speed changes, as it often does on a network.
/// </summary>
public sealed class SpeedMeter(TimeSpan window)
{
    private readonly LinkedList<(TimeSpan Time, long Bytes)> _samples = new();

    public SpeedMeter()
        : this(TimeSpan.FromSeconds(5))
    {
    }

    /// <summary>Adds a report and returns the speed in bytes per second.</summary>
    /// <param name="elapsed">Time since the transfer started.</param>
    /// <param name="processedBytes">Bytes processed since the transfer started.</param>
    public double Add(TimeSpan elapsed, long processedBytes)
    {
        _samples.AddLast((elapsed, processedBytes));
        // Keep one sample at least a window old, so that the speed covers the whole window
        while (_samples.Count > 2 && elapsed - _samples.First!.Next!.Value.Time >= window)
        {
            _samples.RemoveFirst();
        }

        var (startTime, startBytes) = _samples.First!.Value;
        var seconds = (elapsed - startTime).TotalSeconds;
        if (seconds >= 1)
        {
            return Math.Max(0, (processedBytes - startBytes) / seconds);
        }

        // The first second: the average so far
        return elapsed.TotalSeconds > 0 ? processedBytes / elapsed.TotalSeconds : 0;
    }
}
