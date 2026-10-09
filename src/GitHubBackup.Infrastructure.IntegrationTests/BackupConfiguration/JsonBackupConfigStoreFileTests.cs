using System.IO.Abstractions;
using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Configuration;
using GitHubBackup.Infrastructure.BackupConfiguration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GitHubBackup.Infrastructure.IntegrationTests.BackupConfiguration;

/// <summary>
/// Tests <see cref="JsonBackupConfigStore"/> against the real file system in a temporary folder.
/// </summary>
public sealed class JsonBackupConfigStoreFileTests : IDisposable
{
    private readonly string _folderPath =
        Path.Combine(Path.GetTempPath(), "GitHubBackup.Tests", Guid.NewGuid().ToString("N"));

    private readonly string _configPath;

    private readonly JsonBackupConfigStore _store;

    /// <summary>
    /// Creates the temporary folder and a store whose configuration file lives in it.
    /// </summary>
    public JsonBackupConfigStoreFileTests()
    {
        Directory.CreateDirectory(_folderPath);
        _configPath = Path.Combine(_folderPath, "backup-config.json");

        var environment = Substitute.For<IHostEnvironment>();
        environment.ContentRootPath.Returns(_folderPath);
        _store = new(
            new FileSystem(),
            Options.Create(new BackupOptions { ConfigPath = _configPath }),
            environment,
            new BackupConfigValidator(),
            NullLogger<JsonBackupConfigStore>.Instance);
    }

    [Fact]
    public async Task SaveAsync_NoFile_CreatesFileThatLoadsBack()
    {
        await _store.SaveAsync(CreateConfig("main"), TestContext.Current.CancellationToken);

        var loaded = await _store.LoadAsync(TestContext.Current.CancellationToken);
        loaded.Config.Repositories.ShouldHaveSingleItem().Branch.ShouldBe("main");
        FileNames().ShouldBe(["backup-config.json"]);
    }

    [Fact]
    public async Task SaveAsync_RepeatedWrites_ReplacesFileAndKeepsPreviousVersionAsBackup()
    {
        await _store.SaveAsync(CreateConfig("first"), TestContext.Current.CancellationToken);
        await _store.SaveAsync(CreateConfig("second"), TestContext.Current.CancellationToken);
        var secondContent = await File.ReadAllTextAsync(_configPath, TestContext.Current.CancellationToken);

        await _store.SaveAsync(CreateConfig("third"), TestContext.Current.CancellationToken);

        (await File.ReadAllTextAsync(_configPath + ".bak", TestContext.Current.CancellationToken)).ShouldBe(secondContent);
        (await File.ReadAllTextAsync(_configPath, TestContext.Current.CancellationToken)).ShouldContain("\"branch\": \"third\"");
        FileNames().ShouldBe(["backup-config.json", "backup-config.json.bak"]);
    }

    [Fact]
    public async Task SaveAsync_FileLockedByAnotherProcess_ThrowsAndKeepsOriginalFile()
    {
        await _store.SaveAsync(CreateConfig("first"), TestContext.Current.CancellationToken);
        var original = await File.ReadAllBytesAsync(_configPath, TestContext.Current.CancellationToken);

        using (new FileStream(_configPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await Should.ThrowAsync<IOException>(
                () => _store.SaveAsync(CreateConfig("second"), TestContext.Current.CancellationToken));
        }

        (await File.ReadAllBytesAsync(_configPath, TestContext.Current.CancellationToken)).ShouldBe(original);
        FileNames().ShouldBe(["backup-config.json"]);
    }

    /// <summary>
    /// Deletes the temporary folder and everything in it.
    /// </summary>
    public void Dispose() => Directory.Delete(_folderPath, recursive: true);

    /// <summary>
    /// Creates a valid configuration whose only repository uses <paramref name="branch"/>.
    /// </summary>
    /// <param name="branch">The branch of the repository, used to tell the writes apart.</param>
    /// <returns>The configuration.</returns>
    private BackupConfig CreateConfig(string branch) => new()
    {
        BackupRoot = Path.Combine(_folderPath, "archives"),
        Repositories = [new() { Url = "https://github.com/someone-else/library", Id = 323456789, Branch = branch }],
    };

    /// <summary>
    /// Returns the names of the files in the temporary folder, in ordinal order.
    /// </summary>
    /// <returns>The file names.</returns>
    private string[] FileNames() =>
        [.. Directory.GetFiles(_folderPath).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];
}
