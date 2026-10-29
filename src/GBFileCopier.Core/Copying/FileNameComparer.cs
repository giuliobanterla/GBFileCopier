using System.Text;

namespace GBFileCopier.Core.Copying;

/// <summary>
/// Compares paths the way the file systems of Windows and macOS do: without case, and on macOS also
/// without Unicode normalization ("café" typed as é or as e + ´ is the same name on APFS).
/// </summary>
internal sealed class FileNameComparer : IEqualityComparer<string>
{
    public static FileNameComparer Instance { get; } = new();

    public bool Equals(string? x, string? y) => DestinationDrive.KeyComparer.Equals(Normalize(x), Normalize(y));

    public int GetHashCode(string obj) => DestinationDrive.KeyComparer.GetHashCode(Normalize(obj)!);

    private static string? Normalize(string? path) =>
        path is not null && OperatingSystem.IsMacOS() && !path.IsNormalized(NormalizationForm.FormC) ? path.Normalize(NormalizationForm.FormC) : path;
}
