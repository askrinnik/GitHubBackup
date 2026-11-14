using GitHubBackup.Core;
using GitHubBackup.Core.Runs;
using GitHubBackup.Infrastructure.Hosting;
using GitHubBackup.TestUtilities;
using GitHubBackup.TestUtilities.Git;

namespace GitHubBackup.Cli.Tests;

/// <summary>
/// Checks the application folder that <c>scripts/publish.ps1</c> produces: its layout, its settings file, and that the
/// published console executable runs a whole backup with the run history and the log next to it.
/// </summary>
[Trait("Category", "EndToEnd")]
[Collection(BackupRunLockTestGroup.Name)]
public sealed class PublishedApplicationTests(PublishedApplication published) : IClassFixture<PublishedApplication>
{
    private const string _owner = "owner";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public void PublishedFolder_TopLevelEntries_AreTwoExecutablesAndAppSettings()
    {
        var entries = Directory.EnumerateFileSystemEntries(published.FolderPath)
            .Select(Path.GetFileName)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        entries.ShouldBe(["appsettings.json", "GitHubBackup.Cli.exe", "GitHubBackup.exe"]);
    }

    [Fact]
    public async Task PublishedFolder_AppSettings_EqualsSourceAppSettings()
    {
        var source = Path.Combine(
            PublishedApplication.RepositoryRoot, "src", "GitHubBackup.Cli", GitHubBackupHostBuilder.AppSettingsFileName);

        var publishedBytes = await File.ReadAllBytesAsync(
            Path.Combine(published.FolderPath, GitHubBackupHostBuilder.AppSettingsFileName), CancellationToken);

        publishedBytes.ShouldBe(await File.ReadAllBytesAsync(source, CancellationToken));
    }

    [Fact]
    public async Task CliExecutable_Help_ExitsSuccessWithUsage()
    {
        using var api = new GitHubApiStub();
        using var installation = new CliInstallation(api.Url, Path.Combine(Path.GetTempPath(), "unused"), published.FolderPath);

        var result = await installation.RunAsync("--help");

        result.ExitCode.ShouldBe((int)ExitCode.Success, result.ToString());
        result.StandardOutput.ShouldContain("Usage:");
    }

    [Fact]
    public async Task CliExecutable_SilentBackupTrackedRepository_ArchivesAndCreatesHistoryAndLogs()
    {
        using var sandbox = new LocalGitSandbox();
        using var api = new GitHubApiStub();
        using var installation = new CliInstallation(
            api.Url, Path.Combine(sandbox.RootPath, "remotes"), published.FolderPath);
        await sandbox.CreateRemoteAsync($"{_owner}/a", CancellationToken);
        api.GivenCurrentUserRepositories(api.GivenRepository(1, _owner, "a"));
        installation.NewBackupConfig().WithRepository(_owner, "a", 1).WriteTo(installation.BackupConfigPath);

        var result = await installation.RunAsync("backup", "--silent");

        result.ExitCode.ShouldBe((int)ExitCode.Success, result.ToString());
        result.StandardOutput.ShouldContain(nameof(BackupItemStatus.Archived));
        installation.FindArchives(_owner, "a").ShouldHaveSingleItem();
        File.Exists(Path.Combine(installation.FolderPath, "data", "history.db")).ShouldBeTrue();
        Directory.EnumerateFiles(installation.LogsPath).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task PublishScript_NonEmptyOutputWithoutClean_FailsAndKeepsFiles()
    {
        var output = TemporaryFolder.Create();
        try
        {
            var kept = Path.Combine(output, "backup-config.json");
            await File.WriteAllTextAsync(kept, "{}", CancellationToken);

            var result = await PublishedApplication.RunScriptAsync(["-OutputPath", output], CancellationToken);

            result.ExitCode.ShouldNotBe(0, result.ToString());
            result.StandardError.ShouldContain("-Clean");
            Directory.EnumerateFileSystemEntries(output).ShouldBe([kept]);
            (await File.ReadAllTextAsync(kept, CancellationToken)).ShouldBe("{}");
        }
        finally
        {
            TemporaryFolder.Delete(output);
        }
    }

    [Fact]
    public async Task PublishScript_CleanOnFolderWithForeignEntries_FailsAndKeepsFiles()
    {
        var output = TemporaryFolder.Create();
        try
        {
            var published = Path.Combine(output, "GitHubBackup.Cli.exe");
            var kept = Path.Combine(output, "backup-config.json");
            var clones = Path.Combine(output, "sources");
            await File.WriteAllTextAsync(published, "old", CancellationToken);
            await File.WriteAllTextAsync(kept, "{}", CancellationToken);
            Directory.CreateDirectory(clones);

            var result = await PublishedApplication.RunScriptAsync(["-OutputPath", output, "-Clean"], CancellationToken);

            result.ExitCode.ShouldNotBe(0, result.ToString());
            result.StandardError.ShouldContain("publish output");
            Directory.EnumerateFileSystemEntries(output).Order().ShouldBe(new[] { published, kept, clones }.Order());
        }
        finally
        {
            TemporaryFolder.Delete(output);
        }
    }

    [Fact]
    public async Task PublishScript_OutputPathIsFile_FailsAndKeepsFile()
    {
        var folder = TemporaryFolder.Create();
        try
        {
            var file = Path.Combine(folder, "not-a-folder.txt");
            await File.WriteAllTextAsync(file, "keep", CancellationToken);

            var result = await PublishedApplication.RunScriptAsync(["-OutputPath", file, "-Clean"], CancellationToken);

            result.ExitCode.ShouldNotBe(0, result.ToString());
            result.StandardError.ShouldContain("is a file");
            (await File.ReadAllTextAsync(file, CancellationToken)).ShouldBe("keep");
        }
        finally
        {
            TemporaryFolder.Delete(folder);
        }
    }
}
