using System.Globalization;

namespace GBFileCopier.Core;

public static class ByteSize
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    /// <summary>Human readable size with binary multiples, for example "1.5 GB".</summary>
    /// <param name="bytes">The size.</param>
    /// <param name="culture">Number format; invariant ("1.5") if null, as on the command line.</param>
    public static string Format(double bytes, CultureInfo? culture = null)
    {
        var unit = 0;
        while (Math.Abs(bytes) >= 1024 && unit < Units.Length - 1)
        {
            bytes /= 1024;
            unit++;
        }

        var format = unit == 0 ? "0" : "0.0";
        return bytes.ToString(format, culture ?? CultureInfo.InvariantCulture) + " " + Units[unit];
    }
}
