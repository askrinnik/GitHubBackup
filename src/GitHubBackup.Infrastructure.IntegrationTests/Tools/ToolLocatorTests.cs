using System.IO.Abstractions;
using GitHubBackup.Core.Security;
using GitHubBackup.Core.Tools;
using GitHubBackup.Infrastructure.Configuration;
using GitHubBackup.Infrastructure.IntegrationTests.Processes;
using GitHubBackup.Infrastructure.Processes;
using GitHubBackup.Infrastructure.Tools;
using GitHubBackup.TestUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GitHubBackup.Infrastructure.IntegrationTests.Tools;

/// <summary>
/// Tests <see cref="ToolLocator"/> against the git, git-lfs and 7-Zip installed on the machine.
/// </summary>
public sealed class ToolLocatorTests : IDisposable
{
    private readonly TemporaryContentRoot _folder = new();
    private readonly CapturingLogger<ToolLocator> _logger = new();

    [Fact]
    public async Task LocateAsync_InstalledTools_ReturnsPathsAndVersions()
    {
        var tools = await CreateLocator(new()).LocateAsync(TestContext.Current.CancellationToken);

        tools.Git.Path.ShouldEndWith("git.exe");
        tools.Git.Version.ShouldNotBeNullOrWhiteSpace();
        tools.GitLfsVersion.ShouldNotBeNullOrWhiteSpace();
        tools.SevenZip.Path.ShouldEndWith("7z.exe");
        tools.SevenZip.Version.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task LocateAsync_InstalledTools_LogsAVersionForEachTool()
    {
        var tools = await CreateLocator(new()).LocateAsync(TestContext.Current.CancellationToken);

        var found = _logger.Entries.Where(entry => entry.Level == LogLevel.Information && entry.Message.Contains(" found at ")).ToList();
        found.Select(entry => entry.Properties["Tool"]).ShouldBe(["git", "Git LFS", "7-Zip"]);
        found.Select(entry => entry.Properties["Version"]).ShouldBe([tools.Git.Version, tools.GitLfsVersion, tools.SevenZip.Version]);
    }

    [Fact]
    public async Task LocateAsync_GitPathMissing_ThrowsNamingKeyAndPath()
    {
        var missing = Path.Combine(_folder.FolderPath, "git.exe");

        var exception = await Should.ThrowAsync<ToolUnavailableException>(
            () => CreateLocator(new() { GitPath = missing }).LocateAsync(TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("Tools:GitPath");
        exception.Message.ShouldContain(missing);
    }

    /// <inheritdoc />
    public void Dispose() => _folder.Dispose();

    /// <summary>
    /// Creates a locator over the real file system and the real process runner.
    /// </summary>
    /// <param name="options">The tool settings.</param>
    /// <returns>The locator under test.</returns>
    private ToolLocator CreateLocator(ToolsOptions options) => new(
        Options.Create(options),
        new ProcessRunner(new SecretMasker(), new FileSystem(), TimeProvider.System, NullLogger<ProcessRunner>.Instance),
        new FileSystem(),
        _logger);
}
