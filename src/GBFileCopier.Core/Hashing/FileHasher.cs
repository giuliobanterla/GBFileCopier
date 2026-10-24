using GBFileCopier.Core.Copying;

namespace GBFileCopier.Core.Hashing;

public static class FileHasher
{
    private const int BufferSize = 1024 * 1024;

    /// <summary>Checksum of a whole file, read from the disk when the platform can bypass the cache.</summary>
    public static Task<byte[]> HashFileAsync(
        string path,
        HashAlgorithmKind algorithm,
        IFileSystemPlatform? platform = null,
        CancellationToken cancellationToken = default) =>
        FilePipeline.HashFileAsync(
            path, algorithm, BufferSize, platform ?? ManagedFileSystemPlatform.Instance, new PauseController(), _ => { }, cancellationToken);
}
