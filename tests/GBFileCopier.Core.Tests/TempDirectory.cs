namespace GBFileCopier.Core.Tests;

/// <summary>A unique folder for one test, deleted at the end.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gbcopy-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    /// <summary>Creates a file with random content (or the given text) and returns its path.</summary>
    public string CreateFile(string relativePath, int length = 1000, string? text = null)
    {
        var path = Combine(relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        if (text is not null)
        {
            File.WriteAllText(path, text);
        }
        else
        {
            var data = new byte[length];
            Random.Shared.NextBytes(data);
            File.WriteAllBytes(path, data);
        }

        return path;
    }

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
