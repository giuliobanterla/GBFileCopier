using System.CommandLine;
using GBFileCopier.Core;
using GBFileCopier.Platform;

var rootCommand = new RootCommand($"{AppInfo.Name}: copy and move files with integrity verification");

var infoCommand = new Command("info", "Show version and platform information");
infoCommand.SetAction(_ =>
{
    Console.WriteLine($"{AppInfo.Name} {AppInfo.Version}");
    Console.WriteLine(PlatformInfo.Description);
});
rootCommand.Subcommands.Add(infoCommand);

return rootCommand.Parse(args).Invoke();
