using System.Diagnostics;
using GitHubBackup.Infrastructure.Hosting;
using GitHubBackup.TestUtilities;
using GitHubBackup.TestUtilities.Git;

namespace GitHubBackup.TestBench;

/// <summary>
/// Prepares a temporary installation of the desktop app with a running GitHub API stub, starts the app, waits until
/// the developer is done and removes everything again.
/// </summary>
/// <remarks>
/// The installation folder is the content root of the app, so its settings, configuration, log and run history all
/// stay inside it. <c>https://github.com/</c> is redirected to an empty local folder, so a run that reaches git fails
/// at once instead of contacting GitHub.
/// </remarks>
internal static class BenchSession
{
    /// <summary>The name of the executable in the build output of the WPF project.</summary>
    private const string _executableName = "GitHubBackup.App.exe";

    /// <summary>
    /// Runs the bench until the app exits, Enter is pressed or <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <param name="settings">What to prepare.</param>
    /// <param name="cancellationToken">Cancelled by Ctrl+C.</param>
    /// <returns>0 when the bench ran, 1 when the folder to copy holds no app.</returns>
    public static async Task<int> RunAsync(BenchSettings settings, CancellationToken cancellationToken)
    {
        var folder = TemporaryFolder.Create();
        using var otlpHeadersCredential = new OtlpHeadersTestCredential();
        Process? app = null;
        try
        {
            using var api = new GitHubApiStub();
            try
            {
                BuildOutput.CopyTo(settings.AppPath, folder, _executableName);
            }
            catch (InvalidOperationException exception)
            {
                await Console.Error.WriteLineAsync(exception.Message);
                return 1;
            }

            await File.WriteAllTextAsync(
                Path.Combine(folder, GitHubBackupHostBuilder.AppSettingsFileName), AppSettingsJson.Valid(api.Url), cancellationToken);
            var uiSettings = settings.UiSettings.Build();
            if (uiSettings != new UiSettingsJson().Build())
            {
                await File.WriteAllTextAsync(
                    Path.Combine(folder, GitHubBackupHostBuilder.UiSettingsFileName), uiSettings, cancellationToken);
            }

            settings.Scenario.Prepare(api, Path.Combine(folder, "archives"), Path.Combine(folder, "backup-config.json"));
            var remotes = Directory.CreateDirectory(Path.Combine(folder, "remotes")).FullName;
            var environment = new Dictionary<string, string>(
                new GitRemoteRedirect(folder, remotes).EnvironmentVariables(), StringComparer.OrdinalIgnoreCase);

            // The app keeps the OTLP headers of the bench in a credential of its own, deleted when the bench stops.
            otlpHeadersCredential.AddTo(environment);
            var executable = Path.Combine(folder, _executableName);

            Console.WriteLine($"Scenario:   {settings.Scenario.Name}");
            Console.WriteLine($"            {settings.Scenario.Description}");
            Console.WriteLine($"Folder:     {folder}");
            Console.WriteLine($"Executable: {executable}");
            Console.WriteLine($"API stub:   {api.Url}");
            Console.WriteLine("Settings:   appsettings.json holds the stub address and a test token that grants nothing.");
            if (settings.Launch)
            {
                app = Process.Start(IsolatedAppProcess.Create(executable, folder, environment))!;
                Console.WriteLine("The app is running. Close it, press Enter or Ctrl+C to stop the bench.");
            }
            else
            {
                Console.WriteLine("Start the executable from its folder with these variables, so that git stays local and the user's credentials stay untouched:");
                foreach (var (name, value) in environment)
                {
                    Console.WriteLine($"            {name}={value}");
                }

                Console.WriteLine("Press Enter or Ctrl+C to stop the stub.");
            }

            await WaitAsync(app, cancellationToken);
            return 0;
        }
        finally
        {
            if (app is { HasExited: false })
            {
                app.Kill(entireProcessTree: true);
                await app.WaitForExitAsync(CancellationToken.None);
            }

            app?.Dispose();
            if (settings.Keep)
            {
                Console.WriteLine($"Kept {folder}");
            }
            else
            {
                TemporaryFolder.Delete(folder);
                Console.WriteLine($"Deleted {folder}");
            }
        }
    }

    /// <summary>
    /// Waits until the app exits, Enter is pressed or <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <remarks>
    /// Without a console input, as in a background job, reading a line ends at once; the bench then waits for the
    /// other two.
    /// </remarks>
    /// <param name="app">The process of the app, or <see langword="null"/> when the bench did not start it.</param>
    /// <param name="cancellationToken">Cancelled by Ctrl+C.</param>
    /// <returns>A task that completes when the bench should stop.</returns>
    private static async Task WaitAsync(Process? app, CancellationToken cancellationToken)
    {
        var stop = new TaskCompletionSource();
        await using var registration = cancellationToken.Register(() => stop.TrySetResult());
        var enter = Task.Run(Console.ReadLine, CancellationToken.None);
        List<Task> waits = [stop.Task, enter];
        if (app is not null)
        {
            waits.Add(app.WaitForExitAsync(CancellationToken.None));
        }

        while (true)
        {
            var completed = await Task.WhenAny(waits);
            if (completed == enter && await enter is null)
            {
                waits.Remove(enter);
                continue;
            }

            if (app is { HasExited: true })
            {
                Console.WriteLine($"The app exited with code {app.ExitCode}.");
            }

            return;
        }
    }
}
