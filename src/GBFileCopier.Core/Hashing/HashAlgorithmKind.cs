namespace GBFileCopier.Core.Hashing;

/// <summary>Checksum algorithms available for integrity verification.</summary>
public enum HashAlgorithmKind
{
    None,
    Crc32,
    Crc64,
    XxHash64,
    XxHash3,
    XxHash128,
    Md5,
    Sha1,
    Sha256,
    Sha512,
}

/// <summary>Short names of the algorithms, as used on the command line and in checksum files.</summary>
public static class HashAlgorithmNames
{
    private static readonly (HashAlgorithmKind Kind, string Name)[] Names =
    [
        (HashAlgorithmKind.None, "none"),
        (HashAlgorithmKind.Crc32, "crc32"),
        (HashAlgorithmKind.Crc64, "crc64"),
        (HashAlgorithmKind.XxHash64, "xxh64"),
        (HashAlgorithmKind.XxHash3, "xxh3"),
        (HashAlgorithmKind.XxHash128, "xxh128"),
        (HashAlgorithmKind.Md5, "md5"),
        (HashAlgorithmKind.Sha1, "sha1"),
        (HashAlgorithmKind.Sha256, "sha256"),
        (HashAlgorithmKind.Sha512, "sha512"),
    ];

    public static IEnumerable<string> All => Names.Select(n => n.Name);

    public static string GetName(HashAlgorithmKind kind) =>
        Names.First(n => n.Kind == kind).Name;

    public static bool TryParse(string? name, out HashAlgorithmKind kind)
    {
        foreach (var (k, n) in Names)
        {
            if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
            {
                kind = k;
                return true;
            }
        }

        kind = HashAlgorithmKind.None;
        return false;
    }
}
