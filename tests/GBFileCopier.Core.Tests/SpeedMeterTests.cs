using GBFileCopier.Core.Copying;

namespace GBFileCopier.Core.Tests;

public sealed class SpeedMeterTests
{
    private static TimeSpan Seconds(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void SteadyTransfer_GivesItsSpeed()
    {
        var meter = new SpeedMeter();
        double speed = 0;
        for (var t = 0.1; t <= 10; t += 0.1)
        {
            speed = meter.Add(Seconds(t), (long)(t * 1_000_000));
        }

        Assert.InRange(speed, 990_000, 1_010_000);
    }

    [Fact]
    public void SpeedChange_ShowsWithinTheWindow()
    {
        var meter = new SpeedMeter(Seconds(5));
        long bytes = 0;
        double speed = 0;
        for (var i = 1; i <= 200; i++) // 10 s fast, then 10 s at a tenth of the speed
        {
            bytes += i <= 100 ? 10_000_000 : 1_000_000;
            speed = meter.Add(Seconds(i * 0.1), bytes);
        }

        // The average since the start would still be 55 MB/s
        Assert.InRange(speed, 9_000_000, 11_000_000);
    }

    [Fact]
    public void FirstSecond_UsesTheAverage()
    {
        var meter = new SpeedMeter();

        Assert.Equal(0, meter.Add(TimeSpan.Zero, 0));
        Assert.Equal(1_000_000, meter.Add(Seconds(0.5), 500_000), 3);
    }

    [Fact]
    public void Pause_SlowsDownToZero()
    {
        var meter = new SpeedMeter(Seconds(5));
        meter.Add(Seconds(1), 1_000_000);
        meter.Add(Seconds(2), 2_000_000);

        Assert.Equal(0, meter.Add(Seconds(20), 2_000_000));
    }
}
