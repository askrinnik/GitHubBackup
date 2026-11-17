using System.CommandLine;
using System.Reflection;
using GitHubBackup.TestUtilities;

namespace GitHubBackup.TestBench;

/// <summary>
/// Parses the command line of the test bench and runs it.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Runs the test bench.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>0 on success, 1 when the bench cannot start, 2 for invalid arguments.</returns>
    private static async Task<int> Main(string[] args)
    {
        var scenario = new Option<string>("--scenario")
        {
            Description = "The scenario to start: " + string.Join(", ", UiScenarios.All.Select(item => item.Name)) + ".",
            DefaultValueFactory = _ => UiScenarios.MissingRepository.Name,
        };
        scenario.AcceptOnlyFromAmong([.. UiScenarios.All.Select(item => item.Name)]);
        var theme = new Option<string?>("--theme")
        {
            Description = "The theme of the app: light, dark or system; by default the app follows Windows.",
        };
        theme.AcceptOnlyFromAmong("light", "dark", "system");
        var summaryHeight = new Option<double?>("--summary-height")
        {
            Description = "The saved height of the results panel in device-independent pixels, such as its minimum 80.",
        };
        var appPath = new Option<string?>("--app-path")
        {
            Description = "The folder to copy the desktop app from; by default the build output of GitHubBackup.App.",
        };
        var noLaunch = new Option<bool>("--no-launch")
        {
            Description = "Prepare the folder and the stub and print the paths, without starting the app.",
        };
        var keep = new Option<bool>("--keep")
        {
            Description = "Leave the temporary folder in place when the bench stops.",
        };
        var list = new Option<bool>("--list")
        {
            Description = "List the scenarios and exit.",
        };

        var root = new RootCommand(
            "Starts a temporary copy of the GitHubBackup desktop app against a stubbed GitHub API, with a test token "
            + "and a scenario; the real installation and the real token are never used.")
        {
            scenario, theme, summaryHeight, appPath, noLaunch, keep, list,
        };
        root.SetAction(async (parseResult, cancellationToken) =>
        {
            if (parseResult.GetValue(list))
            {
                foreach (var item in UiScenarios.All)
                {
                    Console.WriteLine($"{item.Name,-20} {item.Description}");
                }

                return 0;
            }

            var settings = new BenchSettings(
                UiScenarios.Find(parseResult.GetValue(scenario)!)!,
                Path.GetFullPath(parseResult.GetValue(appPath) ?? DefaultAppPath()),
                new UiSettingsJson { Theme = parseResult.GetValue(theme), SummaryPanelHeight = parseResult.GetValue(summaryHeight) },
                Launch: !parseResult.GetValue(noLaunch),
                Keep: parseResult.GetValue(keep));
            return await BenchSession.RunAsync(settings, cancellationToken);
        });

        // Ctrl+C cancels the token of the action; the bench then stops the app and the stub and deletes its folder,
        // which can take longer than the default grace period.
        var configuration = new InvocationConfiguration { ProcessTerminationTimeout = TimeSpan.FromSeconds(30) };
        return await root.Parse(args).InvokeAsync(configuration);
    }

    /// <summary>
    /// Returns the build output of the WPF project that the bench was built with.
    /// </summary>
    /// <returns>The absolute path of the folder.</returns>
    private static string DefaultAppPath() =>
        typeof(Program).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "GitHubBackupAppOutputPath")
            .Value!;
}
