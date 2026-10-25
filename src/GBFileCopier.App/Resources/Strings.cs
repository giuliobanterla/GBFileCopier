using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;

namespace GBFileCopier.App.Resources;

/// <summary>
/// User interface texts, in the language of <see cref="CultureInfo.CurrentUICulture"/>
/// (Strings.resx, plus Strings.it.resx and the other translations). Each property reads the
/// resource with its own name. Used from XAML with {x:Static res:Strings.Name}.
/// </summary>
public static class Strings
{
    private static readonly ResourceManager Resources = new("GBFileCopier.App.Resources.Strings", typeof(Strings).Assembly);

    internal static ResourceManager ResourceManager => Resources;

    /// <summary>Files and folders</summary>
    public static string FilesAndFolders => Get();

    /// <summary>Add files…</summary>
    public static string AddFiles => Get();

    /// <summary>Add folder…</summary>
    public static string AddFolder => Get();

    /// <summary>Paste</summary>
    public static string Paste => Get();

    /// <summary>Paste the files copied in the Finder or Explorer (⌘V / Ctrl+V). ⌥⌘V pastes them to move.</summary>
    public static string PasteTip => Get();

    /// <summary>Remove</summary>
    public static string Remove => Get();

    /// <summary>Clear</summary>
    public static string Clear => Get();

    /// <summary>Drop files and folders here</summary>
    public static string DropHere => Get();

    /// <summary>or paste what you copied in the Finder / Explorer with ⌘V / Ctrl+V</summary>
    public static string DropHint => Get();

    /// <summary>Destination</summary>
    public static string Destination => Get();

    /// <summary>Folder where the files go (you can also drop it here)</summary>
    public static string DestinationPlaceholder => Get();

    /// <summary>Browse…</summary>
    public static string Browse => Get();

    /// <summary>Verify after copying</summary>
    public static string VerifyAfterCopying => Get();

    /// <summary>Read every copy back from the disk and compare its checksum with the original</summary>
    public static string VerifyTip => Get();

    /// <summary>Checksum</summary>
    public static string Checksum => Get();

    /// <summary>If a file exists</summary>
    public static string IfFileExists => Get();

    /// <summary>Pause</summary>
    public static string Pause => Get();

    /// <summary>Resume</summary>
    public static string Resume => Get();

    /// <summary>Cancel</summary>
    public static string Cancel => Get();

    /// <summary>Move</summary>
    public static string Move => Get();

    /// <summary>Copy</summary>
    public static string Copy => Get();

    /// <summary>Jobs</summary>
    public static string Jobs => Get();

    /// <summary>Clear finished</summary>
    public static string ClearFinished => Get();

    /// <summary>Copy to {0}</summary>
    public static string JobCopyTo => Get();

    /// <summary>Move to {0}</summary>
    public static string JobMoveTo => Get();

    /// <summary>Items: {0}</summary>
    public static string JobItems => Get();

    /// <summary>Waiting: another job is using {0}</summary>
    public static string WaitingForDrive => Get();

    /// <summary>the startup disk</summary>
    public static string StartupDisk => Get();

    /// <summary>Added to the jobs below: you can prepare the next one</summary>
    public static string JobAdded => Get();

    /// <summary>Results</summary>
    public static string Results => Get();

    /// <summary>Show all files</summary>
    public static string ShowAllFiles => Get();

    /// <summary>File already exists</summary>
    public static string ConflictTitle => Get();

    /// <summary>already exists in {0}</summary>
    public static string AlreadyExistsIn => Get();

    /// <summary>Existing</summary>
    public static string Existing => Get();

    /// <summary>New</summary>
    public static string New => Get();

    /// <summary>Do the same for the next conflicts</summary>
    public static string ApplyToNextConflicts => Get();

    /// <summary>Keep both</summary>
    public static string KeepBoth => Get();

    /// <summary>Save the new file as "name (2)"</summary>
    public static string KeepBothTip => Get();

    /// <summary>Skip</summary>
    public static string Skip => Get();

    /// <summary>Replace</summary>
    public static string Replace => Get();

    /// <summary>The new file is more recent</summary>
    public static string NewIsMoreRecent => Get();

