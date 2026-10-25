using GBFileCopier.Core.Copying;
using GBFileCopier.Tests;
using Microsoft.Win32.SafeHandles;

namespace GBFileCopier.Core.Tests;

public sealed class NetworkFlushTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>The portable platform, counting the flushes.</summary>
    private sealed class CountingPlatform : IFileSystemPlatform
    {
        public int Flushes { get; private set; }

        public string Name => "counting";

        public SafeFileHandle OpenForVerification(string path) => ManagedFileSystemPlatform.Instance.OpenForVerification(path);

        public void FlushToDisk(SafeFileHandle handle)
        {
            Flushes++;
            ManagedFileSystemPlatform.Instance.FlushToDisk(handle);
        }

        public bool TryMoveWithinVolume(string sourcePath, string destinationPath, bool overwrite) => false;
    }

    private async Task<(CopyJob Job, int Flushes)> CopyThreeFilesAsync(bool onNetwork, CopyOptions options)
    {
        var platform = new CountingPlatform();
        var request = new CopyRequest
        {
            Sources = [_temp.CreateFile("src/a.jpg"), _temp.CreateFile("src/b.jpg"), _temp.CreateFile("src/c.jpg")],
            Destination = _temp.Combine("dst"),
            Options = options,
        };
        var job = new CopyJob(request, platform, onNetwork);
        var result = await job.RunAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(3, result.SucceededCount);
        return (job, platform.Flushes);
    }

    [Fact]
    public async Task LocalDestination_FlushesEachFile()
    {
        var (job, flushes) = await CopyThreeFilesAsync(onNetwork: false, new CopyOptions());

        Assert.True(job.FlushesEachFile);
        Assert.Equal(3, flushes);
    }

    [Fact]
    public async Task NetworkShare_DoesNotFlushEachFile()
    {
        var (job, flushes) = await CopyThreeFilesAsync(onNetwork: true, new CopyOptions());

        Assert.False(job.FlushesEachFile);
        Assert.Equal(0, flushes);
    }

    [Fact]
    public async Task NetworkShare_FlushesWhenAskedFor()
    {
        var (_, flushes) = await CopyThreeFilesAsync(onNetwork: true, new CopyOptions { FlushOnNetworkShares = true });

        Assert.Equal(3, flushes);
    }

    [Fact]
    public async Task NoFlush_IsNeverOverridden()
    {
        var (_, flushes) = await CopyThreeFilesAsync(onNetwork: false, new CopyOptions { FlushToDisk = false, FlushOnNetworkShares = true });

        Assert.Equal(0, flushes);
    }

    [Fact]
    public void LocalTempFolder_IsNotANetworkShare() => Assert.False(DestinationDrive.IsNetwork(_temp.Combine("dst")));

    [Fact]
    public void UncPath_IsANetworkShare()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows paths");
        Assert.True(DestinationDrive.IsNetwork(@"\\nas\share\Photos"));
    }
}
