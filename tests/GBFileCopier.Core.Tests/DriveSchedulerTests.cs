using GBFileCopier.Core.Copying;

namespace GBFileCopier.Core.Tests;

public sealed class DriveSchedulerTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(100);

    [Fact]
    public async Task SameDrive_WaitsForTheEarlierJob()
    {
        var scheduler = new DriveScheduler();
        var first = await scheduler.WaitForTurnAsync("D:", Token);

        var second = scheduler.WaitForTurnAsync("D:", Token);
        await Task.Delay(Short, Token);
        Assert.False(second.IsCompleted);

        first.Dispose();
        (await second.WaitAsync(TimeSpan.FromSeconds(5), Token)).Dispose();
    }

    [Fact]
    public async Task OtherDrive_DoesNotWait()
    {
        var scheduler = new DriveScheduler();
        using var first = await scheduler.WaitForTurnAsync("D:", Token);

        var other = scheduler.WaitForTurnAsync("E:", Token);

        Assert.True(other.IsCompletedSuccessfully);
        (await other).Dispose();
    }

    [Fact]
    public async Task Turns_FollowTheOrderOfTheRequests()
    {
        var scheduler = new DriveScheduler();
        var order = new List<int>();
        var first = await scheduler.WaitForTurnAsync("D:", Token);
        var waiting = Enumerable.Range(1, 3).Select(async i =>
        {
            using var turn = await scheduler.WaitForTurnAsync("D:", Token);
            lock (order)
            {
                order.Add(i);
            }
        }).ToList();

        first.Dispose();
        await Task.WhenAll(waiting).WaitAsync(TimeSpan.FromSeconds(5), Token);

        Assert.Equal([1, 2, 3], order);
    }

    [Fact]
    public async Task CancelledWhileWaiting_DoesNotHoldTheNextJobs()
    {
        var scheduler = new DriveScheduler();
        var first = await scheduler.WaitForTurnAsync("D:", Token);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var cancelled = scheduler.WaitForTurnAsync("D:", cancel.Token);
        var third = scheduler.WaitForTurnAsync("D:", Token);

        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.False(third.IsCompleted); // the first job still holds the drive

        first.Dispose();
        (await third.WaitAsync(TimeSpan.FromSeconds(5), Token)).Dispose();
    }

    [Fact]
    public async Task DriveKeys_AreComparedLikeTheFileSystem()
    {
        var scheduler = new DriveScheduler();
        using var first = await scheduler.WaitForTurnAsync("/Volumes/Backup", Token);

        var other = scheduler.WaitForTurnAsync("/VOLUMES/BACKUP", Token);

        // Case-insensitive on Windows and macOS: the same drive
        Assert.Equal(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS(), !other.IsCompleted);
    }

    [Theory]
    [InlineData("/Volumes/Backup/Photos/2026", "/Volumes/Backup")]
    [InlineData("/Volumes/Backup", "/Volumes/Backup")]
    [InlineData("/Volumes/Backup2/Photos", "/Volumes/Backup2")]
    [InlineData("/Volumes/Backup 3/Photos", "/")]
    [InlineData("/Users/me/Documents", "/")]
    [InlineData("/System/Volumes/Data/Users/me", "/")] // same disk as /Users
    public void KeyFor_ChoosesTheLongestMountPoint(string path, string expected)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix paths");
        string[] mountPoints = ["/", "/Volumes/Backup/", "/Volumes/Backup2", "/System/Volumes/Data"];

        Assert.Equal(expected, DestinationDrive.KeyFor(path, mountPoints));
    }

    [Theory]
    [InlineData(@"D:\Backup\Photos", @"D:\")]
    [InlineData(@"d:\backup", @"D:\")]
    [InlineData(@"C:\Mount\Disk2\Photos", @"C:\Mount\Disk2")]
    [InlineData(@"\\nas\share\Photos", @"\\nas\share")] // not among the drives: its root
    [InlineData(@"\\?\D:\Backup", @"D:\")]
    [InlineData(@"\\?\UNC\nas\share\Photos", @"\\nas\share")]
    public void KeyFor_OnWindows(string path, string expected)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows paths");
        string[] mountPoints = [@"C:\", @"D:\", @"C:\Mount\Disk2\"];

        Assert.Equal(expected, DestinationDrive.KeyFor(path, mountPoints), DestinationDrive.KeyComparer);
    }

    [Fact]
    public void KeyFor_FolderThatDoesNotExistYet_UsesTheRealDrives()
    {
        var path = Path.Combine(Path.GetTempPath(), "gbcopy-" + Guid.NewGuid().ToString("N"), "new");

        var key = DestinationDrive.KeyFor(path);

        Assert.StartsWith(key.TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);
    }
}
