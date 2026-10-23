using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace GBFileCopier.Platform.Clipboard;

/// <summary>
/// The Windows clipboard: files are a CF_HDROP list, and Explorer adds the
/// "Preferred DropEffect" format set to DROPEFFECT_MOVE when they were cut.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class WindowsFileClipboard
{
    private const uint CF_HDROP = 15;
    private const uint GMEM_MOVEABLE = 0x2;
    private const int DROPEFFECT_COPY = 1;
    private const int DROPEFFECT_MOVE = 2;
    private const int DropFilesHeaderSize = 20; // DROPFILES: pFiles, pt.x, pt.y, fNC, fWide

    private static readonly uint PreferredDropEffect = RegisterClipboardFormat("Preferred DropEffect");

    public static ClipboardFiles? Read()
    {
        using var clipboard = OpenClipboard();
        var drop = GetClipboardData(CF_HDROP);
        if (drop == 0)
        {
            return null;
        }

        var paths = new List<string>();
        var count = DragQueryFile(drop, uint.MaxValue, null, 0);
        for (uint i = 0; i < count; i++)
        {
            var length = DragQueryFile(drop, i, null, 0);
            var buffer = new char[length + 1];
            if (DragQueryFile(drop, i, buffer, (uint)buffer.Length) > 0)
            {
                paths.Add(new string(buffer, 0, (int)length));
            }
        }

        return paths.Count == 0 ? null : new ClipboardFiles(paths, ReadDropEffect() == DROPEFFECT_MOVE);
    }

    public static void Write(IReadOnlyList<string> paths, bool cut)
    {
        var dropFiles = CreateDropFiles(paths);
        var effect = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(effect, cut ? DROPEFFECT_MOVE : DROPEFFECT_COPY);

        using var clipboard = OpenClipboard();
        if (!EmptyClipboard())
        {
            throw NewIOException("Cannot empty the clipboard");
        }

        SetData(CF_HDROP, dropFiles);
        SetData(PreferredDropEffect, effect);
    }

    public static void Clear()
    {
        using var clipboard = OpenClipboard();
        EmptyClipboard();
    }

    /// <summary>DROPFILES header followed by the wide paths, each null-terminated, plus a final null.</summary>
    internal static byte[] CreateDropFiles(IReadOnlyList<string> paths)
    {
        var text = string.Concat(paths.Select(p => p + '\0')) + '\0';
        var data = new byte[DropFilesHeaderSize + Encoding.Unicode.GetByteCount(text)];
        BinaryPrimitives.WriteInt32LittleEndian(data, DropFilesHeaderSize); // pFiles: offset of the list
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(16), 1); // fWide: UTF-16
        Encoding.Unicode.GetBytes(text, data.AsSpan(DropFilesHeaderSize));
        return data;
    }

    private static int ReadDropEffect()
    {
        var handle = GetClipboardData(PreferredDropEffect);
        if (handle == 0 || GlobalSize(handle) < 4)
        {
            return DROPEFFECT_COPY;
        }

        var pointer = GlobalLock(handle);
        try
        {
            return pointer == 0 ? DROPEFFECT_COPY : Marshal.ReadInt32(pointer);
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    private static void SetData(uint format, byte[] data)
    {
        var handle = GlobalAlloc(GMEM_MOVEABLE, (nuint)data.Length);
        if (handle == 0)
        {
            throw NewIOException("Cannot allocate clipboard memory");
        }

        var pointer = GlobalLock(handle);
        Marshal.Copy(data, 0, pointer, data.Length);
        GlobalUnlock(handle);

        // On success the clipboard owns the memory
        if (SetClipboardData(format, handle) == 0)
        {
            GlobalFree(handle);
            throw NewIOException("Cannot write to the clipboard");
        }
    }

    /// <summary>Another program may hold the clipboard for a moment: retry briefly.</summary>
    private static ClipboardLock OpenClipboard()
    {
        for (var attempt = 0; ; attempt++)
        {
            if (OpenClipboard(0))
            {
                return new ClipboardLock();
            }

            if (attempt == 10)
            {
                throw NewIOException("The clipboard is in use by another program");
            }

            Thread.Sleep(50);
        }
    }

    private static IOException NewIOException(string message) =>
        new($"{message}: {Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError())}");

    private readonly struct ClipboardLock : IDisposable
    {
        public void Dispose() => CloseClipboard();
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(nint newOwner);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint GetClipboardData(uint format);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetClipboardData(uint format, nint memory);

    [LibraryImport("user32.dll", EntryPoint = "RegisterClipboardFormatW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterClipboardFormat(string format);

    [LibraryImport("shell32.dll", EntryPoint = "DragQueryFileW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint DragQueryFile(nint drop, uint index, [Out] char[]? file, uint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalAlloc(uint flags, nuint bytes);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalFree(nint memory);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalLock(nint memory);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint memory);

    [LibraryImport("kernel32.dll")]
    private static partial nuint GlobalSize(nint memory);
}
