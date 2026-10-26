using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace GBFileCopier.Platform;

/// <summary>
/// Takes over Ctrl+V in Explorer (see <see cref="ExplorerPasteRules"/>) with a low-level keyboard
/// hook: nothing is loaded into Explorer. The hook only decides, in a few milliseconds, and hands
/// the Explorer window over; finding its folder and copying happen afterwards. Windows calls a
/// low-level hook for every key of every program through the message loop of the thread that
/// installed it, so the hook gets a thread of its own that does nothing else: a busy UI can never
/// slow down the keyboard, nor make Windows drop the hook.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe partial class WindowsPasteHook : IDisposable
{
    /// <summary>Marks the Ctrl+V sent back to Explorer by <see cref="SendPasteToExplorer"/>, so that the hook lets it through.</summary>
    private const nuint ReplayMarker = 0x47424350; // "GBCP"

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint WM_QUIT = 0x0012;
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_V = 0x56;
    private const uint CF_HDROP = 15;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x2;

    private static WindowsPasteHook? s_current;

    // Holding Ctrl+V repeats the key: one paste per press, the repeats are swallowed until V is released
    private static bool s_swallowingV;

    private readonly Action<nint> _onPaste;
    private Thread? _thread;
    private uint _threadId;

    private WindowsPasteHook(Action<nint> onPaste) => _onPaste = onPaste;

    /// <summary>Starts watching Ctrl+V.</summary>
    /// <param name="onPaste">
    /// Called on the hook's own thread, after the key has been taken, with the Explorer window. It
    /// must return at once (post the work to the UI thread): the keyboard waits for it.
    /// </param>
    /// <exception cref="InvalidOperationException">A hook is already installed, or Windows refused it.</exception>
    public static WindowsPasteHook Install(Action<nint> onPaste)
    {
        ArgumentNullException.ThrowIfNull(onPaste);
        if (s_current is not null)
        {
            throw new InvalidOperationException("The paste hook is already installed");
        }

        var hook = new WindowsPasteHook(onPaste);
        s_current = hook;
        using var started = new ManualResetEventSlim();
        string? error = null;
        hook._thread = new Thread(() =>
        {
            hook._threadId = GetCurrentThreadId();
            var handle = SetWindowsHookEx(WH_KEYBOARD_LL, &OnKey, GetModuleHandle(null), 0);
            if (handle == 0)
            {
                error = Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError());
            }

            started.Set();
            if (handle == 0)
            {
                return;
            }

            // Windows calls the hook while this loop waits for messages; WM_QUIT ends it
            Message message;
            while (GetMessage(&message, 0, 0, 0) > 0)
            {
            }

            UnhookWindowsHookEx(handle);
        })
        {
            IsBackground = true,
            Name = "GBFileCopier keyboard hook",
        };
        hook._thread.Start();
        started.Wait();
        if (error is not null)
        {
            s_current = null;
            throw new InvalidOperationException($"Cannot install the keyboard hook: {error}");
        }

        return hook;
    }

    public void Dispose()
    {
        if (_thread is { } thread)
        {
            _ = PostThreadMessage(_threadId, WM_QUIT, 0, 0);
            thread.Join(TimeSpan.FromSeconds(2));
            _thread = null;
        }

        if (ReferenceEquals(s_current, this))
        {
            s_current = null;
            s_swallowingV = false;
        }
    }

    /// <summary>
    /// Gives Ctrl+V back to Explorer, when GBFileCopier does not handle it after all (a virtual
    /// folder such as This PC, or the folder the files come from). Only if that window is still in front.
    /// </summary>
    public static void SendPasteToExplorer(nint explorerWindow)
    {
        if (GetForegroundWindow() != explorerWindow)
        {
            return;
        }

        var ctrlHeld = GetAsyncKeyState(VK_CONTROL) < 0;
        var inputs = new List<Input>();
        if (!ctrlHeld)
        {
            inputs.Add(Key(VK_CONTROL, up: false));
        }

        inputs.Add(Key(VK_V, up: false));
        inputs.Add(Key(VK_V, up: true));
        if (!ctrlHeld)
        {
            inputs.Add(Key(VK_CONTROL, up: true));
        }

        var array = inputs.ToArray();
        fixed (Input* pointer = array)
        {
            _ = SendInput((uint)array.Length, pointer, sizeof(Input));
        }
    }

    private static Input Key(int key, bool up) => new()
    {
        Type = INPUT_KEYBOARD,
        VirtualKey = (ushort)key,
        Flags = up ? KEYEVENTF_KEYUP : 0,
        ExtraInfo = ReplayMarker,
    };

    [UnmanagedCallersOnly]
    private static nint OnKey(int code, nint message, nint data)
    {
        try
        {
            var key = (KeyboardHookData*)data;
            if (code >= 0 && key->VirtualKey == VK_V && key->ExtraInfo != ReplayMarker && s_current is { } hook)
            {
                if (message is WM_KEYUP or WM_SYSKEYUP)
                {
                    s_swallowingV = false;
                }
                else if (message is WM_KEYDOWN or WM_SYSKEYDOWN)
                {
                    if (s_swallowingV)
                    {
                        return 1; // a repeat of a Ctrl+V already taken
                    }

                    var window = GetForegroundWindow();
                    if (ExplorerPasteRules.ShouldIntercept(ReadContext(window)))
                    {
                        s_swallowingV = true;
                        hook._onPaste(window);
                        return 1; // taken: Explorer does not see this Ctrl+V
                    }
                }
            }
        }
        catch
        {
            // Nothing may leave a hook: the key goes on as usual
        }

        return CallNextHookEx(0, code, message, data);
    }

    private static PasteKeyContext ReadContext(nint window)
    {
        var info = new GuiThreadInfo { Size = (uint)sizeof(GuiThreadInfo) };
        var thread = GetWindowThreadProcessId(window, null);
        var focus = thread != 0 && GetGUIThreadInfo(thread, &info) ? info.Focus : 0;
        return new PasteKeyContext(
            ClassName(window),
            focus == 0 ? null : ClassName(focus),
            focus == 0 ? null : ClassName(GetParent(focus)),
            Ctrl: GetAsyncKeyState(VK_CONTROL) < 0,
            Shift: GetAsyncKeyState(VK_SHIFT) < 0,
            Alt: GetAsyncKeyState(VK_MENU) < 0,
            Win: GetAsyncKeyState(VK_LWIN) < 0 || GetAsyncKeyState(VK_RWIN) < 0,
            ClipboardHasFiles: IsClipboardFormatAvailable(CF_HDROP));
    }

    internal static string ClassName(nint window)
    {
        if (window == 0)
        {
            return string.Empty;
        }

        var buffer = stackalloc char[256];
        var length = GetClassName(window, buffer, 256);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookData
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Window;
        public uint Id;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint Size;
        public uint Flags;
        public nint Active;
        public nint Focus;
        public nint Capture;
        public nint MenuOwner;
        public nint MoveSize;
        public nint Caret;
        public Rect CaretRect;
    }

    /// <summary>INPUT with its KEYBDINPUT: the union starts at 8 and the whole is 40 bytes on 64-bit Windows.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input
    {
        [FieldOffset(0)]
        public uint Type;

        [FieldOffset(8)]
        public ushort VirtualKey;

        [FieldOffset(10)]
        public ushort ScanCode;

        [FieldOffset(12)]
        public uint Flags;

        [FieldOffset(16)]
        public uint Time;

        [FieldOffset(24)]
        public nuint ExtraInfo;
    }

    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static partial nint SetWindowsHookEx(int hookType, delegate* unmanaged<int, nint, nint, nint> callback, nint module, uint threadId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(nint hook);

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(nint hook, int code, nint message, nint data);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandle(string? module);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial nint GetParent(nint window);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, uint* processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetGUIThreadInfo(uint thread, GuiThreadInfo* info);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    private static partial int GetClassName(nint window, char* name, int length);

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int key);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsClipboardFormatAvailable(uint format);

    [LibraryImport("user32.dll")]
    private static partial uint SendInput(uint count, Input* inputs, int size);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    private static partial int GetMessage(Message* message, nint window, uint first, uint last);

    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessage(uint thread, uint message, nuint wParam, nint lParam);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();
}
