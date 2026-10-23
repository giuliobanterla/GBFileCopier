using GBFileCopier.Core;
using GBFileCopier.Core.Copying;

namespace GBFileCopier.Cli;

/// <summary>
/// Draws the progress line and asks about conflicts. The engine calls both from thread pool
/// threads, so every write to the console goes through one lock.
/// </summary>
internal sealed class ConsoleUi : IProgress<CopyProgress>
{
    private readonly Lock _lock = new();
    private readonly bool _interactive = !Console.IsOutputRedirected;
    private int _lineLength;
    private ConflictAction? _answerForAll;

    public void Report(CopyProgress value)
    {
        if (!_interactive)
        {
            return;
        }

        var phase = value.Phase switch
        {
            CopyPhase.Scanning => "Scanning",
            CopyPhase.Verifying => "Verifying",
            _ => "Copying",
        };
        var line = value.Phase == CopyPhase.Finished
            ? string.Empty
            : $"{value.Fraction,4:P0}  {value.CompletedFiles}/{value.TotalFiles} files  " +
              $"{ByteSize.Format(value.BytesPerSecond)}/s  {phase} {Path.GetFileName(value.CurrentFile)}";

        lock (_lock)
        {
            WriteStatusLine(line);
        }
    }

    public void WriteLine(string text)
    {
        lock (_lock)
        {
            WriteStatusLine(string.Empty);
            Console.WriteLine(text);
        }
    }

    public ValueTask<ConflictAction> ResolveConflictAsync(FileConflict conflict, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_answerForAll is { } remembered)
            {
                return ValueTask.FromResult(remembered);
            }

            WriteStatusLine(string.Empty);
            Console.WriteLine($"Already exists: {conflict.DestinationPath}");
            Console.WriteLine($"  existing: {ByteSize.Format(conflict.DestinationLength),10}  {conflict.DestinationLastWriteTimeUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"  new:      {ByteSize.Format(conflict.SourceLength),10}  {conflict.SourceLastWriteTimeUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}");

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Console.Write("[o]verwrite, [s]kip, [k]eep both (capital letter = for all): ");
                var answer = Console.ReadLine()?.Trim();
                if (answer is null)
                {
                    return ValueTask.FromResult(ConflictAction.Skip); // input closed
                }

                ConflictAction? action = answer.ToUpperInvariant() switch
                {
                    "O" => ConflictAction.Overwrite,
                    "S" => ConflictAction.Skip,
                    "K" => ConflictAction.KeepBoth,
                    _ => null,
                };
                if (action is { } chosen)
                {
                    if (answer.Length == 1 && char.IsUpper(answer[0]))
                    {
                        _answerForAll = chosen;
                    }

                    return ValueTask.FromResult(chosen);
                }
            }
        }
    }

    private void WriteStatusLine(string line)
    {
        if (!_interactive || (line.Length == 0 && _lineLength == 0))
        {
            return;
        }

        var width = Math.Max(Console.WindowWidth - 1, 20);
        if (line.Length > width)
        {
            line = line[..width];
        }

        Console.Write("\r" + line.PadRight(_lineLength) + "\r" + line);
        _lineLength = line.Length;
        if (line.Length == 0)
        {
            Console.Write("\r");
        }
    }
}
