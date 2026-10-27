using GBFileCopier.Core.Copying;
using GBFileCopier.Tests;
using Microsoft.Win32.SafeHandles;

namespace GBFileCopier.Core.Tests;

public sealed class NetworkFlushTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>The portable platform, counting the flushes; with <see cref="HasBarrier"/>, also offering write barriers.</summary>
    private sealed class CountingPlatform : IFileSystemPlatform
    {
        public int Flushes { get; private set; }

        public int Barriers { get; private set; }

        public bool HasBarrier { get; init; }

        public bool FlushFails { get; init; }

        public bool TryOrderWrites(SafeFileHandle handle)
        {
            if (!HasBarrier)
            {
                return false;
            }

            Barriers++;
            return true;
        }

        public string Name => "counting";

        public SafeFileHandle OpenForVerification(string path) => ManagedFileSystemPlatform.Instance.OpenForVerification(path);

        public void FlushToDisk(SafeFileHandle handle)
        {
            Flushes++;
            if (FlushFails)
            {
                throw new IOException("The drive did not answer");
            }

            ManagedFileSystemPlatform.Instance.FlushToDisk(handle);
        }

        public bool TryMoveWithinVolume(string sourcePath, string destinationPath, bool overwrite) => false;
    }

    private async Task<(CopyJob Job, int Flushes)> CopyThreeFilesAsync(bool onNetwork, CopyOptions options) =>
        await CopyThreeFilesAsync(new CountingPlatform(), onNetwork, options);

    private async Task<(CopyJob Job, int Flushes)> CopyThreeFilesAsync(CountingPlatform platform, bool onNetwork, CopyOptions options, CopyMode mode = CopyMode.Copy)
    {
        var request = new CopyRequest
        {
            Sources = [_temp.CreateFile("src/a.jpg"), _temp.CreateFile("src/b.jpg"), _temp.CreateFile("src/c.jpg")],
            Destination = _temp.Combine("dst"),
            Options = options,
            Mode = mode,
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
    public async Task WithABarrier_EachFileIsOrdered_AndTheDriveFlushedOnceAtTheEnd()
    {
        var platform = new CountingPlatform { HasBarrier = true };

        var (_, flushes) = await CopyThreeFilesAsync(platform, onNetwork: false, new CopyOptions());

        Assert.Equal(3, platform.Barriers);
        Assert.Equal(1, flushes);
        Assert.All(["a.jpg", "b.jpg", "c.jpg"], name => Assert.True(File.Exists(_temp.Combine("dst", name))));
    }

    [Fact]
    public async Task FinalFlushFailing_WarnsOnTheJob()
    {
        var platform = new CountingPlatform { HasBarrier = true, FlushFails = true };
        var request = new CopyRequest { Sources = [_temp.CreateFile("src/a.jpg")], Destination = _temp.Combine("dst") };

        var result = await new CopyJob(request, platform, destinationOnNetwork: false).RunAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.SucceededCount); // the copy itself is complete
        Assert.Contains("may not be on the disk yet", result.Warning, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveLinks_FollowsALinkedFolder()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "symbolic links need extra rights on Windows");
        var real = _temp.Combine("other-disk", "Photos");
        Directory.CreateDirectory(real);
        Directory.CreateSymbolicLink(_temp.Combine("Photos"), real);

        var resolved = CopyJob.ResolveLinks(_temp.Combine("Photos", "2026"));

        // Through the link to the real folder (the temporary folder may itself be under a link, /var → /private/var)
        Assert.EndsWith(Path.Combine("other-disk", "Photos", "2026"), resolved, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Move_KeepsAFullFlushPerFile()
    {
        // The source is deleted right after each copy, on a disk the destination's barrier does not order
        var platform = new CountingPlatform { HasBarrier = true };

        var (_, flushes) = await CopyThreeFilesAsync(platform, onNetwork: false, new CopyOptions(), CopyMode.Move);

        Assert.Equal(0, platform.Barriers);
        Assert.Equal(3, flushes);
    }

    [Fact]
    public async Task NoFlush_NeedsNoBarrierEither()
    {
        var platform = new CountingPlatform { HasBarrier = true };

        var (_, flushes) = await CopyThreeFilesAsync(platform, onNetwork: false, new CopyOptions { FlushToDisk = false });

        Assert.Equal((0, 0), (platform.Barriers, flushes));
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
