namespace GBFileCopier.App.Services;

/// <summary>
/// A request from outside the app: the Finder extension (or any program) opening a
/// gbfilecopier:// link. Any web page can open such a link too, so a request only fills in the
/// window; it never starts a copy by itself.
/// </summary>
/// <param name="Sources">Files and folders to add to the list.</param>
/// <param name="Destination">Destination folder to set, or null.</param>
/// <param name="PasteFromClipboard">Add the files on the clipboard.</param>
/// <param name="Move">Make Move the default action.</param>
public sealed record ExternalRequest(IReadOnlyList<string> Sources, string? Destination, bool PasteFromClipboard, bool Move)
{
    public const string Scheme = "gbfilecopier";

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

    private static List<KeyValuePair<string, string>> ParseQuery(string query) =>
        [.. query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Select(pair => KeyValuePair.Create(
                Uri.UnescapeDataString(pair[0].Replace('+', ' ')),
                pair.Length > 1 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : string.Empty))];
}
