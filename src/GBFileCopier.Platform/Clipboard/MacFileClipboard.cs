using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace GBFileCopier.Platform.Clipboard;

/// <summary>
/// NSPasteboard through the Objective-C runtime, without Swift or bindings. Each call is
/// objc_msgSend declared with the exact signature of the method, which is what the runtime
/// requires (objc_msgSend must never be called as a variadic function).
/// </summary>
/// <param name="pasteboardName">A private pasteboard instead of the general one (used by the tests).</param>
[SupportedOSPlatform("macos")]
internal sealed partial class MacFileClipboard(string? pasteboardName = null)
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    private static readonly nint AppKit;

    // A static constructor, not a field initializer: it runs before any use of the class,
    // while an initializer may run only when the field itself is first read. Loading AppKit
    // registers NSPasteboard with the runtime, otherwise objc_getClass returns nil.
    static MacFileClipboard()
    {
        AppKit = NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
    }

    public ClipboardFiles? Read()
    {
        var pool = objc_autoreleasePoolPush();
        try
        {
            // readObjectsForClasses:@[NSURL.class] options:@{NSPasteboardURLReadingFileURLsOnlyKey: @YES}
            var classes = Send(Class("NSArray"), Sel("arrayWithObject:"), Class("NSURL"));
            var yes = SendBool(Class("NSNumber"), Sel("numberWithBool:"), 1);
            var fileUrlsOnlyKey = Marshal.ReadIntPtr(NativeLibrary.GetExport(AppKit, "NSPasteboardURLReadingFileURLsOnlyKey"));
            var options = Send(Class("NSDictionary"), Sel("dictionaryWithObject:forKey:"), yes, fileUrlsOnlyKey);
            var urls = Send(Pasteboard(), Sel("readObjectsForClasses:options:"), classes, options);
            if (urls == 0)
            {
                return null;
            }

            var paths = new List<string>();
            var count = SendCount(urls, Sel("count"));
            for (nuint i = 0; i < count; i++)
            {
                var url = SendIndex(urls, Sel("objectAtIndex:"), i);
                // The Finder often copies file reference URLs (file:///.file/id=...): filePathURL resolves them.
                // A plain path URL goes through a file reference too: NSURL returns names in decomposed
                // Unicode (NFD), while the round trip through the file ID gives the name as stored on
                // disk, so that the copy gets exactly the same name.
                var reference = Send(url, Sel("fileReferenceURL"));
                var fileUrl = Send(reference != 0 ? reference : url, Sel("filePathURL"));
                var path = fileUrl == 0 ? null : ToManagedString(Send(fileUrl, Sel("path")));
                if (path is not null)
                {
                    paths.Add(path);
                }
            }

            return paths.Count == 0 ? null : new ClipboardFiles(paths, IsCut: false);
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    /// <param name="paths">Full paths.</param>
    /// <param name="asFileReferences">Write file reference URLs (file:///.file/id=...), as the Finder does.</param>
    public void Write(IReadOnlyList<string> paths, bool asFileReferences = false)
    {
        var pool = objc_autoreleasePoolPush();
        try
        {
            var urls = Send(Class("NSMutableArray"), Sel("array"));
            foreach (var path in paths)
            {
                var url = Send(Class("NSURL"), Sel("fileURLWithPath:"), NSString(path));
                if (asFileReferences)
                {
                    url = Send(url, Sel("fileReferenceURL"));
                }

                Send(urls, Sel("addObject:"), url);
            }

            var pasteboard = Pasteboard();
            Send(pasteboard, Sel("clearContents"));
            if (SendReturnsBool(pasteboard, Sel("writeObjects:"), urls) == 0)
            {
                throw new IOException("Cannot write to the clipboard");
            }
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    public void Clear()
    {
        var pool = objc_autoreleasePoolPush();
        try
        {
            Send(Pasteboard(), Sel("clearContents"));
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    /// <summary>Removes a private pasteboard from the system.</summary>
    public void Release()
    {
        var pool = objc_autoreleasePoolPush();
        try
        {
            Send(Pasteboard(), Sel("releaseGlobally"));
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    private nint Pasteboard() => pasteboardName is null
        ? Send(Class("NSPasteboard"), Sel("generalPasteboard"))
        : Send(Class("NSPasteboard"), Sel("pasteboardWithName:"), NSString(pasteboardName));

    private static nint Class(string name)
    {
        var objcClass = objc_getClass(name);
        return objcClass != 0 ? objcClass : throw new PlatformNotSupportedException($"Objective-C class {name} not found");
    }

    private static nint Sel(string name) => sel_registerName(name);

    private static nint NSString(string value) => SendUtf8(Class("NSString"), Sel("stringWithUTF8String:"), value);

    private static string? ToManagedString(nint nsString) =>
        nsString == 0 ? null : Marshal.PtrToStringUTF8(Send(nsString, Sel("UTF8String")));

    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint objc_getClass(string name);

    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint sel_registerName(string name);

    [LibraryImport(ObjC)]
    private static partial nint objc_autoreleasePoolPush();

    [LibraryImport(ObjC)]
    private static partial void objc_autoreleasePoolPop(nint pool);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector, nint argument);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector, nint argument1, nint argument2);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint SendBool(nint receiver, nint selector, byte argument);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial byte SendReturnsBool(nint receiver, nint selector, nint argument);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nuint SendCount(nint receiver, nint selector);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint SendIndex(nint receiver, nint selector, nuint index);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint SendUtf8(nint receiver, nint selector, string argument);
}
