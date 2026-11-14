using System.Reflection;
using GitHubBackup.TestUtilities;

namespace GitHubBackup.Cli.Tests;

/// <summary>
/// Provides a published application folder for the tests of a class: the folder named by
/// <see cref="PublishedAppPathVariable"/>, or a folder that <c>scripts/publish.ps1</c> publishes into a temporary
/// folder and that is deleted on dispose.
/// </summary>
/// <remarks>
/// Tests never run an executable inside <see cref="FolderPath"/>: a run creates the log and history folders next to
/// the executable, which would change the layout the other tests check. They copy the folder first.
/// </remarks>
public sealed class PublishedApplication : IAsyncLifetime
{
    /// <summary>The environment variable that names a folder the build already published, as CI does.</summary>
    public const string PublishedAppPathVariable = "PUBLISHED_APP_PATH";

    /// <summary>The longest time publishing both hosts may take.</summary>
    private static readonly TimeSpan _publishTimeout = TimeSpan.FromMinutes(10);

    /// <summary>The temporary folder that holds the publish output, or <see langword="null"/> for a given folder.</summary>
    private string? _temporaryFolder;

    /// <summary>Gets the absolute path of the repository root.</summary>
    public static string RepositoryRoot { get; } = typeof(PublishedApplication).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == "GitHubBackupRepositoryRoot")
        .Value!;

    /// <summary>Gets the absolute path of the publishing script.</summary>
    public static string ScriptPath => Path.Combine(RepositoryRoot, "scripts", "publish.ps1");

    /// <summary>Gets the absolute path of the published application folder.</summary>
    public string FolderPath { get; private set; } = string.Empty;

    /// <summary>
    /// Runs the publishing script with <c>pwsh</c> and waits for it to exit.
    /// </summary>
    /// <param name="arguments">The arguments of the script.</param>
    /// <param name="cancellationToken">Cancels the run; the process tree is killed.</param>
    /// <returns>The exit code and the output of the script.</returns>
    internal static Task<CliProcessResult> RunScriptAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        CliProcess.RunAsync(
            "pwsh",
            ["-NoProfile", "-NonInteractive", "-File", ScriptPath, .. arguments],
            new Dictionary<string, string>(),
            _publishTimeout,
            cancellationToken);

    /// <summary>
    /// Takes the folder named by <see cref="PublishedAppPathVariable"/>, or publishes both hosts into a new temporary
    /// folder.
    /// </summary>
    /// <returns>A task that completes when the folder is ready.</returns>
    /// <exception cref="InvalidOperationException">The publishing script failed.</exception>
    public async ValueTask InitializeAsync()
    {
        var published = Environment.GetEnvironmentVariable(PublishedAppPathVariable);
        if (!string.IsNullOrEmpty(published))
        {
            FolderPath = Path.GetFullPath(published);
            return;
        }

        _temporaryFolder = TemporaryFolder.Create();
        FolderPath = Path.Combine(_temporaryFolder, "publish");
        var result = await RunScriptAsync(["-OutputPath", FolderPath], CancellationToken.None);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"scripts/publish.ps1 failed.{Environment.NewLine}{result}");
        }
    }

    /// <summary>
    /// Deletes the temporary publish folder, if the fixture created one.
    /// </summary>
    /// <returns>A completed task.</returns>
    public ValueTask DisposeAsync()
    {
        if (_temporaryFolder is not null)
        {
            TemporaryFolder.Delete(_temporaryFolder);
        }

        return ValueTask.CompletedTask;
    }
}