    /// <summary>The existing file is more recent</summary>
    public static string ExistingIsMoreRecent => Get();

    /// <summary>Same modification date</summary>
    public static string SameModificationDate => Get();

    /// <summary>{0}  ·  modified {1}</summary>
    public static string SizeAndModified => Get();

    /// <summary>Add files and folders, choose a destination, then Copy or Move</summary>
    public static string IdleStatus => Get();

    /// <summary>Add files</summary>
    public static string PickFilesTitle => Get();

    /// <summary>Add folders</summary>
    public static string PickFoldersTitle => Get();

    /// <summary>Choose the destination folder</summary>
    public static string PickDestinationTitle => Get();

    /// <summary>There are no files on the clipboard: copy them in the Finder or in Explorer first</summary>
    public static string NoFilesOnClipboard => Get();

    /// <summary>Items pasted: {0}. Choose the destination, then Move</summary>
    public static string PastedThenMove => Get();

    /// <summary>Items pasted: {0}. Choose the destination, then Copy</summary>
    public static string PastedThenCopy => Get();

    /// <summary>Paused</summary>
    public static string Paused => Get();

    /// <summary>Cancelling…</summary>
    public static string Cancelling => Get();

    /// <summary>Scanning…</summary>
    public static string Scanning => Get();

    /// <summary>Verifying {0}</summary>
    public static string VerifyingFile => Get();

    /// <summary>Copying {0}</summary>
    public static string CopyingFile => Get();

    /// <summary>Finishing…</summary>
    public static string Finishing => Get();

    /// <summary>File {0} of {1}  ·  {2}/s</summary>
    public static string ProgressDetail => Get();

    /// <summary>{0} left</summary>
    public static string TimeLeft => Get();

    /// <summary>Done</summary>
    public static string Done => Get();

    /// <summary>Finished with errors</summary>
    public static string FinishedWithErrors => Get();

    /// <summary>Cancelled</summary>
    public static string Cancelled => Get();

    /// <summary>Copied: {0} ({1})</summary>
    public static string SummaryCopied => Get();

    /// <summary>Moved: {0} ({1})</summary>
    public static string SummaryMoved => Get();

    /// <summary>verified: {0}</summary>
    public static string SummaryVerified => Get();

    /// <summary>skipped: {0}</summary>
    public static string SummarySkipped => Get();

    /// <summary>failed: {0}</summary>
    public static string SummaryFailed => Get();

    /// <summary>not processed: {0}</summary>
    public static string SummaryNotProcessed => Get();

    /// <summary>Moved</summary>
    public static string StatusMoved => Get();

    /// <summary>Moved, verified</summary>
    public static string StatusMovedVerified => Get();

    /// <summary>Verified</summary>
    public static string StatusVerified => Get();

    /// <summary>Copied</summary>
    public static string StatusCopied => Get();

    /// <summary>Skipped</summary>
    public static string StatusSkipped => Get();

    /// <summary>Failed</summary>
    public static string StatusFailed => Get();

    /// <summary>Not processed</summary>
    public static string StatusNotProcessed => Get();

    /// <summary>Folder</summary>
    public static string Folder => Get();

    /// <summary>Not found</summary>
    public static string NotFound => Get();

    /// <summary>xxHash3 (fastest)</summary>
    public static string HashFastest => Get();

    /// <summary>None</summary>
    public static string HashNone => Get();

    /// <summary>Ask</summary>
    public static string ConflictAsk => Get();

    /// <summary>Overwrite if newer</summary>
    public static string ConflictOverwriteIfNewer => Get();

    /// <summary>Overwrite</summary>
    public static string ConflictOverwrite => Get();

    /// <summary>Skip</summary>
    public static string ConflictSkip => Get();

    /// <summary>Keep both</summary>
    public static string ConflictKeepBoth => Get();

    /// <summary>File</summary>
    public static string MenuFile => Get();

    /// <summary>Help</summary>
    public static string MenuHelp => Get();

    /// <summary>Paste Files</summary>
    public static string MenuPasteFiles => Get();

    /// <summary>Paste Files to Move</summary>
    public static string MenuPasteFilesToMove => Get();

    /// <summary>Settings…</summary>
    public static string MenuSettings => Get();

