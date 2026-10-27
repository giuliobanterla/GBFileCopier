using System.Buffers;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using GBFileCopier.Core.Hashing;

namespace GBFileCopier.Core.Copying;

/// <summary>
/// Copies the bytes of one file. A reader task reads blocks and feeds the checksum,
/// a writer task writes them: reading and writing overlap, with at most
/// <see cref="CopyOptions.BufferCount"/> blocks in between.
/// </summary>
internal static class FilePipeline
{
    /// <summary>Alignment required by unbuffered I/O: a memory page, a multiple of every sector size.</summary>
    public const int Alignment = 4096;

    private readonly record struct Block(byte[] Buffer, int Length);

    /// <returns>
    /// Bytes copied, the checksum of the source (null without an algorithm), and whether only a write
    /// barrier was used (the data still has to be flushed to permanent storage later).
    /// </returns>
    public static async Task<(long Bytes, byte[]? Hash, bool OnlyOrdered)> CopyAsync(
        string sourcePath,
        string destinationPath,
        long expectedLength,
        CopyOptions options,
        IFileSystemPlatform platform,
        PauseController pause,
        Action<long> onBytesWritten,
        CancellationToken cancellationToken)
    {
        var algorithm = options.EffectiveHashAlgorithm;
        using var hasher = algorithm == HashAlgorithmKind.None ? null : StreamingHasher.Create(algorithm);
        using var source = File.OpenHandle(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        // Preallocating fails right away if the disk is too full, and limits fragmentation
        using var destination = File.OpenHandle(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            FileOptions.Asynchronous, preallocationSize: expectedLength);

        var blocks = Channel.CreateBounded<Block>(new BoundedChannelOptions(options.BufferCount)
        {
            SingleReader = true,
            SingleWriter = true,
        });
        using var failure = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = failure.Token;

        async Task ReadAsync()
        {
            try
            {
                long offset = 0;
                while (true)
                {
                    await pause.WaitIfPausedAsync(token);
                    var buffer = ArrayPool<byte>.Shared.Rent(options.BufferSize);
                    int read;
                    try
                    {
                        read = await RandomAccess.ReadAsync(source, buffer.AsMemory(0, options.BufferSize), offset, token);
                    }
                    catch
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                        throw;
                    }

                    if (read == 0)
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                        break;
                    }

                    hasher?.Append(buffer.AsSpan(0, read));
                    offset += read;
                    await blocks.Writer.WriteAsync(new Block(buffer, read), token);
                }

                blocks.Writer.Complete();
            }
            catch (Exception ex)
            {
                blocks.Writer.TryComplete(ex);
                throw;
            }
        }

        async Task<long> WriteAsync()
        {
            try
            {
                long offset = 0;
                await foreach (var block in blocks.Reader.ReadAllAsync(token))
                {
                    try
                    {
                        await RandomAccess.WriteAsync(destination, block.Buffer.AsMemory(0, block.Length), offset, token);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(block.Buffer);
                    }

                    offset += block.Length;
                    onBytesWritten(block.Length);
                }

                return offset;
            }
            catch
            {
                failure.Cancel(); // unblock the reader if it is waiting for space
                throw;
            }
        }

        var reader = Task.Run(ReadAsync, CancellationToken.None);
        var writer = Task.Run(WriteAsync, CancellationToken.None);
        try
        {
            await Task.WhenAll(reader, writer);
        }
        catch
        {
            // Report the real cause, not the cancellation it triggered in the other task
            var cause = new Task[] { reader, writer }
                .Where(t => t.IsFaulted)
                .Select(t => t.Exception!.InnerException!)
                .FirstOrDefault(e => e is not OperationCanceledException);
            if (cause is not null)
            {
                ExceptionDispatchInfo.Throw(cause);
            }

            throw;
        }

        var written = writer.Result;
        RandomAccess.SetLength(destination, written); // drop any preallocated space beyond the data
        var onlyOrdered = false;
        if (options.FlushToDisk)
        {
            // The barrier keeps the order: the data is on the disk before the file gets its final name
            onlyOrdered = options.BarrierThenFinalFlush && platform.TryOrderWrites(destination);
            if (!onlyOrdered)
            {
                platform.FlushToDisk(destination);
            }
        }

        return (written, hasher?.GetHashAndReset(), onlyOrdered);
    }

    /// <summary>Reads a whole file, bypassing the OS cache where the platform allows it, and returns its checksum.</summary>
    public static async Task<byte[]> HashFileAsync(
        string path,
        HashAlgorithmKind algorithm,
        int bufferSize,
        IFileSystemPlatform platform,
        PauseController pause,
        Action<long> onBytesRead,
        CancellationToken cancellationToken)
    {
        using var hasher = StreamingHasher.Create(algorithm);
        using var file = platform.OpenForVerification(path);
        using var buffer = new AlignedBuffer(bufferSize, Alignment);
        var memory = buffer.Memory;

        long offset = 0;
        while (true)
        {
            await pause.WaitIfPausedAsync(cancellationToken);
            // Always a full aligned block: only the last read returns less
            var read = await RandomAccess.ReadAsync(file, memory, offset, cancellationToken);
            if (read == 0)
            {
                break;
            }

            hasher.Append(memory.Span[..read]);
            offset += read;
            onBytesRead(read);
        }

        return hasher.GetHashAndReset();
    }
}
