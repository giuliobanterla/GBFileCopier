using System.Text;
using GBFileCopier.Core.Hashing;

namespace GBFileCopier.Core.Tests;

public class HashingTests
{
    private static readonly byte[] Check = Encoding.ASCII.GetBytes("123456789");

    [Theory]
    [InlineData(HashAlgorithmKind.Crc32, "cbf43926")] // standard CRC-32 check value
    [InlineData(HashAlgorithmKind.Md5, "25f9e794323b453885f5181f1b624d0b")]
    [InlineData(HashAlgorithmKind.Sha256, "15e2b0d3c33891ebb0f1ef609ec419420c20e320ce94c65fbc8c3312448eb225")]
    public void Compute_MatchesKnownValues(HashAlgorithmKind kind, string expected)
    {
        Assert.Equal(expected, StreamingHasher.ToHex(StreamingHasher.Compute(kind, Check)));
    }

    [Fact]
    public void XxHash64_OfEmptyInput_MatchesReference()
    {
        Assert.Equal("ef46db3751d8e999", StreamingHasher.ToHex(StreamingHasher.Compute(HashAlgorithmKind.XxHash64, [])));
    }

    [Theory]
    [InlineData(HashAlgorithmKind.Crc64)]
    [InlineData(HashAlgorithmKind.XxHash3)]
    [InlineData(HashAlgorithmKind.XxHash128)]
    [InlineData(HashAlgorithmKind.Sha1)]
    [InlineData(HashAlgorithmKind.Sha512)]
    public void Append_InPieces_EqualsSinglePass(HashAlgorithmKind kind)
    {
        var data = new byte[100_000];
        Random.Shared.NextBytes(data);

        using var hasher = StreamingHasher.Create(kind);
        hasher.Append(data.AsSpan(0, 333));
        hasher.Append(data.AsSpan(333));

        Assert.Equal(StreamingHasher.Compute(kind, data), hasher.GetHashAndReset());
    }

    [Fact]
    public void Names_RoundTrip()
    {
        foreach (var kind in Enum.GetValues<HashAlgorithmKind>())
        {
            Assert.True(HashAlgorithmNames.TryParse(HashAlgorithmNames.GetName(kind).ToUpperInvariant(), out var parsed));
            Assert.Equal(kind, parsed);
        }

        Assert.False(HashAlgorithmNames.TryParse("nope", out _));
    }
}
