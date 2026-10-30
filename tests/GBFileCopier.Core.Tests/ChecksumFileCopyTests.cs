using GBFileCopier.Core.Copying;
using GBFileCopier.Core.Hashing;
using GBFileCopier.Tests;
using Microsoft.Win32.SafeHandles;

namespace GBFileCopier.Core.Tests;

/// <summary>The checksum file written at the end of a copy (<see cref="CopyOptions.WriteChecksumFile"/>).</summary>
public sealed class ChecksumFileCopyTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>The portable platform, but moves within the volume are renames, as on macOS and Windows.</summary>
    private sealed class RenamingPlatform : IFileSystemPlatform
    {
        public string Name => "renaming";

        public SafeFileHandle OpenForVerification(string path) => ManagedFileSystemPlatform.Instance.OpenForVerification(path);

        public void FlushToDisk(SafeFileHandle handle) => ManagedFileSystemPlatform.Instance.FlushToDisk(handle);

        public bool TryMoveWithinVolume(string sourcePath, string destinationPath, bool overwrite)
        {
            File.Move(sourcePath, destinationPath, overwrite);
            return true;
        }
    }

    private Task<CopyJobResult> CopyAsync(IReadOnlyList<string> sources, CopyOptions options, CopyMode mode = CopyMode.Copy)
    {
        var request = new CopyRequest { Sources = sources, Destination = _temp.Combine("dst"), Mode = mode, Options = options };
        return new CopyJob(request, new RenamingPlatform(), destinationOnNetwork: false).RunAsync(cancellationToken: Token);
    }

    [Fact]
    public async Task Folder_GetsNameDotAlgorithm_WithPathsFromTheDestination()
    {
        _temp.CreateFile("src/Photos/a.jpg");
        _temp.CreateFile("src/Photos/2026/b.jpg");

        var result = await CopyAsync([_temp.Combine("src", "Photos")], new CopyOptions { WriteChecksumFile = true, HashAlgorithm = HashAlgorithmKind.Sha256 });

        Assert.Equal(_temp.Combine("dst", "Photos.sha256"), result.ChecksumFilePath);
        var list = ChecksumFile.Read(result.ChecksumFilePath!);
        Assert.Equal(["Photos/a.jpg", "Photos/2026/b.jpg"], list.Entries.Select(e => e.Path));
        var check = await new ChecksumCheckJob(result.ChecksumFilePath!).RunAsync(cancellationToken: Token);
        Assert.Equal(2, check.SucceededCount);
        Assert.True(check.IsSuccess);
    }

    [Fact]
    public async Task OneFile_IsNamedAfterIt_AndSeveralSourcesShareOneList()
    {
        var single = await CopyAsync([_temp.CreateFile("src/IMG.jpg")], new CopyOptions { WriteChecksumFile = true, HashAlgorithm = HashAlgorithmKind.Md5 });
        Assert.Equal(_temp.Combine("dst", "IMG.jpg.md5"), single.ChecksumFilePath);

        var several = await CopyAsync([_temp.CreateFile("src/x.bin"), _temp.CreateFile("src/y.bin")], new CopyOptions { WriteChecksumFile = true, HashAlgorithm = HashAlgorithmKind.Crc32 });
        Assert.Equal(_temp.Combine("dst", "checksums.sfv"), several.ChecksumFilePath);
        Assert.Equal(2, ChecksumFile.Read(several.ChecksumFilePath!).Entries.Count);
    }

    [Fact]
    public async Task ExistingList_IsKept_AndTheNewOneTakesAFreeName()
    {
        _temp.CreateFile("dst/IMG.jpg.xxh3", text: "old");

        var result = await CopyAsync([_temp.CreateFile("src/IMG.jpg")], new CopyOptions { WriteChecksumFile = true, ConflictPolicy = ConflictPolicy.Overwrite });

        Assert.Equal("old", File.ReadAllText(_temp.Combine("dst", "IMG.jpg.xxh3")));
        Assert.Equal(_temp.Combine("dst", "IMG.jpg (2).xxh3"), result.ChecksumFilePath);
    }

    [Fact]
    public async Task NoAlgorithmChosen_UsesXxh3()
    {
        var result = await CopyAsync([_temp.CreateFile("src/a.bin")], new CopyOptions { WriteChecksumFile = true, HashAlgorithm = HashAlgorithmKind.None });

        Assert.Equal(HashAlgorithmKind.XxHash3, result.HashAlgorithm);
        Assert.EndsWith(".xxh3", result.ChecksumFilePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithAFailure_NoListIsWritten()
    {
        var result = await CopyAsync([_temp.CreateFile("src/a.bin"), _temp.Combine("src", "missing.bin")], new CopyOptions { WriteChecksumFile = true });

        Assert.Equal(1, result.FailedCount);
        Assert.Null(result.ChecksumFilePath);
        Assert.Empty(Directory.GetFiles(_temp.Combine("dst"), "*.xxh3"));
    }

    [Fact]
    public async Task SkippedFiles_AreLeftOut()
    {
        _temp.CreateFile("dst/old.bin", text: "already there");

        var result = await CopyAsync(
            [_temp.CreateFile("src/old.bin"), _temp.CreateFile("src/new.bin")],
            new CopyOptions { WriteChecksumFile = true, ConflictPolicy = ConflictPolicy.Skip });

        Assert.Equal(["new.bin"], ChecksumFile.Read(result.ChecksumFilePath!).Entries.Select(e => e.Path));
    }

    [Fact]
    public async Task MoveByRename_HashesTheMovedFiles()
    {
        // Same volume: the files are renamed, never read while moving
        var source = _temp.CreateFile("src/Album/a.bin", 5000);
        var expected = StreamingHasher.ToHex(StreamingHasher.Compute(HashAlgorithmKind.Sha256, File.ReadAllBytes(source)));

        var result = await CopyAsync([_temp.Combine("src", "Album")], new CopyOptions { WriteChecksumFile = true, HashAlgorithm = HashAlgorithmKind.Sha256 }, CopyMode.Move);

        Assert.False(File.Exists(source));
        Assert.True(Assert.Single(result.Files, f => f.Kind == CopyItemKind.File).Renamed);
        Assert.Equal(new ChecksumEntry("Album/a.bin", expected), Assert.Single(ChecksumFile.Read(result.ChecksumFilePath!).Entries));
    }

    [Fact]
    public async Task Off_NoListIsWritten()
    {
        var result = await CopyAsync([_temp.CreateFile("src/a.bin")], new CopyOptions());

        Assert.Null(result.ChecksumFilePath);
        Assert.Single(Directory.GetFiles(_temp.Combine("dst")));
    }
}