    /// <summary>Exit</summary>
    public static string MenuExit => Get();

    /// <summary>About GBFileCopier</summary>
    public static string MenuAbout => Get();

    /// <summary>GBFileCopier on GitHub</summary>
    public static string MenuWebsite => Get();

    /// <summary>Settings</summary>
    public static string SettingsTitle => Get();

    /// <summary>Language</summary>
    public static string Language => Get();

    /// <summary>Same as the system</summary>
    public static string LanguageSystem => Get();

    /// <summary>The new language is used the next time you open GBFileCopier.</summary>
    public static string LanguageRestart => Get();

    /// <summary>Settings are saved in {0}</summary>
    public static string SettingsLocation => Get();

    /// <summary>Close</summary>
    public static string Close => Get();

    /// <summary>About GBFileCopier</summary>
    public static string AboutTitle => Get();

    /// <summary>Copy and move files with integrity verification.</summary>
    public static string AboutDescription => Get();

    /// <summary>Version {0}</summary>
    public static string AboutVersion => Get();

    /// <summary>Free software released under the GNU General Public License, version 3 or later.</summary>
    public static string AboutLicense => Get();

    /// <summary>Theme</summary>
    public static string Theme => Get();

    /// <summary>Same as the system</summary>
    public static string ThemeSystem => Get();

    /// <summary>Light</summary>
    public static string ThemeLight => Get();

    /// <summary>Dark</summary>
    public static string ThemeDark => Get();

    /// <summary>Check for new versions of GBFileCopier automatically? The app asks GitHub once a day and sends nothing else.</summary>
    public static string UpdateAskAutomatic => Get();

    /// <summary>Yes, check</summary>
    public static string UpdateYes => Get();

    /// <summary>No, thanks</summary>
    public static string UpdateNo => Get();

    /// <summary>Checking for updates…</summary>
    public static string UpdateChecking => Get();

    /// <summary>GBFileCopier {0} is the latest version</summary>
    public static string UpdateUpToDate => Get();

    /// <summary>GBFileCopier {0} is available (you have {1})</summary>
    public static string UpdateAvailable => Get();

    /// <summary>Install and restart</summary>
    public static string UpdateInstall => Get();

    /// <summary>Download…</summary>
    public static string UpdateDownloadPage => Get();

    /// <summary>What's new</summary>
    public static string UpdateWhatsNew => Get();

    /// <summary>Later</summary>
    public static string UpdateLater => Get();

    /// <summary>Downloading GBFileCopier {0}…</summary>
    public static string UpdateDownloading => Get();

    /// <summary>Finish or cancel the jobs, then install</summary>
    public static string UpdateWaitForJobs => Get();

    /// <summary>Installing GBFileCopier {0}: the app restarts by itself</summary>
    public static string UpdateInstalling => Get();

    /// <summary>The update did not work: {0}</summary>
    public static string UpdateFailed => Get();

    /// <summary>Cannot check for updates: {0}</summary>
    public static string UpdateCheckFailed => Get();

    /// <summary>the release has no checksum for this file</summary>
    public static string UpdateNoChecksums => Get();

    /// <summary>the downloaded file is damaged (different checksum)</summary>
    public static string UpdateChecksumMismatch => Get();

    /// <summary>the downloaded app is not signed by the GBFileCopier developer</summary>
    public static string UpdateNotSigned => Get();

    /// <summary>the downloaded app is not the expected version</summary>
    public static string UpdateWrongVersion => Get();

    /// <summary>the release has no package for this system</summary>
    public static string UpdateNoPackage => Get();

    /// <summary>the installation did not complete (code {0})</summary>
    public static string UpdateNotCompleted => Get();

    /// <summary>the installation could not start</summary>
    public static string UpdateFailedGeneric => Get();

    /// <summary>Check for Updates…</summary>
    public static string MenuCheckForUpdates => Get();

    /// <summary>Check for updates automatically (once a day, on GitHub)</summary>
    public static string SettingsCheckForUpdates => Get();

    private static string Get([CallerMemberName] string name = "") =>
        Resources.GetString(name, CultureInfo.CurrentUICulture) ?? name;
}
