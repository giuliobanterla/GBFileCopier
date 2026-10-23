using System.IO.Hashing;
using System.Security.Cryptography;

namespace GBFileCopier.Core.Hashing;

/// <summary>
/// Computes a checksum incrementally, one block at a time, so that it can be calculated
/// while the data flows through the copy pipeline.
/// </summary>
public sealed class StreamingHasher : IDisposable
{
    private readonly NonCryptographicHashAlgorithm? _nonCryptographic;
    private readonly IncrementalHash? _cryptographic;
    private readonly bool _littleEndianResult;

    private StreamingHasher(NonCryptographicHashAlgorithm algorithm, bool littleEndianResult = false)
    {
        _nonCryptographic = algorithm;
        _littleEndianResult = littleEndianResult;
    }

    private StreamingHasher(IncrementalHash algorithm) => _cryptographic = algorithm;

    public static StreamingHasher Create(HashAlgorithmKind kind) => kind switch
    {
        // System.IO.Hashing returns CRCs in little-endian order: reversed to get the usual notation
        HashAlgorithmKind.Crc32 => new(new Crc32(), littleEndianResult: true),
        HashAlgorithmKind.Crc64 => new(new Crc64(), littleEndianResult: true),
        HashAlgorithmKind.XxHash64 => new(new XxHash64()),
        HashAlgorithmKind.XxHash3 => new(new XxHash3()),
        HashAlgorithmKind.XxHash128 => new(new XxHash128()),
        // MD5 and SHA-1 are used only to detect accidental corruption, not for security
#pragma warning disable CA5350, CA5351
        HashAlgorithmKind.Md5 => new(IncrementalHash.CreateHash(HashAlgorithmName.MD5)),
        HashAlgorithmKind.Sha1 => new(IncrementalHash.CreateHash(HashAlgorithmName.SHA1)),
#pragma warning restore CA5350, CA5351
        HashAlgorithmKind.Sha256 => new(IncrementalHash.CreateHash(HashAlgorithmName.SHA256)),
        HashAlgorithmKind.Sha512 => new(IncrementalHash.CreateHash(HashAlgorithmName.SHA512)),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No hash algorithm selected"),
    };

    public void Append(ReadOnlySpan<byte> data)
    {
        if (_nonCryptographic is not null)
        {
            _nonCryptographic.Append(data);
        }
        else
        {
            _cryptographic!.AppendData(data);
        }
    }

    public byte[] GetHashAndReset()
    {
        if (_cryptographic is not null)
        {
            return _cryptographic.GetHashAndReset();
        }

        var hash = _nonCryptographic!.GetHashAndReset();
        if (_littleEndianResult)
        {
            Array.Reverse(hash);
        }

        return hash;
    }

    public void Dispose() => _cryptographic?.Dispose();

    /// <summary>Hash of a whole buffer, mostly for tests and small data.</summary>
    public static byte[] Compute(HashAlgorithmKind kind, ReadOnlySpan<byte> data)
    {
        using var hasher = Create(kind);
        hasher.Append(data);
        return hasher.GetHashAndReset();
    }

    public static string ToHex(byte[] hash) => Convert.ToHexStringLower(hash);
}
