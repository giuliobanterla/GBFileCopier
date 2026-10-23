using System.CommandLine;
using System.CommandLine.Parsing;
using GBFileCopier.Cli;
using GBFileCopier.Core;
using GBFileCopier.Core.Copying;
using GBFileCopier.Core.Hashing;
using GBFileCopier.Platform;

const int ExitFailures = 1;
const int ExitCancelled = 3;

var hashOption = new Option<HashAlgorithmKind?>("--hash")
{
    Description = $"Checksum algorithm: {string.Join(", ", HashAlgorithmNames.All)} (default: xxh3)",
    HelpName = "algorithm",
    CustomParser = ParseHashAlgorithm,
};

var verifyOption = new Option<bool>("--verify", "-v")
{
    Description = "Read every copy back and compare its checksum with the source",
};

var conflictOption = new Option<ConflictPolicy?>("--on-conflict", "-c")
{
    Description = "When the destination exists: ask, overwrite, newer, skip, keep-both (default: ask, or skip when not interactive)",
    HelpName = "policy",
    CustomParser = ParseConflictPolicy,
};

var retriesOption = new Option<int>("--retries")
{
    Description = "Attempts after a transient error",
    DefaultValueFactory = _ => 2,
};

var noFlushOption = new Option<bool>("--no-flush")
{
    Description = "Don't force each file to disk before going on (faster, less safe)",
};

var pathsArgument = new Argument<string[]>("paths")
{
    Description = "One or more sources (files or folders), followed by the destination folder",
    Arity = new ArgumentArity(2, 100_000),
};

var rootCommand = new RootCommand($"{AppInfo.Name}: copy and move files with integrity verification");
rootCommand.Subcommands.Add(CreateTransferCommand("copy", CopyMode.Copy, "Copy files and folders into a destination folder"));
rootCommand.Subcommands.Add(CreateTransferCommand("move", CopyMode.Move, "Move files and folders: each source is deleted only after its copy has succeeded"));
rootCommand.Subcommands.Add(CreateHashCommand());

var infoCommand = new Command("info", "Show version and platform information");
infoCommand.SetAction(_ =>
{
    Console.WriteLine($"{AppInfo.Name} {AppInfo.Version}");
    Console.WriteLine(PlatformInfo.Description);
});
rootCommand.Subcommands.Add(infoCommand);

return await rootCommand.Parse(args).InvokeAsync();

Command CreateTransferCommand(string name, CopyMode mode, string description)
{
    var command = new Command(name, description)
    {
        pathsArgument, verifyOption, hashOption, conflictOption, retriesOption, noFlushOption,
    };
    command.SetAction((parseResult, cancellationToken) => RunTransferAsync(parseResult, mode, cancellationToken));
    return command;
}

async Task<int> RunTransferAsync(ParseResult parseResult, CopyMode mode, CancellationToken cancellationToken)
{
    var paths = parseResult.GetValue(pathsArgument)!;
    var ui = new ConsoleUi();
    var interactive = !Console.IsInputRedirected && !Console.IsOutputRedirected;

    var request = new CopyRequest
    {
        Sources = paths[..^1],
        Destination = paths[^1],
        Mode = mode,
        Options = new CopyOptions
        {
            HashAlgorithm = parseResult.GetValue(hashOption) ?? HashAlgorithmKind.XxHash3,
            Verify = parseResult.GetValue(verifyOption),
            ConflictPolicy = parseResult.GetValue(conflictOption) ?? (interactive ? ConflictPolicy.Ask : ConflictPolicy.Skip),
            RetryCount = parseResult.GetValue(retriesOption),
            FlushToDisk = !parseResult.GetValue(noFlushOption),
        },
    };

    var result = await new CopyJob(request).RunAsync(ui, ui.ResolveConflictAsync, cancellationToken);
    PrintSummary(ui, result);

    return result.WasCancelled ? ExitCancelled
        : result.FailedCount > 0 ? ExitFailures
        : 0;
}

static void PrintSummary(ConsoleUi ui, CopyJobResult result)
{
    foreach (var file in result.Files.Where(f => f.Status == FileCopyStatus.Failed))
    {
        ui.WriteLine($"FAILED  {file.SourcePath}: {file.Error}");
    }

    foreach (var file in result.Files.Where(f => f.Warning is not null))
    {
        ui.WriteLine($"WARNING {file.DestinationPath}: {file.Warning}");
    }

    var verb = result.Mode == CopyMode.Move ? "moved" : "copied";
    var verified = result.Files.Count(f => f.Verified);
    var speed = result.Elapsed.TotalSeconds > 0 ? result.CopiedBytes / result.Elapsed.TotalSeconds : 0;

    ui.WriteLine(string.Empty);
    ui.WriteLine(
        $"{result.SucceededCount} {verb} ({ByteSize.Format(result.CopiedBytes)}), " +
        $"{verified} verified, {result.SkippedCount} skipped, {result.FailedCount} failed" +
        (result.CancelledCount > 0 ? $", {result.CancelledCount} not processed" : string.Empty));
    ui.WriteLine($"Time {result.Elapsed:hh\\:mm\\:ss}, {ByteSize.Format(speed)}/s, checksum {HashAlgorithmNames.GetName(result.HashAlgorithm)}");
    if (result.WasCancelled)
    {
        ui.WriteLine("Cancelled.");
    }
}

Command CreateHashCommand()
{
    var filesArgument = new Argument<string[]>("files")
    {
        Description = "Files to hash",
        Arity = ArgumentArity.OneOrMore,
    };
    var command = new Command("hash", "Print the checksum of files, in the same format as sha256sum and similar tools")
    {
        filesArgument, hashOption,
    };
    command.SetAction(async (parseResult, cancellationToken) =>
    {
        var algorithm = parseResult.GetValue(hashOption) ?? HashAlgorithmKind.XxHash3;
        if (algorithm == HashAlgorithmKind.None)
        {
            Console.Error.WriteLine("Choose an algorithm other than 'none'");
            return ExitFailures;
        }

        var exitCode = 0;
        foreach (var file in parseResult.GetValue(filesArgument)!)
        {
            try
            {
                var hash = await FileHasher.HashFileAsync(file, algorithm, cancellationToken);
                Console.WriteLine($"{StreamingHasher.ToHex(hash)}  {file}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"{file}: {ex.Message}");
                exitCode = ExitFailures;
            }
        }

        return exitCode;
    });
    return command;
}

static HashAlgorithmKind? ParseHashAlgorithm(ArgumentResult result)
{
    var text = result.Tokens.Single().Value;
    if (HashAlgorithmNames.TryParse(text, out var kind))
    {
        return kind;
    }

    result.AddError($"Unknown algorithm '{text}'. Available: {string.Join(", ", HashAlgorithmNames.All)}");
    return null;
}

static ConflictPolicy? ParseConflictPolicy(ArgumentResult result)
{
    var text = result.Tokens.Single().Value;
    ConflictPolicy? policy = text.ToUpperInvariant() switch
    {
        "ASK" => ConflictPolicy.Ask,
        "OVERWRITE" => ConflictPolicy.Overwrite,
        "NEWER" => ConflictPolicy.OverwriteIfNewer,
        "SKIP" => ConflictPolicy.Skip,
        "KEEP-BOTH" => ConflictPolicy.KeepBoth,
        _ => null,
    };
    if (policy is null)
    {
        result.AddError($"Unknown policy '{text}'. Available: ask, overwrite, newer, skip, keep-both");
    }

    return policy;
}
