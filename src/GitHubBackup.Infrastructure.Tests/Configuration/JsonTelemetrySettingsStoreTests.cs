using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Core.Security;
using GitHubBackup.Core.Telemetry;
using GitHubBackup.Infrastructure.Configuration;
using GitHubBackup.Infrastructure.Files;
using GitHubBackup.Infrastructure.Hosting;
using GitHubBackup.TestUtilities;
using Microsoft.Extensions.Configuration;
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
    private const string _filePath = @"C:\app\" + GitHubBackupHostBuilder.AppSettingsFileName;

    private readonly MockFileSystem _fileSystem = new();

    private readonly SecretMasker _masker = new();

    private readonly AppSettingsFile _file;

    private readonly IOtlpHeadersStore _headersStore = Substitute.For<IOtlpHeadersStore>();

    /// <summary>The headers the configuration reads from Windows Credential Manager, or <see langword="null"/>.</summary>
    private string? _credentialHeaders;

    /// <summary>
    /// Creates the content root and the settings file of the store.
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

        store.Current.ShouldBe(new TelemetrySettings(true, "http://otel:4317", null));
    }

    [Fact]
    public async Task Current_AfterSave_ReturnsSavedSettings()
    {
        var store = CreateStore(new() { Enabled = true, OtlpEndpoint = "http://otel:4317" });

        await store.SaveAsync(new(false, " http://new:4317 ", null), TestContext.Current.CancellationToken);

        store.Current.ShouldBe(new TelemetrySettings(false, "http://new:4317", null));
    }

    [Fact]
    public async Task Current_SaveFails_ReturnsConfiguredSettings()
    {
        var store = CreateStore(new() { Enabled = true, OtlpEndpoint = "http://otel:4317" });

        await Should.ThrowAsync<ArgumentException>(
            () => store.SaveAsync(new(true, "not a uri", null), TestContext.Current.CancellationToken));

        store.Current.ShouldBe(new TelemetrySettings(true, "http://otel:4317", null));
    }

    [Fact]
    public async Task SaveAsync_EnabledWithEndpoint_WritesOpenTelemetrySection()
    {
        await CreateStore().SaveAsync(new(true, " http://otel:4317 ", null), TestContext.Current.CancellationToken);

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
        await CreateStore().SaveAsync(new(false, endpoint, null), TestContext.Current.CancellationToken);

        _fileSystem.File.ReadAllText(_filePath).ShouldContain("\"Enabled\": false");
        _fileSystem.File.ReadAllText(_filePath).ShouldContain("\"OtlpEndpoint\": null");
    }

    [Fact]
    public async Task SaveAsync_OtherSectionsInFile_KeepsThemInPlace()
    {
        _fileSystem.AddFile(_filePath, new MockFileData("""
            {
              "Tools": { "GitPath": "C:\\Git\\git.exe" },
              "OpenTelemetry": { "Enabled": false, "OtlpEndpoint": "http://old:4317", "Headers": "x" },
              "Serilog": { "MinimumLevel": { "Default": "Debug" } }
            }
            """));

        await CreateStore().SaveAsync(new(false, "http://new:4317", "x=y"), TestContext.Current.CancellationToken);

        _fileSystem.File.ReadAllText(_filePath).ShouldBe("""
            {
              "Tools": {
                "GitPath": "C:\\Git\\git.exe"
              },
              "OpenTelemetry": {
                "Enabled": false,
                "OtlpEndpoint": "http://new:4317"
              },
              "Serilog": {
                "MinimumLevel": {
                  "Default": "Debug"
                }
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
            () => CreateStore().SaveAsync(new(true, endpoint, null), TestContext.Current.CancellationToken));

        exception.Message.ShouldStartWith(TelemetrySettings.OtlpEndpointKey);
        _fileSystem.File.Exists(_filePath).ShouldBeFalse();
        _headersStore.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task SaveAsync_DisabledWithRelativeEndpoint_Writes()
    {
        await CreateStore().SaveAsync(new(false, "not a uri", null), TestContext.Current.CancellationToken);

        _fileSystem.File.ReadAllText(_filePath).ShouldContain("\"OtlpEndpoint\": \"not a uri\"");
    }

    [Fact]
    public void Validate_EnabledWithRelativeEndpoint_ReturnsErrorWithKeyWithoutValue()
    {
        var errors = CreateStore().Validate(new(true, "secret-host", null));

        var error = errors.ShouldHaveSingleItem();
        error.ShouldStartWith(TelemetrySettings.OtlpEndpointKey);
        error.ShouldNotContain("secret-host");
    }

    [Fact]
    public void Validate_DisabledOrAbsoluteEndpoint_ReturnsNoError()
    {
        var store = CreateStore();

        store.Validate(new(false, "anything", null)).ShouldBeEmpty();
        store.Validate(new(true, "http://localhost:4317", null)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false, "http://collector.example:4317")]
    [InlineData(true, "https://otlp.nr-data.net:4317")]
    [InlineData(true, "http://localhost:4317")]
    public void Validate_FieldHeadersWithHttpsLoopbackOrDisabled_ReturnsNoError(bool enabled, string endpoint) =>
        CreateStore().Validate(new(enabled, endpoint, "api-key=secret-value")).ShouldBeEmpty();

    [Fact]
    public void Current_FileHeaders_ReturnsFileValue()
    {
        var store = CreateStore(
            new() { Headers = "api-key=file-secret" },
            """{ "OpenTelemetry": { "Headers": "api-key=file-secret" } }""");

        store.Current.Headers.ShouldBe("api-key=file-secret");
        store.HeadersOverridden.ShouldBeFalse();
    }

    [Fact]
    public void Current_HeadersOverridden_ReturnsFileValueNotOverrideValue()
    {
        var store = CreateStore(
            new() { Headers = "api-key=override-secret" },
            """{ "OpenTelemetry": { "Headers": "api-key=file-secret" } }""",
            "--OpenTelemetry:Headers=api-key=override-secret");

        store.Current.Headers.ShouldBe("api-key=file-secret");
    }

    [Theory]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "--OpenTelemetry:Headers=api-key=override-secret" }, true)]
    [InlineData(new[] { "--OpenTelemetry:Headers=" }, true)]
    public void HeadersOverridden_ReflectsConfiguration(string[] arguments, bool expected)
    {
        var store = CreateStore(appSettings: """{ "OpenTelemetry": { "Headers": "api-key=file-secret" } }""", arguments: arguments);

        store.HeadersOverridden.ShouldBe(expected);
    }

    [Fact]
    public async Task Current_AfterSave_ReturnsSavedHeaders()
    {
        var store = CreateStore(appSettings: """{ "OpenTelemetry": { "Headers": "api-key=file-secret" } }""");

        await store.SaveAsync(new(false, null, " api-key=new-secret "), TestContext.Current.CancellationToken);

        store.Current.ShouldBe(new TelemetrySettings(false, null, "api-key=new-secret"));
    }

    [Fact]
    public async Task SaveAsync_Headers_WritesStoreAndRemovesKeyFromFileKeepingBackup()
    {
        _fileSystem.AddFile(_filePath, new MockFileData("""
            {
              "OpenTelemetry": { "Enabled": false, "Headers": "api-key=old-secret" }
            }
            """));

        await CreateStore().SaveAsync(
            new(true, "https://otlp.nr-data.net:4317", "api-key=new-secret"),
            TestContext.Current.CancellationToken);

        await _headersStore.Received(1).WriteAsync("api-key=new-secret", Arg.Any<CancellationToken>());
        _fileSystem.File.ReadAllText(_filePath).ShouldBe("""
            {
              "OpenTelemetry": {
                "Enabled": true,
                "OtlpEndpoint": "https://otlp.nr-data.net:4317"
              }
            }
            """.ReplaceLineEndings("\r\n") + "\r\n");
        _fileSystem.File.ReadAllText(_filePath + ".bak").ShouldContain("old-secret");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SaveAsync_BlankHeaders_DeletesStoreEntryAndRemovesKey(string? headers)
    {
        _fileSystem.AddFile(_filePath, new MockFileData("""{ "OpenTelemetry": { "Headers": "api-key=old-secret" } }"""));

        await CreateStore().SaveAsync(new(false, null, headers), TestContext.Current.CancellationToken);

        await _headersStore.Received(1).DeleteAsync(Arg.Any<CancellationToken>());
        await _headersStore.DidNotReceive().WriteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _fileSystem.File.ReadAllText(_filePath).ShouldNotContain("Headers");
    }

    [Fact]
    public async Task SaveAsync_HeadersWithSurroundingSpaces_WritesTrimmed()
    {
        await CreateStore().SaveAsync(new(false, null, "  api-key=new-secret  "), TestContext.Current.CancellationToken);

        await _headersStore.Received(1).WriteAsync("api-key=new-secret", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveAsync_StoreFails_LeavesFileUnchangedAndKeepsCurrent()
    {
        const string original = """{ "OpenTelemetry": { "Enabled": false, "Headers": "api-key=old-secret" } }""";
        _fileSystem.AddFile(_filePath, new MockFileData(original));
        _headersStore.WriteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new OtlpHeadersStoreException("Cannot save the credential 'x'.")));
        var store = CreateStore(appSettings: original);

        await Should.ThrowAsync<OtlpHeadersStoreException>(() => store.SaveAsync(
            new(true, "https://otlp.nr-data.net:4317", "api-key=new-secret"), TestContext.Current.CancellationToken));

        _fileSystem.File.ReadAllText(_filePath).ShouldBe(original);
        _fileSystem.File.Exists(_filePath + ".bak").ShouldBeFalse();
        store.HeadersInAppSettings.ShouldBeTrue();
        store.Current.Headers.ShouldBe("api-key=old-secret");
    }

    [Fact]
    public async Task SaveAsync_HeadersOverridden_WritesFieldValueAndNeverOverrideValue()
    {
        var store = CreateStore(
            new() { Headers = "api-key=override-secret" },
            "{}",
            "--OpenTelemetry:Headers=api-key=override-secret");

        await store.SaveAsync(
            new(true, "https://otlp.nr-data.net:4317", "api-key=field-secret"), TestContext.Current.CancellationToken);

        await _headersStore.Received(1).WriteAsync("api-key=field-secret", Arg.Any<CancellationToken>());
        _fileSystem.File.ReadAllText(_filePath).ShouldNotContain("override-secret");
    }

    [Fact]
    public void Current_CredentialHeaders_ReturnsCredentialValueOverFileValue()
    {
        _credentialHeaders = "api-key=credential-secret";

        var store = CreateStore(
            new() { Headers = "api-key=credential-secret" },
            """{ "OpenTelemetry": { "Headers": "api-key=file-secret" } }""");

        store.Current.Headers.ShouldBe("api-key=credential-secret");
        store.HeadersOverridden.ShouldBeFalse();
        store.HeadersInAppSettings.ShouldBeTrue();
    }

    [Theory]
    [InlineData("{}", false)]
    [InlineData("""{ "OpenTelemetry": { "Headers": null } }""", false)]
    [InlineData("""{ "OpenTelemetry": { "Headers": "  " } }""", false)]
    [InlineData("""{ "OpenTelemetry": { "Headers": "api-key=file-secret" } }""", true)]
    public void HeadersInAppSettings_ReflectsFile(string appSettings, bool expected) =>
        CreateStore(appSettings: appSettings).HeadersInAppSettings.ShouldBe(expected);

    [Fact]
    public async Task HeadersInAppSettings_AfterSave_IsFalse()
    {
        var store = CreateStore(appSettings: """{ "OpenTelemetry": { "Headers": "api-key=file-secret" } }""");

        await store.SaveAsync(new(false, null, "api-key=file-secret"), TestContext.Current.CancellationToken);

        store.HeadersInAppSettings.ShouldBeFalse();
    }

    [Fact]
    public void Constructor_CredentialHeaders_RegistersThemWithMasker()
    {
        _credentialHeaders = "api-key=credential-secret";

        CreateStore(
            new() { Headers = "api-key=override-secret" },
            "{}",
            "--OpenTelemetry:Headers=api-key=override-secret");

        _masker.Mask("Sent credential-secret.").ShouldBe("Sent ***.");
    }

    [Fact]
    public async Task SaveAsync_Headers_RegistersWholeStringAndEachValueBeforeWriting()
    {
        _fileSystem.AddFile(_filePath, new MockFileData("not json"));
        var store = CreateStore();

        await Should.ThrowAsync<IOException>(() => store.SaveAsync(
            new(false, null, "api-key=first-secret,x-other=second-secret"),
            TestContext.Current.CancellationToken));

        _masker.Mask("api-key=first-secret,x-other=second-secret").ShouldBe("***");
        _masker.Mask("first-secret and second-secret").ShouldBe("*** and ***");
    }

    [Fact]
    public async Task SaveAsync_MalformedHeadersWhileEnabled_RegistersThemAndThrowsWithoutValue()
    {
        var exception = await Should.ThrowAsync<ArgumentException>(() => CreateStore().SaveAsync(
            new(true, "https://otlp.nr-data.net:4317", "api-key first-secret"),
            TestContext.Current.CancellationToken));

        exception.Message.ShouldStartWith(TelemetrySettings.HeadersKey);
        exception.Message.ShouldNotContain("first-secret");
        _masker.Mask("api-key first-secret").ShouldBe("***");
        _fileSystem.File.Exists(_filePath).ShouldBeFalse();
        _headersStore.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public void Validate_EnabledMalformedHeaders_ReturnsHeadersErrorWithoutValue()
    {
        var errors = CreateStore().Validate(new(true, "https://otlp.nr-data.net:4317", "api-key=,secret-value"));

        var error = errors.ShouldHaveSingleItem();
        error.ShouldStartWith(TelemetrySettings.HeadersKey);
        error.ShouldNotContain("secret-value");
    }

    [Fact]
    public void Validate_DisabledMalformedHeaders_ReturnsNoError() =>
        CreateStore().Validate(new(false, "http://collector.example:4317", "malformed secret-value")).ShouldBeEmpty();

    [Fact]
    public void Validate_FieldHeadersWithHttpRemoteEndpoint_ReturnsEndpointErrorWithoutValue()
    {
        var errors = CreateStore().Validate(new(true, "http://collector.example:4317", "api-key=secret-value"));

        var error = errors.ShouldHaveSingleItem();
        error.ShouldStartWith(TelemetrySettings.OtlpEndpointKey);
        error.ShouldContain("https");
        error.ShouldNotContain("secret-value");
    }

    [Fact]
    public void Validate_OverrideHeadersWithHttpRemoteEndpointAndEmptyField_ReturnsEndpointError()
    {
        var store = CreateStore(
            new() { Headers = "api-key=override-secret" },
            "{}",
            "--OpenTelemetry:Headers=api-key=override-secret");

        var errors = store.Validate(new(true, "http://collector.example:4317", null));

        var error = errors.ShouldHaveSingleItem();
        error.ShouldStartWith(TelemetrySettings.OtlpEndpointKey);
        error.ShouldNotContain("override-secret");
    }

    [Fact]
    public void Validate_OverriddenAndMalformedField_ReturnsHeadersError()
    {
        var store = CreateStore(
            new() { Headers = "api-key=override-secret" },
            "{}",
            "--OpenTelemetry:Headers=api-key=override-secret");

        var errors = store.Validate(new(true, "https://otlp.nr-data.net:4317", "malformed secret-value"));

        var error = errors.ShouldHaveSingleItem();
        error.ShouldStartWith(TelemetrySettings.HeadersKey);
        error.ShouldNotContain("secret-value");
    }

    [Fact]
    public void Validate_OverriddenEmptyAndFieldWithHttpRemoteEndpoint_ReturnsEndpointErrorWithoutValue()
    {
        var store = CreateStore(appSettings: "{}", arguments: "--OpenTelemetry:Headers=");

        var errors = store.Validate(new(true, "http://collector.example:4317", "api-key=secret-value"));

        errors.ShouldHaveSingleItem().ShouldStartWith(TelemetrySettings.OtlpEndpointKey);
        errors[0].ShouldNotContain("secret-value");
    }

    [Fact]
    public void Constructor_FileHeaders_RegistersThemWithMasker()
    {
        CreateStore(
            new() { Headers = "api-key=override-secret" },
            """{ "OpenTelemetry": { "Headers": "api-key=file-secret" } }""",
            "--OpenTelemetry:Headers=api-key=override-secret");

        _masker.Mask("Sent file-secret.").ShouldBe("Sent ***.");
    }

    /// <summary>
    /// Creates the subject over the mock file system, <paramref name="options"/> and a configuration with
    /// <paramref name="appSettings"/> as the settings file, <see cref="_credentialHeaders"/> as Windows Credential
    /// Manager and <paramref name="arguments"/> as the command line.
    /// </summary>
    /// <param name="options">The telemetry options, or <see langword="null"/> for the defaults.</param>
    /// <param name="appSettings">The JSON text of the settings file the configuration reads.</param>
    /// <param name="arguments">The command-line arguments, which override the file.</param>
    /// <returns>The subject.</returns>
    private JsonTelemetrySettingsStore CreateStore(
        OpenTelemetryOptions? options = null,
        string appSettings = "{}",
        params string[] arguments)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(
                InMemoryFileProvider.WithAppSettings(appSettings),
                GitHubBackupHostBuilder.AppSettingsFileName,
                optional: true,
                reloadOnChange: false)
            .Add(new CredentialManagerConfigurationSource(() => _credentialHeaders, new ConfigurationWarnings()))
            .AddCommandLine(arguments)
            .Build();
        return new(Options.Create(options ?? new()), configuration, _masker, _file, _headersStore);
    }
}
