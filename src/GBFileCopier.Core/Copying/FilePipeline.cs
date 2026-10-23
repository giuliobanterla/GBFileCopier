using System.Buffers;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using GBFileCopier.Core.Hashing;
using Microsoft.Win32.SafeHandles;

namespace GBFileCopier.Core.Copying;

/// <summary>
/// Copies the bytes of one file. A reader task reads blocks and feeds the checksum,
/// a writer task writes them: reading and writing overlap, with at most
/// <see cref="CopyOptions.BufferCount"/> blocks in between.
/// </summary>
internal static class FilePipeline
{
    private readonly record struct Block(byte[] Buffer, int Length);

    /// <returns>Bytes copied and the checksum of the source (null without an algorithm).</returns>
    public static async Task<(long Bytes, byte[]? Hash)> CopyAsync(
        string sourcePath,
        string destinationPath,
        long expectedLength,
        CopyOptions options,
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
        if (options.FlushToDisk)
        {
            FlushToDisk(destination);
        }

        return (written, hasher?.GetHashAndReset());
    }

    /// <summary>Reads a whole file and returns its checksum.</summary>
    public static async Task<byte[]> HashFileAsync(
        string path,
        HashAlgorithmKind algorithm,
        int bufferSize,
        PauseController pause,
        Action<long> onBytesRead,
        CancellationToken cancellationToken)
    {
        using var hasher = StreamingHasher.Create(algorithm);
        using var file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            long offset = 0;
            while (true)
            {
                await pause.WaitIfPausedAsync(cancellationToken);
                var read = await RandomAccess.ReadAsync(file, buffer.AsMemory(0, bufferSize), offset, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                hasher.Append(buffer.AsSpan(0, read));
                offset += read;
                onBytesRead(read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return hasher.GetHashAndReset();
    }

    private static void FlushToDisk(SafeFileHandle handle)
    {
        // FileStream is the only portable way to fsync / FlushFileBuffers a handle.
        // It takes ownership of the handle, which the caller disposes again: harmless.
        using var stream = new FileStream(handle, FileAccess.Write, bufferSize: 0, isAsync: true);
        stream.Flush(flushToDisk: true);
    }
}
