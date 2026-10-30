using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;

namespace GBFileCopier.Core.Resources;

/// <summary>
/// Engine messages shown to the user, in the language of <see cref="CultureInfo.CurrentUICulture"/>
/// (Messages.resx, plus Messages.it.resx and the other translations). Each property reads the
/// resource with its own name.
/// </summary>
public static class Messages
{
    private static readonly ResourceManager Resources = new("GBFileCopier.Core.Resources.Messages", typeof(Messages).Assembly);

    internal static ResourceManager ResourceManager => Resources;

    /// <summary>Copying a whole drive is not supported: select its contents instead</summary>
    public static string DriveNotSupported => Get();

    /// <summary>Source and destination are the same</summary>
    public static string SameSourceAndDestination => Get();

    /// <summary>The destination is inside the source folder</summary>
    public static string DestinationInsideSource => Get();

    /// <summary>The source does not exist</summary>
    public static string SourceDoesNotExist => Get();

    /// <summary>The folder cannot be read: {0}</summary>
    public static string FolderCannotBeRead => Get();

    /// <summary>The destination already exists</summary>
    public static string DestinationExists => Get();

    /// <summary>The source file changed while it was being copied</summary>
    public static string SourceChanged => Get();

    /// <summary>Verification failed: the copy does not match the source</summary>
    public static string VerificationFailed => Get();

    /// <summary>A folder with the same name already exists</summary>
    public static string FolderExistsWithSameName => Get();

    /// <summary>Copied, but the source could not be deleted: {0}</summary>
    public static string SourceNotDeleted => Get();

    /// <summary>Not all metadata could be copied ({0})</summary>
    public static string MetadataNotCopied => Get();

    /// <summary>permissions</summary>
    public static string MetadataPermissions => Get();

    /// <summary>modified time</summary>
    public static string MetadataModifiedTime => Get();

    /// <summary>creation time</summary>
    public static string MetadataCreationTime => Get();

    /// <summary>attributes</summary>
    public static string MetadataAttributes => Get();

    /// <summary>The copies on {0} may not be on the disk yet: {1}</summary>
    public static string FilesNotFlushed => Get();

    /// <summary>Cannot flush the file to disk</summary>
    public static string CannotFlushToDisk => Get();

    /// <summary>The checksum algorithm is unknown: name the file .sha256, .md5, .sfv…</summary>
    public static string ChecksumFileUnknownAlgorithm => Get();

    /// <summary>The file contains no {0} checksums</summary>
    public static string ChecksumFileEmpty => Get();

    /// <summary>The file does not exist</summary>
    public static string ChecksumFileMissing => Get();

    /// <summary>The checksum does not match: expected {0}, found {1}</summary>
    public static string ChecksumMismatch => Get();

    /// <summary>The checksum file could not be written: {0}</summary>
    public static string ChecksumFileNotWritten => Get();

    private static string Get([CallerMemberName] string name = "") =>
        Resources.GetString(name, CultureInfo.CurrentUICulture) ?? name;
}
