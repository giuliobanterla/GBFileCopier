using GBFileCopier.Core.Copying;

namespace GBFileCopier.Core.Hashing;

public static class FileHasher
{
    private const int BufferSize = 1024 * 1024;

    /// <summary>Checksum of a whole file.</summary>
    public static Task<byte[]> HashFileAsync(string path, HashAlgorithmKind algorithm, CancellationToken cancellationToken = default) =>
        FilePipeline.HashFileAsync(path, algorithm, BufferSize, new PauseController(), _ => { }, cancellationToken);
}
