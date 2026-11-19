using System.ComponentModel;
using GitHubBackup.Core.Telemetry;
using GitHubBackup.Infrastructure.Configuration;
using GitHubBackup.Infrastructure.Hosting;
using GitHubBackup.TestUtilities;
using Microsoft.Extensions.Configuration;

namespace GitHubBackup.Infrastructure.IntegrationTests.Configuration;

/// <summary>
/// Tests <see cref="CredentialManagerOtlpHeadersStore"/> and <see cref="CredentialManagerConfigurationSource"/>
/// against the real Windows Credential Manager, each test with its own credential that is deleted afterwards, so the
/// credential of the application is never touched.
/// </summary>
public sealed class CredentialManagerOtlpHeadersStoreTests : IDisposable
{
    /// <summary>
    /// The Win32 error a process without a logon session gets from Credential Manager, as on some CI runners.
    /// </summary>
    private const int _noSuchLogonSession = 1312;

    private const string _headers = "api-key=credential-store-secret";

    private readonly OtlpHeadersTestCredential _credential = new();
    private readonly CredentialManagerOtlpHeadersStore _store;

    /// <summary>
    /// Creates a store over the unique test credential.
    /// </summary>
    public CredentialManagerOtlpHeadersStoreTests() => _store = new(_credential.Target);

    /// <inheritdoc />
    public void Dispose() => _credential.Dispose();

    [Fact]
    public async Task ReadAsync_NothingSaved_ReturnsNull() =>
        (await _store.ReadAsync(TestContext.Current.CancellationToken)).ShouldBeNull();

    [Fact]
    public async Task WriteAsync_ThenConfigurationSource_ProvidesHeadersKey()
    {
        await WriteOrSkipAsync(_headers);

        var configuration = new ConfigurationBuilder()
            .Add(new CredentialManagerConfigurationSource(_credential.Target, new ConfigurationWarnings()))
            .Build();

        configuration[OpenTelemetryOptions.HeadersKey].ShouldBe(_headers);
        (await _store.ReadAsync(TestContext.Current.CancellationToken)).ShouldBe(_headers);
    }

    [Fact]
    public async Task DeleteAsync_HeadersSaved_RemovesThem()
    {
        await WriteOrSkipAsync(_headers);

        await _store.DeleteAsync(TestContext.Current.CancellationToken);

        _credential.Read().ShouldBeNull();
    }

    [Fact]
    public async Task DeleteAsync_NothingSaved_Succeeds()
    {
        try
        {
            await _store.DeleteAsync(TestContext.Current.CancellationToken);
        }
        catch (OtlpHeadersStoreException exception)
        {
            SkipWithoutLogonSession(exception);
            throw;
        }

        _credential.Read().ShouldBeNull();
    }

    [Fact]
    public async Task WriteAsync_HeadersTooLong_ThrowsWithoutValue()
    {
        var headers = "api-key=" + new string('x', 3000);

        var exception = await Should.ThrowAsync<OtlpHeadersStoreException>(
            () => _store.WriteAsync(headers, TestContext.Current.CancellationToken));

        SkipWithoutLogonSession(exception);
        exception.Message.ShouldContain(_credential.Target);
        exception.Message.ShouldNotContain("xxxx");
        _credential.Read().ShouldBeNull();
    }

    [Fact]
    public void Location_Always_NamesCredentialManagerAndTarget() =>
        _store.Location.ShouldBe($"Windows Credential Manager ({_credential.Target})");

    /// <summary>
    /// Saves <paramref name="headers"/>, or skips the test when the process has no logon session to keep credentials
    /// in.
    /// </summary>
    /// <param name="headers">The headers to save.</param>
    /// <returns>A task that completes when the headers are saved.</returns>
    private async Task WriteOrSkipAsync(string headers)
    {
        try
        {
            await _store.WriteAsync(headers, TestContext.Current.CancellationToken);
        }
        catch (OtlpHeadersStoreException exception)
        {
            SkipWithoutLogonSession(exception);
            throw;
        }
    }

    /// <summary>
    /// Skips the test when <paramref name="exception"/> reports that the process has no logon session to keep
    /// credentials in.
    /// </summary>
    /// <param name="exception">The failure of the store.</param>
    private static void SkipWithoutLogonSession(OtlpHeadersStoreException exception)
    {
        if (exception.InnerException is Win32Exception { NativeErrorCode: _noSuchLogonSession })
        {
            Assert.Skip("Windows Credential Manager is not available without a logon session.");
        }
    }
}
