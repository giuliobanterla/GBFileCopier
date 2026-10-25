using System.Security.Cryptography;
using GBFileCopier.App.Resources;

namespace GBFileCopier.App.Services.Updates;

/// <summary>An update that cannot go on; the message is for the user.</summary>
public sealed class UpdateException(string message, Exception? innerException = null) : Exception(message, innerException);

/// <summary>Downloads a release file and checks it against the release's SHA256SUMS.txt.</summary>
public sealed class UpdateDownloader(HttpClient http)
{
    public const string ChecksumsFile = "SHA256SUMS.txt";

    /// <summary>Downloads <paramref name="asset"/> into <paramref name="folder"/> and checks its SHA-256.</summary>
    /// <param name="progress">From 0 to 1.</param>
    /// <returns>The path of the downloaded file.</returns>
    /// <exception cref="UpdateException">The checksums are missing or do not match.</exception>
    /// <exception cref="HttpRequestException">The network or GitHub failed.</exception>
    public async Task<string> DownloadAsync(ReleaseInfo release, ReleaseAsset asset, string folder, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(asset);
        var sums = release.Find(ChecksumsFile) ?? throw new UpdateException(Strings.UpdateNoChecksums);
        var expected = ParseChecksums(await http.GetStringAsync(sums.Url, cancellationToken))
            .GetValueOrDefault(asset.Name) ?? throw new UpdateException(Strings.UpdateNoChecksums);

        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, asset.Name);
        using (var response = await http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? asset.Size;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                done += read;
                if (total > 0)
                {
                    progress?.Report(Math.Min(1, (double)done / total));
                }
            }
        }

        if (!string.Equals(await HashAsync(path, cancellationToken), expected, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(path);
            throw new UpdateException(Strings.UpdateChecksumMismatch);
        }

        return path;
    }

    /// <summary>Lines like "a6b5…79a7b  GBFileCopier-0.3.1-windows-x64-setup.exe", as written by sha256sum.</summary>
    public static Dictionary<string, string> ParseChecksums(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var sums = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[0].Length == 64)
            {
                sums[parts[1].TrimStart('*')] = parts[0]; // "*" marks binary mode in sha256sum output
            }
        }

        return sums;
    }

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
    }
}
