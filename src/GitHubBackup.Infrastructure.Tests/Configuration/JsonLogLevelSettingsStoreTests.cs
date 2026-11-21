using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Core.Logging;
using GitHubBackup.Infrastructure.Configuration;
using GitHubBackup.Infrastructure.Files;
using GitHubBackup.Infrastructure.Hosting;
using GitHubBackup.Infrastructure.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GitHubBackup.Infrastructure.Tests.Configuration;

/// <summary>
/// Tests <see cref="JsonLogLevelSettingsStore"/>.
/// </summary>
public sealed class JsonLogLevelSettingsStoreTests : IDisposable
{
    private const string _contentRootPath = @"C:\app";
    private const string _filePath = @"C:\app\" + GitHubBackupHostBuilder.AppSettingsFileName;

    private readonly MockFileSystem _fileSystem = new();

    private readonly AppSettingsFile _file;

    /// <summary>
    /// Creates the content root and the settings file of the store.
    /// </summary>
    public JsonLogLevelSettingsStoreTests()
    {
        _fileSystem.AddDirectory(_contentRootPath);
        var environment = Substitute.For<IHostEnvironment>();
        environment.ContentRootPath.Returns(_contentRootPath);
        _file = new(_fileSystem, environment, new AtomicFileWriter(_fileSystem));
    }

    /// <inheritdoc />
    public void Dispose() => _file.Dispose();

    [Fact]
    public void Current_Options_ReturnsConfiguredLevelsIgnoringCaseAndBlankAsNotSet()
    {
        var endpoints = new SerilogEndpointsOptions();
        endpoints.File.Level = "debug";
        endpoints.Console.Level = "";
        endpoints.OpenTelemetry.Level = "OFF";

        var store = CreateStore(endpoints: endpoints);

        store.Current.ShouldBe(new LogLevelSettings(EndpointLogLevel.Debug, null, null, EndpointLogLevel.Off));
    }

    [Fact]
    public void Current_NoEndpointsSection_ReturnsDebugForFileOnly()
    {
        var configuration = new ConfigurationBuilder().Build();
        using var provider = new ServiceCollection().AddGitHubBackupLogging(configuration).BuildServiceProvider();

        var store = CreateStore(
            provider.GetRequiredService<IOptions<SerilogOptions>>().Value,
            provider.GetRequiredService<IOptions<SerilogEndpointsOptions>>().Value);

        store.Current.ShouldBe(new LogLevelSettings(EndpointLogLevel.Debug, null, null, null));
    }

    [Theory]
    [InlineData("Warning", EndpointLogLevel.Warning)]
    [InlineData("verbose", EndpointLogLevel.Verbose)]
    [InlineData(null, EndpointLogLevel.Information)]
    public void DefaultLevel_Options_ReturnsMinimumLevelDefault(string? configured, EndpointLogLevel expected)
    {
        var store = CreateStore(new SerilogOptions { Default = configured });

        store.DefaultLevel.ShouldBe(expected);
    }

    [Fact]
    public async Task Current_AfterSave_ReturnsSavedLevels()
    {
        var store = CreateStore();
        var saved = new LogLevelSettings(EndpointLogLevel.Verbose, EndpointLogLevel.Off, null, EndpointLogLevel.Error);

        await store.SaveAsync(saved, TestContext.Current.CancellationToken);

        store.Current.ShouldBe(saved);
    }

    [Fact]
    public async Task Current_SaveFails_ReturnsConfiguredLevels()
    {
        _fileSystem.AddFile(_filePath, new MockFileData("not json"));
        var store = CreateStore();

        await Should.ThrowAsync<IOException>(() => store.SaveAsync(
            new(EndpointLogLevel.Error, null, null, null), TestContext.Current.CancellationToken));

        store.Current.ShouldBe(new LogLevelSettings(null, null, null, null));
    }

    [Fact]
    public async Task SaveAsync_Levels_WritesFourLevelKeysDefaultAsNullAndKeepsOverrides()
    {
        _fileSystem.AddFile(_filePath, new MockFileData("""
            {
              "Serilog": {
                "MinimumLevel": { "Default": "Warning" },
                "Endpoints": { "LiveLog": { "Level": "Error", "Override": { "GitHubBackup": "Debug" } } }
              }
            }
            """));

        await CreateStore().SaveAsync(
            new(EndpointLogLevel.Debug, EndpointLogLevel.Off, null, EndpointLogLevel.Fatal),
            TestContext.Current.CancellationToken);

        _fileSystem.File.ReadAllText(_filePath).ShouldBe("""
            {
              "Serilog": {
                "MinimumLevel": {
                  "Default": "Warning"
                },
                "Endpoints": {
                  "LiveLog": {
                    "Level": null,
                    "Override": {
                      "GitHubBackup": "Debug"
                    }
                  },
                  "File": {
                    "Level": "Debug"
                  },
                  "Console": {
                    "Level": "Off"
                  },
                  "OpenTelemetry": {
                    "Level": "Fatal"
                  }
                }
              }
            }
            """.ReplaceLineEndings("\r\n") + "\r\n");
        _fileSystem.File.Exists(_filePath + AtomicFileWriter.BackupExtension).ShouldBeTrue();
    }

    /// <summary>
    /// Creates the store over the given options and the settings file of the test.
    /// </summary>
    /// <param name="options">The minimum levels; the defaults when <see langword="null"/>.</param>
    /// <param name="endpoints">The endpoint levels; none set when <see langword="null"/>.</param>
    /// <returns>The store.</returns>
    private JsonLogLevelSettingsStore CreateStore(SerilogOptions? options = null, SerilogEndpointsOptions? endpoints = null) =>
        new(Options.Create(options ?? new SerilogOptions()), Options.Create(endpoints ?? new SerilogEndpointsOptions()), _file);
}
