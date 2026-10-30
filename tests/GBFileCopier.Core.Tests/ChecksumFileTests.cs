using GBFileCopier.Core.Copying;
using GBFileCopier.Core.Hashing;
using GBFileCopier.Tests;

namespace GBFileCopier.Core.Tests;

public sealed class ChecksumFileTests : IDisposable
{
    private const string Sha256OfHello = "5891b5b522d5df086d0ff0b110fbd9d21bb4fc7163af34d08286a2e846f6be03"; // "hello\n"
    private const string Sha1 = "f572d396fae9206628714fb2ce00f72e94f2258f";

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(HashAlgorithmKind.Sha256, ".sha256")]
    [InlineData(HashAlgorithmKind.Md5, ".md5")]
    [InlineData(HashAlgorithmKind.Crc32, ".sfv")]
    [InlineData(HashAlgorithmKind.XxHash3, ".xxh3")]
    public void Extension_FollowsTheAlgorithm_AndBack(HashAlgorithmKind kind, string extension)
    {
        Assert.Equal(extension, ChecksumFile.ExtensionFor(kind));
        Assert.True(ChecksumFile.TryGetAlgorithm("list" + extension.ToUpperInvariant(), out var back));
        Assert.Equal(kind, back);
    }

    [Fact]
    public void Format_IsTheSha256sumFormat_AndReadsBack()
    {
        var entries = new[] { new ChecksumEntry("Photos/a b.txt", Sha256OfHello), new ChecksumEntry("Photos/back\\slash", Sha256OfHello) };

        var text = ChecksumFile.Format(HashAlgorithmKind.Sha256, entries);

        Assert.Equal($"{Sha256OfHello}  Photos/a b.txt\n\\{Sha256OfHello}  Photos/back\\\\slash\n", text);
        var list = ChecksumFile.Parse(text, ".sha256");
        Assert.Equal(HashAlgorithmKind.Sha256, list.Algorithm);
        Assert.Equal(entries, list.Entries);
    }

    [Fact]
    public void Sfv_HasUppercaseCrcAfterTheName_AndReadsBack()
    {
        var entries = new[] { new ChecksumEntry("dir/name with spaces.bin", "cbf43926") };

        var text = ChecksumFile.Format(HashAlgorithmKind.Crc32, entries);

        Assert.EndsWith("dir/name with spaces.bin CBF43926\n", text, StringComparison.Ordinal);
        Assert.StartsWith(";", text, StringComparison.Ordinal);
        Assert.Equal(entries, ChecksumFile.Parse(text, ".sfv").Entries);
    }

    [Theory]
    [InlineData($"{Sha1}  a.txt\n")] // sha1sum, text mode
    [InlineData($"{Sha1} *a.txt\r\n")] // binary mode, Windows line ends
    [InlineData($"{Sha1} a.txt")] // md5 -r style
    [InlineData($"﻿# comment\n\n{Sha1}  a.txt\n")] // BOM, comments, blank lines
    public void Parse_ReadsTheUsualVariants(string text)
    {
        var entry = Assert.Single(ChecksumFile.Parse(text, ".sha1").Entries);

        Assert.Equal(new ChecksumEntry("a.txt", Sha1), entry);
    }

    [Fact]
    public void Parse_BsdTags_GiveTheAlgorithm()
    {
        var list = ChecksumFile.Parse($"SHA1 (dir/a (1).txt) = {Sha1.ToUpperInvariant()}\n", ".txt");

        Assert.Equal(HashAlgorithmKind.Sha1, list.Algorithm);
        Assert.Equal(new ChecksumEntry("dir/a (1).txt", Sha1), Assert.Single(list.Entries));
    }

    [Fact]
    public void Parse_UnknownAlgorithm_IsAnError()
    {
        var error = Assert.Throws<InvalidDataException>(() => ChecksumFile.Parse($"{Sha1}  a.txt\n", ".txt"));

        Assert.Contains("algorithm is unknown", error.Message, StringComparison.Ordinal);
        Assert.Single(ChecksumFile.Parse($"{Sha1}  a.txt\n", ".txt", HashAlgorithmKind.Sha1).Entries); // chosen by the caller
    }

    [Fact]
    public void Parse_LinesOfAnotherLength_AreIgnored()
    {
        Assert.Throws<InvalidDataException>(() => ChecksumFile.Parse($"{Sha1}  a.txt\n", ".sha256"));
    }

    [Fact]
    public async Task Check_ReportsMatchingDifferentAndMissingFiles()
    {
        _temp.CreateFile("Photos/ok.txt", text: "hello\n");
        _temp.CreateFile("Photos/sub/changed.txt", text: "hello!\n");
        var listPath = _temp.Combine("Photos.sha256");
        File.WriteAllText(listPath, $"{Sha256OfHello}  Photos/ok.txt\n{Sha256OfHello}  Photos/sub/changed.txt\n{Sha256OfHello}  Photos/gone.txt\n");
        var planned = new List<PlannedFile>();

        var result = await new ChecksumCheckJob(listPath) { Planned = planned.AddRange }.RunAsync(cancellationToken: Token);

        Assert.Equal(3, planned.Count);
        Assert.Equal(HashAlgorithmKind.Sha256, result.HashAlgorithm);
        Assert.Equal([FileCopyStatus.Succeeded, FileCopyStatus.Failed, FileCopyStatus.Failed], result.Files.Select(f => f.Status));
        Assert.True(result.Files[0].Verified);
        Assert.Equal(_temp.Combine("Photos", "ok.txt"), result.Files[0].SourcePath);
        Assert.Contains("does not match", result.Files[1].Error, StringComparison.Ordinal);
        Assert.Equal("The file does not exist", result.Files[2].Error);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Check_UnreadableList_IsOneFailure()
    {
        var listPath = _temp.Combine("list.sha256");
        File.WriteAllText(listPath, "not a checksum\n");

        var result = await new ChecksumCheckJob(listPath).RunAsync(cancellationToken: Token);

        var failure = Assert.Single(result.Files);
        Assert.Equal(listPath, failure.SourcePath);
        Assert.Contains("no sha256 checksums", failure.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_Cancelled_EveryFileHasAResult()
    {
        for (var i = 0; i < 5; i++)
        {
            _temp.CreateFile($"f{i}.txt", text: "hello\n");
        }

        var listPath = _temp.Combine("list.sha256");
        File.WriteAllText(listPath, string.Concat(Enumerable.Range(0, 5).Select(i => $"{Sha256OfHello}  f{i}.txt\n")));
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);

        var result = await new ChecksumCheckJob(listPath) { FileFinished = _ => cancel.Cancel() }.RunAsync(cancellationToken: cancel.Token);

        Assert.True(result.WasCancelled);
        Assert.Equal(5, result.Files.Count);
        Assert.Equal(FileCopyStatus.Succeeded, result.Files[0].Status);
        Assert.All(result.Files.Skip(1), f => Assert.Equal(FileCopyStatus.Cancelled, f.Status));
    }
}
