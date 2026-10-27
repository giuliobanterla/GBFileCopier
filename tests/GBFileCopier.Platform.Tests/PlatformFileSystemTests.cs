using GBFileCopier.Core.Copying;
using GBFileCopier.Core.Hashing;
using GBFileCopier.Tests;

namespace GBFileCopier.Platform.Tests;

public sealed class PlatformFileSystemTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static IFileSystemPlatform Platform => PlatformFileSystem.Current;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void Current_IsNativeOnWindowsAndMacOS()
    {
        Assert.Equal(
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS(),
            Platform is not ManagedFileSystemPlatform);
    }

    [Fact]
    public void WriteBarrier_OnMacOS_IsAvailable_ElsewhereNot()
    {
        var path = _temp.Combine("barrier.bin");
        using var handle = File.OpenHandle(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, FileOptions.Asynchronous);
        RandomAccess.Write(handle, new byte[10_000], 0);

        // APFS (the temporary folder) has barriers; Windows has no such call
        Assert.Equal(OperatingSystem.IsMacOS(), Platform.TryOrderWrites(handle));
    }

    [Fact]
    public void FlushToDisk_ThroughAReadOnlyHandle_Succeeds()
    {
        // The end of a job flushes the drive through a file opened only for reading. macOS only: that is
        // where it happens (Windows has no barrier, and FlushFileBuffers needs a handle open for writing)
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "macOS only");
        var path = _temp.CreateFile("written.bin", 10_000);
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        Platform.FlushToDisk(handle);
    }

    [Fact]
    public void FlushToDisk_OnAWrittenFile_Succeeds()
    {
        var path = _temp.Combine("flush.bin");
        using var handle = File.OpenHandle(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, FileOptions.Asynchronous);
        RandomAccess.Write(handle, new byte[10_000], 0);

        Platform.FlushToDisk(handle);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4095)]
    [InlineData(4096)]
    [InlineData(4097)]
    [InlineData(1_000_003)]
    public async Task VerificationRead_ReturnsTheFileContent(int length)
    {
        // Unbuffered reads have alignment rules: sizes around the 4096 boundary catch mistakes
        var path = _temp.CreateFile("data.bin", length);

        var hash = await FileHasher.HashFileAsync(path, HashAlgorithmKind.Sha256, Platform, Token);

        Assert.Equal(StreamingHasher.Compute(HashAlgorithmKind.Sha256, File.ReadAllBytes(path)), hash);
    }

    [Fact]
    public void TryMoveWithinVolume_RenamesTheFile()
    {
        Assert.SkipWhen(Platform is ManagedFileSystemPlatform, "No native rename on this platform");
        var source = _temp.CreateFile("a.txt", text: "content");
        var destination = _temp.Combine("sub", "b.txt");
        Directory.CreateDirectory(_temp.Combine("sub"));

        Assert.True(Platform.TryMoveWithinVolume(source, destination, overwrite: false));

        Assert.False(File.Exists(source));
        Assert.Equal("content", File.ReadAllText(destination));
    }

    [Fact]
    public void TryMoveWithinVolume_ExistingDestination_FailsWithoutOverwrite()
    {
        Assert.SkipWhen(Platform is ManagedFileSystemPlatform, "No native rename on this platform");
        var source = _temp.CreateFile("a.txt", text: "new");
        var destination = _temp.CreateFile("b.txt", text: "old");

        Assert.ThrowsAny<IOException>(() => Platform.TryMoveWithinVolume(source, destination, overwrite: false));

        Assert.Equal("new", File.ReadAllText(source));
        Assert.Equal("old", File.ReadAllText(destination));
    }

    [Fact]
    public void TryMoveWithinVolume_ExistingDestination_IsReplacedWithOverwrite()
    {
        Assert.SkipWhen(Platform is ManagedFileSystemPlatform, "No native rename on this platform");
        var source = _temp.CreateFile("a.txt", text: "new");
        var destination = _temp.CreateFile("b.txt", text: "old");

        Assert.True(Platform.TryMoveWithinVolume(source, destination, overwrite: true));

        Assert.False(File.Exists(source));
        Assert.Equal("new", File.ReadAllText(destination));
    }

    [Fact]
    public async Task CopyJob_MoveOnSameVolume_RenamesInsteadOfCopying()
    {
        Assert.SkipWhen(Platform is ManagedFileSystemPlatform, "No native rename on this platform");
        _temp.CreateFile("src/a.bin", 10_000);
        _temp.CreateFile("src/sub/b.bin", 20_000);
        var expected = File.ReadAllBytes(_temp.Combine("src", "sub", "b.bin"));
        var job = new CopyJob(
            new CopyRequest { Sources = [_temp.Combine("src")], Destination = _temp.Combine("dst"), Mode = CopyMode.Move },
            Platform);

        var result = await job.RunAsync(cancellationToken: Token);

        Assert.True(result.IsSuccess);
        Assert.All(result.Files, f => Assert.True(f.Renamed));
        Assert.False(Directory.Exists(_temp.Combine("src")));
        Assert.Equal(expected, File.ReadAllBytes(_temp.Combine("dst", "src", "sub", "b.bin")));
    }

    [Fact]
    public async Task CopyJob_CopyWithVerification_UsesNativeOperations()
    {
        var source = _temp.CreateFile("src/big.bin", 3_000_001);
        var job = new CopyJob(
            new CopyRequest
            {
                Sources = [source],
                Destination = _temp.Combine("dst"),
                Options = new CopyOptions { Verify = true, BufferSize = 64 * 1024 },
            },
            Platform);

        var result = await job.RunAsync(cancellationToken: Token);

        var file = Assert.Single(result.Files);
        Assert.True(file.Verified, file.Error);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(_temp.Combine("dst", "big.bin")));
    }

    [Theory]
    [InlineData(@"C:\data\file.txt", @"\\?\C:\data\file.txt")]
    [InlineData(@"\\server\share\file.txt", @"\\?\UNC\server\share\file.txt")]
    [InlineData(@"\\?\C:\already.txt", @"\\?\C:\already.txt")]
    public void ToExtendedPath_AddsThePrefix(string path, string expected)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows paths");
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(expected, WindowsFileSystem.ToExtendedPath(path));
        }
    }
}

public sealed class MacFileSystemTests : IDisposable
{
    private const int F_GETFD = 1;
    private const int F_SETFD = 2;
    private const int FD_CLOEXEC = 1;

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>
    /// fcntl is variadic and needs a special calling trick on Apple Silicon: if the argument
    /// landed in the wrong place, F_NOCACHE would fail silently. F_SETFD/F_GETFD make it visible.
    /// </summary>
    [Fact]
    public void Fcntl_PassesTheArgument()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "macOS only");
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        using var handle = File.OpenHandle(_temp.CreateFile("f.txt"), FileMode.Open);

        Assert.Equal(0, MacFileSystem.Fcntl(handle, F_SETFD, 0));
        Assert.Equal(0, MacFileSystem.Fcntl(handle, F_GETFD, 0) & FD_CLOEXEC);
        Assert.Equal(0, MacFileSystem.Fcntl(handle, F_SETFD, FD_CLOEXEC));
        Assert.Equal(FD_CLOEXEC, MacFileSystem.Fcntl(handle, F_GETFD, 0) & FD_CLOEXEC);
    }
}
