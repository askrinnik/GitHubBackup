using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using GitHubBackup.Core.Processes;
using GitHubBackup.Core.Tools;
using GitHubBackup.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GitHubBackup.Infrastructure.Tools;

/// <summary>
/// Locates git, git-lfs and 7-Zip from <see cref="ToolsOptions"/> and the <c>PATH</c> variable, and verifies each by
/// running it through <see cref="IProcessRunner"/>.
/// </summary>
/// <remarks>
/// git is searched for in <c>PATH</c> with <see cref="ExecutableSearchPath"/>.
/// </remarks>
/// <param name="options">The configured tool paths.</param>
/// <param name="processRunner">Runs the tools to read their versions.</param>
/// <param name="fileSystem">Checks that the executables exist.</param>
/// <param name="logger">Logs the versions found.</param>
internal sealed class ToolLocator(
    IOptions<ToolsOptions> options, IProcessRunner processRunner, IFileSystem fileSystem, ILogger<ToolLocator> logger)
    : IToolLocator
{
    /// <summary>The name of the git executable searched for in <c>PATH</c>.</summary>
    private const string _gitFileName = "git.exe";

    /// <summary>Gets the function that reads an environment variable; tests replace it to supply <c>PATH</c>.</summary>
    internal Func<string, string?> GetEnvironmentVariable { get; init; } = Environment.GetEnvironmentVariable;

    /// <inheritdoc />
    public async Task<ToolSet> LocateAsync(CancellationToken cancellationToken)
    {
        var gitPath = FindGit();
        var gitVersion = await RunVersionAsync(
            "git", gitPath, ["version"], ToolVersionParser.TryParseGit, cancellationToken);

        var lfsVersion = await RunVersionAsync(
            "Git LFS", gitPath, ["lfs", "version"], ToolVersionParser.TryParseGitLfs, cancellationToken);

        var sevenZipPath = FindSevenZip();
        var sevenZipVersion = await RunVersionAsync(
            "7-Zip", sevenZipPath, [], ToolVersionParser.TryParseSevenZip, cancellationToken);

        return new ToolSet(new LocatedTool(gitPath, gitVersion), lfsVersion, new LocatedTool(sevenZipPath, sevenZipVersion));
    }

    /// <summary>
    /// Returns the configured git path, or the first <c>git.exe</c> found in <c>PATH</c>.
    /// </summary>
    /// <returns>The path of an existing <c>git.exe</c>.</returns>
    /// <exception cref="ToolUnavailableException">The file is not there.</exception>
    private string FindGit()
    {
        var configured = options.Value.GitPath;
        if (!string.IsNullOrEmpty(configured))
        {
            return fileSystem.File.Exists(configured)
                ? configured
                : throw new ToolUnavailableException(
                    $"git was not found at '{configured}'. Fix {ToolsOptions.SectionName}:{nameof(ToolsOptions.GitPath)}.");
        }

        return ExecutableSearchPath.Find(fileSystem, GetEnvironmentVariable("PATH"), _gitFileName)
            ?? throw new ToolUnavailableException(
                $"{_gitFileName} was not found in PATH. Install Git for Windows or set " +
                $"{ToolsOptions.SectionName}:{nameof(ToolsOptions.GitPath)}.");
    }

    /// <summary>
    /// Returns the configured 7-Zip path, or <see cref="ToolsOptions.DefaultSevenZipPath"/> when none is set.
    /// </summary>
    /// <returns>The path of an existing <c>7z.exe</c>.</returns>
    /// <exception cref="ToolUnavailableException">The file is not there.</exception>
    private string FindSevenZip()
    {
        var configured = options.Value.SevenZipPath;
        var path = string.IsNullOrEmpty(configured) ? ToolsOptions.DefaultSevenZipPath : configured;

        return fileSystem.File.Exists(path)
            ? path
            : throw new ToolUnavailableException(
                $"7-Zip was not found at '{path}'. Install 7-Zip or fix " +
                $"{ToolsOptions.SectionName}:{nameof(ToolsOptions.SevenZipPath)}.");
    }

    /// <summary>
    /// Runs <paramref name="path"/> with <paramref name="arguments"/> in the folder of the executable, parses the
    /// version from its output and logs it.
    /// </summary>
    /// <param name="tool">The name of the tool in messages and the log.</param>
    /// <param name="path">The path of the executable to run.</param>
    /// <param name="arguments">The arguments that make the tool print its version.</param>
    /// <param name="tryParse">Reads the version from the output lines.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>The version the tool reported.</returns>
    /// <exception cref="ToolUnavailableException">
    /// The tool cannot start, exits with a non-zero code or prints no recognizable version.
    /// </exception>
    private async Task<string> RunVersionAsync(
        string tool,
        string path,
        IReadOnlyList<string> arguments,
        TryParseVersion tryParse,
        CancellationToken cancellationToken)
    {
        var request = new ProcessRequest
        {
            FileName = path,
            Arguments = arguments,
            WorkingDirectory = fileSystem.Path.GetDirectoryName(path)!,
        };

        ProcessResult result;
        try
        {
            result = await processRunner.RunAsync(request, cancellationToken);
        }
        catch (ProcessStartException exception)
        {
            throw new ToolUnavailableException($"{tool} cannot be started from '{path}': {exception.Message}", exception);
        }

        if (!result.Succeeded)
        {
            throw new ToolUnavailableException(
                $"{tool} is not available: '{path}' exited with code {result.ExitCode} when asked for its version.");
        }

        if (!tryParse(result.StandardOutput, out var version))
        {
            throw new ToolUnavailableException($"{tool} did not report a recognizable version when '{path}' was run.");
        }

        ToolLocatorLog.ToolFound(logger, tool, version, path);
        return version;
    }

    /// <summary>
    /// Reads a version from the output lines of a tool.
    /// </summary>
    /// <param name="lines">The standard output lines.</param>
    /// <param name="version">The version, or <see langword="null"/> when none was found.</param>
    /// <returns><see langword="true"/> when a version was found.</returns>
    private delegate bool TryParseVersion(IReadOnlyList<string> lines, [NotNullWhen(true)] out string? version);
}
