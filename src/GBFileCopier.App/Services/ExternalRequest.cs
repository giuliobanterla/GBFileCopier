namespace GBFileCopier.App.Services;

/// <summary>
/// A request from outside the app: the Finder extension opening a gbfilecopier:// link, or the
/// Explorer context menu and "Send to" starting the app with arguments. Links can come from any
/// web page too, so a request only fills in the window; it never starts a copy by itself.
/// </summary>
/// <param name="Sources">Files and folders to add to the list.</param>
/// <param name="Destination">Destination folder to set, or null.</param>
/// <param name="PasteFromClipboard">Add the files on the clipboard.</param>
/// <param name="Move">Make Move the default action.</param>
public sealed record ExternalRequest(IReadOnlyList<string> Sources, string? Destination, bool PasteFromClipboard, bool Move)
{
    public const string Scheme = "gbfilecopier";

    /// <summary>Start without showing the window (at sign-in, for Ctrl+V in Explorer); not a request.</summary>
    public const string BackgroundArgument = "--background";

    /// <summary>
    /// gbfilecopier://add?source=/a&amp;source=/b and gbfilecopier://paste?destination=/folder&amp;move=1.
    /// Returns null for anything else, and ignores paths that are not absolute.
    /// </summary>
    public static ExternalRequest? Parse(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var query = ParseQuery(uri.Query);
        string[] Values(string name) => [.. query.Where(p => p.Key == name).Select(p => p.Value).Where(Path.IsPathFullyQualified)];
        var move = query.Any(p => p.Key == "move" && p.Value is "1" or "true");

        return uri.Host.ToUpperInvariant() switch
        {
            "ADD" when Values("source") is { Length: > 0 } sources => new ExternalRequest(sources, null, PasteFromClipboard: false, move),
            "PASTE" => new ExternalRequest([], Values("destination").FirstOrDefault(), PasteFromClipboard: true, move),
            _ => null,
        };
    }

    /// <summary>
    /// Command line, as used by the Explorer context menu and "Send to":
    /// <c>--add path…</c> (or just paths), <c>--paste [folder]</c>, <c>--move</c>, or a gbfilecopier:// link.
    /// Relative paths are resolved against <paramref name="workingDirectory"/>, the directory the
    /// command was started in (which differs from the running instance's one).
    /// </summary>
    /// <returns>Null if the arguments ask for nothing.</returns>
    public static ExternalRequest? FromArguments(IReadOnlyList<string> arguments, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count == 1 && Uri.TryCreate(arguments[0], UriKind.Absolute, out var uri) && uri.Scheme == Scheme)
        {
            return Parse(uri);
        }

        var paths = new List<string>();
        var paste = false;
        var move = false;
        foreach (var argument in arguments)
        {
            switch (argument)
            {
                case "--add":
                    break;
                case "--paste":
                    paste = true;
                    break;
                case "--move":
                    move = true;
                    break;
                case BackgroundArgument:
                    break;
                case "":
                    break;
                default:
                    // Explorer quotes "C:" as "C:"" when the folder is a drive root: drop the stray quote
                    paths.Add(Path.GetFullPath(argument.TrimEnd('"'), workingDirectory));
                    break;
            }
        }

        if (paste)
        {
            return new ExternalRequest([], paths.FirstOrDefault(), PasteFromClipboard: true, move);
        }

        return paths.Count > 0 ? new ExternalRequest(paths, null, PasteFromClipboard: false, move) : null;
    }

    private static List<KeyValuePair<string, string>> ParseQuery(string query) =>
        [.. query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Select(pair => KeyValuePair.Create(
                Uri.UnescapeDataString(pair[0].Replace('+', ' ')),
                pair.Length > 1 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : string.Empty))];
}
