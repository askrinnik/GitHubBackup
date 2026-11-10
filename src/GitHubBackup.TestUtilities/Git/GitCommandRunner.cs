using System.Diagnostics;

namespace GitHubBackup.TestUtilities.Git;

/// <summary>
/// Runs <c>git</c> for test scenarios in an environment that ignores the machine's git configuration, never prompts
/// and carries a fixed identity.
/// </summary>
/// <remarks>
/// The runner is independent of the production process runner so that building a scenario does not depend on the
/// code under test.
/// </remarks>
/// <param name="gitPath">The absolute path of <c>git.exe</c>.</param>
/// <param name="globalConfigPath">The path of an empty file that replaces the user's global git configuration.</param>
internal sealed class GitCommandRunner(string gitPath, string globalConfigPath)
{
    private static readonly (string Key, string Value)[] _fixedConfiguration =
    [
        ("user.name", "Test User"),
        ("user.email", "test@example.invalid"),
        ("init.defaultBranch", "main"),
        ("core.autocrlf", "false"),
        ("commit.gpgsign", "false"),
        ("tag.gpgsign", "false"),
        ("protocol.file.allow", "always"),
        ("core.longpaths", "true"),
        ("credential.helper", string.Empty),
    ];

    private static readonly string[] _inheritedVariablePrefixes = ["GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_CONFIG"];

    /// <summary>
    /// Finds <c>git.exe</c> on <c>PATH</c>.
    /// </summary>
    /// <returns>The absolute path of the first match.</returns>
    /// <exception cref="FileNotFoundException">No <c>git.exe</c> is on <c>PATH</c>.</exception>
    public static string LocateGit()
    {
        var directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var directory in directories)
        {
            var candidate = Path.Combine(directory, "git.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("git.exe was not found on PATH.");
    }

    /// <summary>
    /// Runs one git command and waits for it to finish.
    /// </summary>
    /// <param name="workingDirectory">The folder the command runs in.</param>
    /// <param name="cancellationToken">Cancels the command and kills the process tree.</param>
    /// <param name="arguments">The arguments, passed one by one without shell parsing.</param>
    /// <returns>The non-empty lines of standard output.</returns>
    /// <exception cref="InvalidOperationException">The command exits with a non-zero code; the message carries the
    /// command, the exit code and the standard error.</exception>
    public async Task<IReadOnlyList<string>> RunAsync(
        string workingDirectory,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(gitPath)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        ConfigureEnvironment(startInfo);

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        process.StandardInput.Close();

        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        var output = await standardOutput;
        var error = await standardError;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} failed in '{workingDirectory}' with exit code {process.ExitCode}: {error.Trim()}");
        }

        return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Replaces the inherited git variables with the isolated configuration.
    /// </summary>
    /// <param name="startInfo">The start info to configure.</param>
    private void ConfigureEnvironment(ProcessStartInfo startInfo)
    {
        var environment = startInfo.Environment;
        foreach (var key in environment.Keys.ToArray())
        {
            if (_inheritedVariablePrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                environment.Remove(key);
            }
        }

        environment["GIT_CONFIG_NOSYSTEM"] = "1";
        environment["GIT_CONFIG_GLOBAL"] = globalConfigPath;
        environment["GIT_TERMINAL_PROMPT"] = "0";
        environment["GCM_INTERACTIVE"] = "never";
        environment["GIT_CONFIG_COUNT"] = _fixedConfiguration.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
        for (var index = 0; index < _fixedConfiguration.Length; index++)
        {
            environment[$"GIT_CONFIG_KEY_{index}"] = _fixedConfiguration[index].Key;
            environment[$"GIT_CONFIG_VALUE_{index}"] = _fixedConfiguration[index].Value;
        }
    }
}
