using GitHubBackup.Cli;
using GitHubBackup.Infrastructure.Hosting;

var application = new CliApplication(Console.Error);
var exitCode = await application.RunAsync(GitHubBackupHostSettings.ForCurrentProcess(args), CancellationToken.None);
return (int)exitCode;
