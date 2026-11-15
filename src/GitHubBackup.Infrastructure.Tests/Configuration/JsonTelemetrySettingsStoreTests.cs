using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Core.Telemetry;
using GitHubBackup.Infrastructure.Configuration;
using GitHubBackup.Infrastructure.Files;
using GitHubBackup.Infrastructure.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GitHubBackup.Infrastructure.Tests.Configuration;

/// <summary>
/// Tests <see cref="JsonTelemetrySettingsStore"/>.
/// </summary>
public sealed class JsonTelemetrySettingsStoreTests : IDisposable
{
    private const string _contentRootPath = @"C:\app";
    private const string _filePath = @"C:\app\" + GitHubBackupHostBuilder.UserSettingsFileName;

    private readonly MockFileSystem _fileSystem = new();

    private readonly UserSettingsFile _file;

    /// <summary>
    /// Creates the content root and the user settings file of the store.
    /// </summary>
    public JsonTelemetrySettingsStoreTests()
    {
        _fileSystem.AddDirectory(_contentRootPath);
        var environment = Substitute.For<IHostEnvironment>();
        environment.ContentRootPath.Returns(_contentRootPath);
        _file = new(_fileSystem, environment, new AtomicFileWriter(_fileSystem));
    }

    /// <inheritdoc />
    public void Dispose() => _file.Dispose();

    [Fact]
    public void Current_Options_ReturnsConfiguredSettings()
    {
        var store = CreateStore(new() { Enabled = true, OtlpEndpoint = "http://otel:4317" });

        store.Current.ShouldBe(new TelemetrySettings(true, "http://otel:4317"));
    }

    [Fact]
    public async Task Current_AfterSave_ReturnsSavedSettings()
    {
        var store = CreateStore(new() { Enabled = true, OtlpEndpoint = "http://otel:4317" });

        await store.SaveAsync(new(false, " http://new:4317 "), TestContext.Current.CancellationToken);

        store.Current.ShouldBe(new TelemetrySettings(false, "http://new:4317"));
    }

    [Fact]
    public async Task Current_SaveFails_ReturnsConfiguredSettings()
    {
        var store = CreateStore(new() { Enabled = true, OtlpEndpoint = "http://otel:4317" });

        await Should.ThrowAsync<ArgumentException>(
            () => store.SaveAsync(new(true, "not a uri"), TestContext.Current.CancellationToken));

        store.Current.ShouldBe(new TelemetrySettings(true, "http://otel:4317"));
    }

    [Fact]
    public async Task SaveAsync_EnabledWithEndpoint_WritesOpenTelemetrySection()
    {
        await CreateStore().SaveAsync(new(true, " http://otel:4317 "), TestContext.Current.CancellationToken);

        _fileSystem.File.ReadAllText(_filePath).ShouldBe("""
            {
              "OpenTelemetry": {
                "Enabled": true,
                "OtlpEndpoint": "http://otel:4317"
              }
            }
            """.ReplaceLineEndings("\r\n") + "\r\n");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task SaveAsync_DisabledWithoutEndpoint_WritesNull(string? endpoint)
    {
        await CreateStore().SaveAsync(new(false, endpoint), TestContext.Current.CancellationToken);

        _fileSystem.File.ReadAllText(_filePath).ShouldContain("\"Enabled\": false");
        _fileSystem.File.ReadAllText(_filePath).ShouldContain("\"OtlpEndpoint\": null");
    }

    [Fact]
    public async Task SaveAsync_ToolsAndUiSectionsInFile_KeepsThem()
    {
        _fileSystem.AddFile(_filePath, new MockFileData("""
            {
              "Tools": { "GitPath": "C:\\Git\\git.exe" },
              "Ui": { "Theme": "Dark" },
              "OpenTelemetry": { "Enabled": false, "OtlpEndpoint": "http://old:4317" }
            }
            """));

        await CreateStore().SaveAsync(new(true, "http://new:4317"), TestContext.Current.CancellationToken);

        _fileSystem.File.ReadAllText(_filePath).ShouldBe("""
            {
              "Tools": {
                "GitPath": "C:\\Git\\git.exe"
              },
              "OpenTelemetry": {
                "Enabled": true,
                "OtlpEndpoint": "http://new:4317"
              },
              "Ui": {
                "Theme": "Dark"
              }
            }
            """.ReplaceLineEndings("\r\n") + "\r\n");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/v1/traces")]
    public async Task SaveAsync_EnabledWithoutAbsoluteEndpoint_ThrowsWithoutValueAndWritesNothing(string? endpoint)
    {
        var exception = await Should.ThrowAsync<ArgumentException>(
            () => CreateStore().SaveAsync(new(true, endpoint), TestContext.Current.CancellationToken));

        exception.Message.ShouldStartWith(TelemetrySettings.OtlpEndpointKey);
        _fileSystem.File.Exists(_filePath).ShouldBeFalse();
    }

    [Fact]
    public async Task SaveAsync_DisabledWithRelativeEndpoint_Writes()
    {
        await CreateStore().SaveAsync(new(false, "not a uri"), TestContext.Current.CancellationToken);

        _fileSystem.File.ReadAllText(_filePath).ShouldContain("\"OtlpEndpoint\": \"not a uri\"");
    }

    [Fact]
    public void Validate_EnabledWithRelativeEndpoint_ReturnsErrorWithKeyWithoutValue()
    {
        var errors = CreateStore().Validate(new(true, "secret-host"));

        var error = errors.ShouldHaveSingleItem();
        error.ShouldStartWith(TelemetrySettings.OtlpEndpointKey);
        error.ShouldNotContain("secret-host");
    }

    [Fact]
    public void Validate_DisabledOrAbsoluteEndpoint_ReturnsNoError()
    {
        var store = CreateStore();

        store.Validate(new(false, "anything")).ShouldBeEmpty();
        store.Validate(new(true, "http://localhost:4317")).ShouldBeEmpty();
    }

    /// <summary>
    /// Creates the subject over the mock file system and <paramref name="options"/>.
    /// </summary>
    /// <param name="options">The telemetry options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The subject.</returns>
    private JsonTelemetrySettingsStore CreateStore(OpenTelemetryOptions? options = null) =>
        new(Options.Create(options ?? new()), _file);
}
