using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Configuration;
using GitHubBackup.Infrastructure.BackupConfiguration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GitHubBackup.Infrastructure.Tests.BackupConfiguration;

/// <summary>
/// Supplies the documented <c>backup-config.json</c> sample and builds stores over a mock file system.
/// </summary>
internal static class BackupConfigSamples
{
    /// <summary>The folder of the executable in every test.</summary>
    public const string ContentRootPath = @"C:\app";

    /// <summary>The path of the configuration file in every test.</summary>
    public const string ConfigPath = @"C:\app\backup-config.json";

    /// <summary>The path of the copy of the previous configuration file.</summary>
    public const string BackupPath = ConfigPath + JsonBackupConfigStore.BackupExtension;

    /// <summary>The sample of the documentation, verbatim.</summary>
    public const string DocumentedSample =
        """
        {
          "schemaVersion": 1,
          "backupRoot": "C:\\Users\\Sanya\\OneDrive\\GitHubBackups",
          "sourcesRoot": null,
          "exclude": [ "someone/*" ],
          "accounts": [
            {
              "url": "https://github.com/askrinnik",
              "path": null,
              "exclude": [ "test-*" ],
              "repositories": [
                { "name": "my-repo", "id": 123456789, "branch": null, "path": null, "status": "Active" },
                { "name": "old-tool", "id": 223456789, "branch": "legacy", "path": "E:\\Archive\\old-tool", "status": "Active" }
              ]
            },
            { "url": "https://github.com/AMTOSS", "path": "E:\\WorkBackups\\AMTOSS", "exclude": [], "repositories": [] },
            { "url": "https://github.com/get-the-manual", "path": null, "exclude": [], "repositories": [] }
          ],
          "repositories": [
            { "url": "https://github.com/someone-else/library", "id": 323456789, "branch": "develop", "path": null, "status": "Active" }
          ]
        }
        """;

    /// <summary>
    /// Creates a store over <paramref name="fileSystem"/> with the real validator.
    /// </summary>
    /// <param name="fileSystem">The mock file system.</param>
    /// <param name="logger">The logger, or <see langword="null"/> for one that only records.</param>
    /// <returns>The store.</returns>
    public static JsonBackupConfigStore CreateStore(MockFileSystem fileSystem, RecordingLogger<JsonBackupConfigStore>? logger = null)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.ContentRootPath.Returns(ContentRootPath);

        return new(
            fileSystem,
            Options.Create(new BackupOptions { ConfigPath = ConfigPath }),
            environment,
            new BackupConfigValidator(),
            logger ?? new RecordingLogger<JsonBackupConfigStore>());
    }

    /// <summary>
    /// Creates a mock file system with the folder of the executable and, when given, the configuration file.
    /// </summary>
    /// <param name="configJson">The text of the configuration file, or <see langword="null"/> for none.</param>
    /// <returns>The file system.</returns>
    public static MockFileSystem CreateFileSystem(string? configJson = null) => Populate(new MockFileSystem(), configJson);

    /// <summary>
    /// Adds the folder of the executable and, when given, the configuration file to <paramref name="fileSystem"/>.
    /// </summary>
    /// <typeparam name="TFileSystem">The type of the file system.</typeparam>
    /// <param name="fileSystem">The file system to populate.</param>
    /// <param name="configJson">The text of the configuration file, or <see langword="null"/> for none.</param>
    /// <returns>The same file system.</returns>
    public static TFileSystem Populate<TFileSystem>(TFileSystem fileSystem, string? configJson)
        where TFileSystem : MockFileSystem
    {
        fileSystem.AddDirectory(ContentRootPath);
        if (configJson is not null)
        {
            fileSystem.AddFile(ConfigPath, new MockFileData(configJson));
        }

        return fileSystem;
    }
}
