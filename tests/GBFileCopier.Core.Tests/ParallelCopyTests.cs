using GBFileCopier.Core.Copying;
using GBFileCopier.Tests;
using Microsoft.Win32.SafeHandles;

namespace GBFileCopier.Core.Tests;

public sealed class ParallelCopyTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>The portable platform, with a slow flush that counts how many files are being flushed at once.</summary>
    private sealed class SlowFlushPlatform : IFileSystemPlatform
    {
        private int _current;
        private int _most;

        public int MostAtOnce => Volatile.Read(ref _most);

        public string Name => "slow flush";

        public SafeFileHandle OpenForVerification(string path) => ManagedFileSystemPlatform.Instance.OpenForVerification(path);

        public void FlushToDisk(SafeFileHandle handle)
        {
            var now = Interlocked.Increment(ref _current);
            int most;
            while (now > (most = Volatile.Read(ref _most)) && Interlocked.CompareExchange(ref _most, now, most) != most)
            {
            }

            Thread.Sleep(40);
            Interlocked.Decrement(ref _current);
            ManagedFileSystemPlatform.Instance.FlushToDisk(handle);
        }

        public bool TryMoveWithinVolume(string sourcePath, string destinationPath, bool overwrite) => false;
    }

    private List<string> CreateFiles(int count, int length = 1000) =>
        [.. Enumerable.Range(0, count).Select(i => _temp.CreateFile($"src/f{i:00}.bin", length))];

    private async Task<(CopyJobResult Result, SlowFlushPlatform Platform)> CopyAsync(IReadOnlyList<string> sources, CopyOptions options, ConflictResolver? resolver = null)
    {
        var platform = new SlowFlushPlatform();
        var request = new CopyRequest { Sources = sources, Destination = _temp.Combine("dst"), Options = options };
        var result = await new CopyJob(request, platform, destinationOnNetwork: false).RunAsync(conflictResolver: resolver, cancellationToken: Token);
        return (result, platform);
    }

    [Fact]
    public async Task SmallFiles_AreCopiedSeveralAtOnce()
    {
        var (result, platform) = await CopyAsync(CreateFiles(12), new CopyOptions { ParallelFiles = 4 });

        Assert.Equal(12, result.SucceededCount);
        Assert.InRange(platform.MostAtOnce, 2, 4);
    }

    [Fact]
    public async Task ParallelFilesOne_CopiesOneAtATime()
    {
        var (result, platform) = await CopyAsync(CreateFiles(6), new CopyOptions { ParallelFiles = 1 });

        Assert.Equal(6, result.SucceededCount);
        Assert.Equal(1, platform.MostAtOnce);
    }

    [Fact]
    public async Task BigFiles_AreCopiedOneAtATime()
    {
        var (result, platform) = await CopyAsync(CreateFiles(6, length: 5000), new CopyOptions { SmallFileLimit = 4096 });

        Assert.Equal(6, result.SucceededCount);
        Assert.Equal(1, platform.MostAtOnce);
    }

    [Fact]
    public async Task Results_KeepThePlanOrder()
    {
        var sources = CreateFiles(20);

        var (result, _) = await CopyAsync(sources, new CopyOptions());

        Assert.Equal(sources, result.Files.Select(f => f.SourcePath));
        Assert.All(sources, s => Assert.Equal(File.ReadAllBytes(s), File.ReadAllBytes(Path.Combine(_temp.Combine("dst"), Path.GetFileName(s)))));
    }

    [Fact]
    public async Task SameDestinationTwice_TheSecondMeetsTheFirst()
    {
        // The file alone and inside its folder: both go to dst/a.bin
        var file = _temp.CreateFile("src/a.bin");
        var others = CreateFiles(6);

        var (result, _) = await CopyAsync([file, .. others, file], new CopyOptions { ConflictPolicy = ConflictPolicy.Skip });

        var twice = result.Files.Where(f => f.SourcePath == file).ToList();
        Assert.Equal([FileCopyStatus.Succeeded, FileCopyStatus.Skipped], twice.Select(f => f.Status)); // not a failed rename
    }

    [Fact]
    public async Task KeepBoth_DoesNotTakeTheNameOfAnotherFileInTheJob()
    {
        // dst has IMG.jpg; the job copies IMG.jpg (kept as a new name) and a file really called "IMG (2).jpg"
        _temp.CreateFile("dst/IMG.jpg", text: "old");
        var sources = new List<string> { _temp.CreateFile("src/IMG.jpg", text: "new") };
        sources.AddRange(Enumerable.Range(0, 6).Select(i => _temp.CreateFile($"src/other{i}.jpg")));
        sources.Add(_temp.CreateFile("src/IMG (2).jpg", text: "literal"));

        var (result, _) = await CopyAsync(sources, new CopyOptions { ConflictPolicy = ConflictPolicy.KeepBoth });

        Assert.Equal(sources.Count, result.SucceededCount);
        Assert.Equal("old", File.ReadAllText(_temp.Combine("dst", "IMG.jpg")));
        Assert.Equal("literal", File.ReadAllText(_temp.Combine("dst", "IMG (2).jpg")));
        Assert.Equal("new", File.ReadAllText(_temp.Combine("dst", "IMG (3).jpg"))); // the next free name, not a planned one
    }

    [Fact]
    public async Task Conflicts_AreAskedOneAtATime()
    {
        var sources = CreateFiles(8);
        foreach (var source in sources)
        {
            _temp.CreateFile(Path.Combine("dst", Path.GetFileName(source)), text: "old");
        }

        var current = 0;
        var most = 0;
        var asked = 0;
        async ValueTask<ConflictAction> Resolver(FileConflict conflict, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref asked);
            most = Math.Max(most, Interlocked.Increment(ref current));
            await Task.Delay(20, cancellationToken);
            Interlocked.Decrement(ref current);
            return ConflictAction.Overwrite;
        }

        var (result, _) = await CopyAsync(sources, new CopyOptions { ConflictPolicy = ConflictPolicy.Ask }, Resolver);

        Assert.Equal(8, result.SucceededCount);
        Assert.Equal(8, asked);
        Assert.Equal(1, most); // a dialog at a time
    }

    [Fact]
    public async Task Cancelled_EveryFileHasAResult()
    {
        var sources = CreateFiles(16);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var request = new CopyRequest { Sources = sources, Destination = _temp.Combine("dst") };
        var job = new CopyJob(request, new SlowFlushPlatform(), destinationOnNetwork: false)
        {
            FileFinished = _ => cancel.Cancel(), // cancel as soon as the first file is done
        };

        var result = await job.RunAsync(cancellationToken: cancel.Token);

        Assert.True(result.WasCancelled);
        Assert.Equal(sources, result.Files.Select(f => f.SourcePath));
        Assert.Contains(result.Files, f => f.Status == FileCopyStatus.Cancelled);
        Assert.DoesNotContain(result.Files, f => f.Status == FileCopyStatus.Failed);
        Assert.Empty(Directory.GetFiles(_temp.Combine("dst"), "*.gbcopy-tmp", SearchOption.AllDirectories));
    }
}
