namespace GBFileCopier.App.Services.Updates;

/// <summary>
/// A release version as in the tags: 1.2.3, optionally with a prerelease suffix (1.2.3-beta.1).
/// Build metadata after "+" (the commit, in AppInfo.Version) is ignored.
/// </summary>
public sealed record UpdateVersion(Version Number, string Prerelease) : IComparable<UpdateVersion>
{
    public bool IsPrerelease => Prerelease.Length > 0;

    /// <summary>"v1.2.3", "1.2.3-beta.1", "0.3.1+8d85fea…"; null if it is not a version.</summary>
    public static UpdateVersion? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var value = text.Trim().TrimStart('v', 'V');
        var plus = value.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            value = value[..plus];
        }

        var dash = value.IndexOf('-', StringComparison.Ordinal);
        var number = dash >= 0 ? value[..dash] : value;
        var prerelease = dash >= 0 ? value[(dash + 1)..] : string.Empty;
        return Version.TryParse(number, out var parsed) && parsed.Major >= 0
            ? new UpdateVersion(new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0)), prerelease)
            : null;
    }

    /// <summary>1.2.3-beta &lt; 1.2.3 &lt; 1.2.4; prerelease labels are compared as text.</summary>
    public int CompareTo(UpdateVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var byNumber = Number.CompareTo(other.Number);
        if (byNumber != 0)
        {
            return byNumber;
        }

        return (IsPrerelease, other.IsPrerelease) switch
        {
            (false, false) => 0,
            (true, false) => -1,
            (false, true) => 1,
            _ => string.CompareOrdinal(Prerelease, other.Prerelease),
        };
    }

    public static bool operator >(UpdateVersion left, UpdateVersion right) => left.CompareTo(right) > 0;

    public static bool operator <(UpdateVersion left, UpdateVersion right) => left.CompareTo(right) < 0;

    public static bool operator >=(UpdateVersion left, UpdateVersion right) => left.CompareTo(right) >= 0;

    public static bool operator <=(UpdateVersion left, UpdateVersion right) => left.CompareTo(right) <= 0;

    public override string ToString() =>
        Number.ToString(3) + (IsPrerelease ? "-" + Prerelease : string.Empty);

    /// <summary>The version of this app; GBFILECOPIER_PRETEND_VERSION overrides it, to try an update.</summary>
    public static UpdateVersion Current { get; } =
        Parse(Environment.GetEnvironmentVariable("GBFILECOPIER_PRETEND_VERSION"))
        ?? Parse(Core.AppInfo.Version)
        ?? new UpdateVersion(new Version(0, 0, 0), string.Empty);
}
