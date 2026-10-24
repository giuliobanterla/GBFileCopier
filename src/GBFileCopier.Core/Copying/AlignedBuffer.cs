using System.Buffers;
using System.Runtime.InteropServices;

namespace GBFileCopier.Core.Copying;

/// <summary>
/// Native memory aligned to a page boundary. Unbuffered I/O (FILE_FLAG_NO_BUFFERING on Windows,
/// direct reads with F_NOCACHE on macOS) requires aligned buffers, which managed arrays don't guarantee.
/// </summary>
internal sealed unsafe class AlignedBuffer : MemoryManager<byte>
{
    private void* _pointer;
    private readonly int _length;

    public AlignedBuffer(int length, int alignment)
    {
        _length = length;
        _pointer = NativeMemory.AlignedAlloc((nuint)length, (nuint)alignment);
    }

    public override Span<byte> GetSpan()
    {
        ObjectDisposedException.ThrowIf(_pointer is null, this);
        return new Span<byte>(_pointer, _length);
    }

    public override MemoryHandle Pin(int elementIndex = 0) =>
        new((byte*)_pointer + elementIndex); // native memory never moves

    public override void Unpin()
    {
    }

    protected override void Dispose(bool disposing)
    {
        if (_pointer is not null)
        {
            NativeMemory.AlignedFree(_pointer);
            _pointer = null;
        }
    }
}
