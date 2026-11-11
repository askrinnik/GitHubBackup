using GitHubBackup.Cli;
using GitHubBackup.Infrastructure.Hosting;
using Spectre.Console;

var application = new CliApplication(new CliConsole(AnsiConsole.Console, Console.Error));
var exitCode = await application.RunAsync(GitHubBackupHostSettings.ForCurrentProcess(args), CancellationToken.None);
return (int)exitCode;
