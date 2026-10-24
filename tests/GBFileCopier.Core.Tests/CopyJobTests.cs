using GBFileCopier.Core.Copying;
using GBFileCopier.Core.Hashing;
using GBFileCopier.Tests;

namespace GBFileCopier.Core.Tests;

public sealed class CopyJobTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string Destination => _temp.Combine("dst");

    private static Task<CopyJobResult> RunAsync(
        IReadOnlyList<string> sources,
        string destination,
        CopyOptions? options = null,
        CopyMode mode = CopyMode.Copy,
        ConflictResolver? resolver = null)
    {
        var job = new CopyJob(new CopyRequest
        {
            Sources = sources,
            Destination = destination,
            Mode = mode,
            Options = options ?? new CopyOptions { RetryDelay = TimeSpan.Zero },
        });
        return job.RunAsync(conflictResolver: resolver, cancellationToken: Token);
    }

    private static void AssertSameContent(string expected, string actual) =>
        Assert.Equal(File.ReadAllBytes(expected), File.ReadAllBytes(actual));

    private static string[] TempFilesIn(string folder) =>
        Directory.Exists(folder) ? Directory.GetFiles(folder, "*.gbcopy-tmp", SearchOption.AllDirectories) : [];

    [Fact]
    public async Task Copy_SingleFile_CopiesContentAndReportsHash()
    {
        var source = _temp.CreateFile("src/file.bin", 50_000);

        var result = await RunAsync([source], Destination);

        var file = Assert.Single(result.Files);
        Assert.Equal(FileCopyStatus.Succeeded, file.Status);
        AssertSameContent(source, Path.Combine(Destination, "file.bin"));
        Assert.Equal(StreamingHasher.ToHex(StreamingHasher.Compute(HashAlgorithmKind.XxHash3, File.ReadAllBytes(source))), file.Hash);
        Assert.True(result.IsSuccess);
        Assert.Equal(50_000, result.CopiedBytes);
        Assert.Empty(TempFilesIn(Destination));
    }

    [Fact]
    public async Task Copy_FileLargerThanBuffer_GoesThroughThePipelineIntact()
    {
        var source = _temp.CreateFile("src/big.bin", 1_000_003);
        var options = new CopyOptions { BufferSize = 4096, BufferCount = 2, Verify = true };

        var result = await RunAsync([source], Destination, options);

        Assert.True(Assert.Single(result.Files).Verified);
        AssertSameContent(source, Path.Combine(Destination, "big.bin"));
    }

    [Fact]
    public async Task Copy_EmptyFile_Works()
    {
        var source = _temp.CreateFile("src/empty.txt", 0);

        var result = await RunAsync([source], Destination, new CopyOptions { Verify = true });

        Assert.True(result.IsSuccess);
        Assert.Equal(0, new FileInfo(Path.Combine(Destination, "empty.txt")).Length);
    }

    [Fact]
    public async Task Copy_Folder_RecreatesTreeIncludingEmptyFolders()
    {
        _temp.CreateFile("src/a.txt");
        _temp.CreateFile("src/sub/b.txt");
        _temp.CreateFile("src/sub/deeper/c.txt");
        Directory.CreateDirectory(_temp.Combine("src", "empty"));

        var result = await RunAsync([_temp.Combine("src")], Destination);

        Assert.Equal(3, result.SucceededCount);
        AssertSameContent(_temp.Combine("src", "sub", "deeper", "c.txt"), Path.Combine(Destination, "src", "sub", "deeper", "c.txt"));
        Assert.True(Directory.Exists(Path.Combine(Destination, "src", "empty")));
    }

    [Fact]
    public async Task Copy_PreservesTimestamps()
    {
        var source = _temp.CreateFile("src/old.txt");
        var time = new DateTime(2020, 5, 17, 10, 30, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(source, time);
        Directory.SetLastWriteTimeUtc(_temp.Combine("src"), time);

        await RunAsync([_temp.Combine("src")], Destination);

        Assert.Equal(time, File.GetLastWriteTimeUtc(Path.Combine(Destination, "src", "old.txt")));
        Assert.Equal(time, Directory.GetLastWriteTimeUtc(Path.Combine(Destination, "src")));
    }

    [Fact]
    public async Task Copy_PreservesUnixPermissions()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix only");
        var source = _temp.CreateFile("src/private.txt");
        const UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(source, mode);
        }

        await RunAsync([source], Destination);

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(mode, File.GetUnixFileMode(Path.Combine(Destination, "private.txt")));
        }
    }

    [Fact]
    public async Task Copy_SymbolicLinkInsideFolder_IsCopiedAsLink()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating links on Windows needs developer mode or admin rights");
        _temp.CreateFile("src/target.txt");
        File.CreateSymbolicLink(_temp.Combine("src", "link.txt"), "target.txt");

        var result = await RunAsync([_temp.Combine("src")], Destination);

        Assert.True(result.IsSuccess);
        Assert.Equal("target.txt", new FileInfo(Path.Combine(Destination, "src", "link.txt")).LinkTarget);
    }

    [Theory]
    [InlineData(ConflictPolicy.Skip, "old", FileCopyStatus.Skipped)]
    [InlineData(ConflictPolicy.Overwrite, "new", FileCopyStatus.Succeeded)]
    [InlineData(ConflictPolicy.Ask, "old", FileCopyStatus.Skipped)] // no resolver: skip
    public async Task Conflict_FollowsPolicy(ConflictPolicy policy, string expectedText, FileCopyStatus expectedStatus)
    {
        var source = _temp.CreateFile("src/file.txt", text: "new");
        var existing = _temp.CreateFile("dst/file.txt", text: "old");

        var result = await RunAsync([source], Destination, new CopyOptions { ConflictPolicy = policy });

        Assert.Equal(expectedStatus, Assert.Single(result.Files).Status);
        Assert.Equal(expectedText, File.ReadAllText(existing));
    }

    [Fact]
    public async Task Conflict_KeepBoth_SavesWithNewName()
    {
        var source = _temp.CreateFile("src/file.txt", text: "new");
        _temp.CreateFile("dst/file.txt", text: "old");
        _temp.CreateFile("dst/file (2).txt", text: "older");

        var result = await RunAsync([source], Destination, new CopyOptions { ConflictPolicy = ConflictPolicy.KeepBoth });

        Assert.Equal(Path.Combine(Destination, "file (3).txt"), Assert.Single(result.Files).DestinationPath);
        Assert.Equal("new", File.ReadAllText(Path.Combine(Destination, "file (3).txt")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(Destination, "file.txt")));
    }

    [Theory]
    [InlineData(-1, "new")] // destination older: overwritten
    [InlineData(+1, "old")] // destination newer: kept
    public async Task Conflict_OverwriteIfNewer_ComparesModifiedTimes(int destinationAgeHours, string expectedText)
    {
        var source = _temp.CreateFile("src/file.txt", text: "new");
        var existing = _temp.CreateFile("dst/file.txt", text: "old");
        var now = DateTime.UtcNow;
        File.SetLastWriteTimeUtc(source, now);
        File.SetLastWriteTimeUtc(existing, now.AddHours(destinationAgeHours));

        await RunAsync([source], Destination, new CopyOptions { ConflictPolicy = ConflictPolicy.OverwriteIfNewer });

        Assert.Equal(expectedText, File.ReadAllText(existing));
    }

    [Fact]
    public async Task Conflict_Ask_UsesResolverAnswer()
    {
        var source = _temp.CreateFile("src/file.txt", text: "new");
        var existing = _temp.CreateFile("dst/file.txt", text: "old");
        FileConflict? asked = null;

        await RunAsync([source], Destination, new CopyOptions { ConflictPolicy = ConflictPolicy.Ask }, resolver: (conflict, _) =>
        {
            asked = conflict;
            return ValueTask.FromResult(ConflictAction.Overwrite);
        });

        Assert.Equal(existing, asked?.DestinationPath);
        Assert.Equal(3, asked?.DestinationLength);
        Assert.Equal("new", File.ReadAllText(existing));
    }

    [Fact]
    public async Task Overwrite_ReadOnlyDestination_Works()
    {
        var source = _temp.CreateFile("src/file.txt", text: "new");
        var existing = _temp.CreateFile("dst/file.txt", text: "old");
        File.SetAttributes(existing, FileAttributes.ReadOnly);

        var result = await RunAsync([source], Destination, new CopyOptions { ConflictPolicy = ConflictPolicy.Overwrite });

        Assert.True(result.IsSuccess, Assert.Single(result.Files).Error);
        Assert.Equal("new", File.ReadAllText(existing));
    }

    [Fact]
    public async Task Move_DeletesSourcesAfterCopy()
    {
        _temp.CreateFile("src/a.txt");
        _temp.CreateFile("src/sub/b.txt");
        var expected = File.ReadAllBytes(_temp.Combine("src", "sub", "b.txt"));

        var result = await RunAsync([_temp.Combine("src")], Destination, new CopyOptions { Verify = true }, CopyMode.Move);

        Assert.True(result.IsSuccess);
        Assert.False(Directory.Exists(_temp.Combine("src")));
        Assert.Equal(expected, File.ReadAllBytes(Path.Combine(Destination, "src", "sub", "b.txt")));
    }

    [Fact]
    public async Task Move_SkippedFile_StaysInSource()
    {
        _temp.CreateFile("src/a.txt", text: "moved");
        var kept = _temp.CreateFile("src/b.txt", text: "kept");
        _temp.CreateFile("dst/src/b.txt", text: "existing");

        var result = await RunAsync([_temp.Combine("src")], Destination, new CopyOptions { ConflictPolicy = ConflictPolicy.Skip }, CopyMode.Move);

        Assert.Equal(1, result.SkippedCount);
        Assert.False(File.Exists(_temp.Combine("src", "a.txt")));
        Assert.True(File.Exists(kept));
    }

    [Fact]
    public async Task LockedFile_FailsWithoutStoppingTheOthers()
    {
        var locked = _temp.CreateFile("src/1-locked.txt");
        var other = _temp.CreateFile("src/2-other.txt");
        using var lockHandle = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = await RunAsync([_temp.Combine("src")], Destination, new CopyOptions { RetryCount = 1, RetryDelay = TimeSpan.Zero });

        var failed = Assert.Single(result.Files, f => f.Status == FileCopyStatus.Failed);
        Assert.Equal(locked, failed.SourcePath);
        Assert.Equal(2, failed.Attempts);
        Assert.Equal(1, result.SucceededCount);
        AssertSameContent(other, Path.Combine(Destination, "src", "2-other.txt"));
        Assert.Empty(TempFilesIn(Destination));
    }

    [Fact]
    public async Task MissingSource_IsReportedAsFailure()
    {
        var present = _temp.CreateFile("src/present.txt");

        var result = await RunAsync([_temp.Combine("src", "missing.txt"), present], Destination);

        Assert.Equal(1, result.FailedCount);
        Assert.Equal(1, result.SucceededCount);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task DestinationInsideSource_IsRejected()
    {
        _temp.CreateFile("src/a.txt");

        var result = await RunAsync([_temp.Combine("src")], _temp.Combine("src", "backup"));

        Assert.Equal(FileCopyStatus.Failed, Assert.Single(result.Files).Status);
        Assert.False(Directory.Exists(_temp.Combine("src", "backup")));
    }

    [Fact]
    public async Task Cancel_StopsTheJobAndLeavesNoTemporaryFiles()
    {
        for (var i = 0; i < 20; i++)
        {
            _temp.CreateFile($"src/file{i:00}.bin", 200_000);
        }

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var options = new CopyOptions { BufferSize = 4096, BufferCount = 1 };
        var job = new CopyJob(new CopyRequest { Sources = [_temp.Combine("src")], Destination = Destination, Options = options });
        var progress = new SynchronousProgress(p =>
        {
            if (p.CompletedFiles >= 2)
            {
                cancellation.Cancel();
            }
        });

        var result = await job.RunAsync(progress, cancellationToken: cancellation.Token);

        Assert.True(result.WasCancelled);
        Assert.True(result.CancelledCount > 0);
        Assert.Equal(20, result.Files.Count);
        Assert.Empty(TempFilesIn(Destination));
    }

    [Fact]
    public async Task Pause_HoldsTheJobUntilResumed()
    {
        var source = _temp.CreateFile("src/file.bin", 100_000);
        var job = new CopyJob(new CopyRequest { Sources = [source], Destination = Destination });

        job.Pause();
        var run = job.RunAsync(cancellationToken: Token);
        await Task.Delay(300, Token);
        Assert.False(run.IsCompleted);
        Assert.False(File.Exists(Path.Combine(Destination, "file.bin")));

        job.Resume();
        var result = await run;

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Progress_EndsAtFullWorkload()
    {
        _temp.CreateFile("src/a.bin", 30_000);
        _temp.CreateFile("src/b.bin", 70_000);
        CopyProgress? last = null;
        var job = new CopyJob(new CopyRequest
        {
            Sources = [_temp.Combine("src")],
            Destination = Destination,
            Options = new CopyOptions { Verify = true },
        });

        await job.RunAsync(new SynchronousProgress(p => last = p), cancellationToken: Token);

        Assert.NotNull(last);
        Assert.Equal(CopyPhase.Finished, last.Phase);
        Assert.Equal(200_000, last.TotalBytes); // copy + verify
        Assert.Equal(last.TotalBytes, last.ProcessedBytes);
        Assert.Equal(2, last.CompletedFiles);
    }

    [Fact]
    public void GetAvailableName_SkipsExistingNames()
    {
        _temp.CreateFile("photo.jpg");
        _temp.CreateFile("photo (2).jpg");

        Assert.Equal(_temp.Combine("photo (3).jpg"), CopyJob.GetAvailableName(_temp.Combine("photo.jpg")));
    }

    [Fact]
    public async Task Job_RunsOnlyOnce()
    {
        var job = new CopyJob(new CopyRequest { Sources = [_temp.CreateFile("a.txt")], Destination = Destination });
        await job.RunAsync(cancellationToken: Token);

        Assert.Throws<InvalidOperationException>(() => { _ = job.RunAsync(cancellationToken: Token); });
    }

    /// <summary>Unlike Progress&lt;T&gt;, reports immediately on the calling thread.</summary>
    private sealed class SynchronousProgress(Action<CopyProgress> report) : IProgress<CopyProgress>
    {
        public void Report(CopyProgress value) => report(value);
    }
}
