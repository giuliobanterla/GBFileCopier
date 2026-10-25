using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace GBFileCopier.Platform;

/// <summary>Authenticode signatures of Windows executables, as Windows itself checks them.</summary>
[SupportedOSPlatform("windows")]
public static unsafe partial class WindowsAuthenticode
{
    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_NONE = 0;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_STATEACTION_VERIFY = 1;
    private const uint WTD_STATEACTION_CLOSE = 2;

    // WINTRUST_ACTION_GENERIC_VERIFY_V2
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    /// <summary>
    /// The name (CN) of whoever signed <paramref name="path"/>, for example "SignPath Foundation",
    /// if the signature is valid and its chain trusted; otherwise null. Only the name: a renewed
    /// certificate may change the other parts of the subject.
    /// </summary>
    public static string? TrustedSigner(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!IsTrusted(path))
        {
            return null;
        }

        try
        {
#pragma warning disable SYSLIB0057 // the only API that reads the signer of a signed file; the signature was verified above
            using var certificate = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            return new X500DistinguishedName(certificate.Subject)
                .EnumerateRelativeDistinguishedNames()
                .FirstOrDefault(n => n.GetSingleElementType().Value == "2.5.4.3")? // CN
                .GetSingleElementValue();
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static bool IsTrusted(string path)
    {
        fixed (char* filePath = path)
        {
            var file = new WintrustFileInfo
            {
                Size = (uint)sizeof(WintrustFileInfo),
                FilePath = filePath,
            };
            var data = new WintrustData
            {
                Size = (uint)sizeof(WintrustData),
                UiChoice = WTD_UI_NONE,
                RevocationChecks = WTD_REVOKE_NONE,
                UnionChoice = WTD_CHOICE_FILE,
                File = &file,
                StateAction = WTD_STATEACTION_VERIFY,
            };
            var action = GenericVerifyV2;
            var result = WinVerifyTrust(-1, &action, &data); // -1: no window, never ask the user
            data.StateAction = WTD_STATEACTION_CLOSE;
            _ = WinVerifyTrust(-1, &action, &data);
            return result == 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WintrustFileInfo
    {
        public uint Size;
        public char* FilePath;
        public nint File;
        public Guid* KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WintrustData
    {
        public uint Size;
        public nint PolicyCallbackData;
        public nint SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public WintrustFileInfo* File;
        public uint StateAction;
        public nint StateData;
        public char* UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public nint SignatureSettings;
    }

    [LibraryImport("wintrust.dll")]
    private static partial int WinVerifyTrust(nint window, Guid* action, WintrustData* data);
}
