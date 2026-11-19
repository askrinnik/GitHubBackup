using GitHubBackup.Core.Telemetry;
using GitHubBackup.Infrastructure.Configuration;
using GitHubBackup.Infrastructure.Hosting;

namespace GitHubBackup.Infrastructure.Tests.Configuration;

/// <summary>
/// Tests <see cref="CredentialManagerConfigurationProvider"/> over a stand-in reader of the saved headers.
/// </summary>
public sealed class CredentialManagerConfigurationProviderTests
{
    private const string _headers = "api-key=credential-secret";

    private readonly ConfigurationWarnings _warnings = new();

    [Fact]
    public void Load_HeadersSaved_ProvidesOnlyHeadersKey()
    {
        var provider = CreateProvider(() => _headers);

        provider.Load();

        provider.TryGet(OpenTelemetryOptions.HeadersKey, out var value).ShouldBeTrue();
        value.ShouldBe(_headers);
        provider.GetChildKeys([], null).ShouldBe(["OpenTelemetry"]);
        provider.GetChildKeys([], "OpenTelemetry").ShouldBe(["Headers"]);
        _warnings.Items.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Load_NothingOrBlankSaved_ProvidesNoKey(string? saved)
    {
        var provider = CreateProvider(() => saved);

        provider.Load();

        provider.TryGet(OpenTelemetryOptions.HeadersKey, out _).ShouldBeFalse();
        provider.GetChildKeys([], null).ShouldBeEmpty();
        _warnings.Items.ShouldBeEmpty();
    }

    [Fact]
    public void Load_StoreFails_ProvidesNoKeyAndAddsWarningWithoutValue()
    {
        var provider = CreateProvider(() => throw new OtlpHeadersStoreException(
            "Cannot read the credential 'GitHubBackup:OpenTelemetry:Headers' from Windows Credential Manager: Access is denied."));

        provider.Load();

        provider.TryGet(OpenTelemetryOptions.HeadersKey, out _).ShouldBeFalse();
        var warning = _warnings.Items.ShouldHaveSingleItem();
        warning.ShouldStartWith(OpenTelemetryOptions.HeadersKey);
        warning.ShouldContain("Access is denied.");
        warning.ShouldNotContain("credential-secret");
    }

    /// <summary>
    /// Creates the subject over <paramref name="readHeaders"/> and the warnings of the test.
    /// </summary>
    /// <param name="readHeaders">Returns the saved headers or throws.</param>
    /// <returns>The subject, not yet loaded.</returns>
    private CredentialManagerConfigurationProvider CreateProvider(Func<string?> readHeaders) => new(readHeaders, _warnings);
}
