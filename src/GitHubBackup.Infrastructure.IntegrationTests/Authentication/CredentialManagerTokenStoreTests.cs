using System.ComponentModel;
using GitHubBackup.Core.Authentication;
using GitHubBackup.Infrastructure.Authentication;
using Meziantou.Framework.Win32;

namespace GitHubBackup.Infrastructure.IntegrationTests.Authentication;

/// <summary>
/// Tests <see cref="CredentialManagerTokenStore"/> against the real Windows Credential Manager, each test with its own
/// credential that is deleted afterwards, so the credential of the application is never touched.
/// </summary>
public sealed class CredentialManagerTokenStoreTests : IDisposable
{
    /// <summary>
    /// The Win32 error a process without a logon session gets from Credential Manager, as on some CI runners.
    /// </summary>
    private const int _noSuchLogonSession = 1312;

    private const string _token = "ghp_CredentialStoreValue0123456789abcdef";

    private readonly string _target = $"GitHubBackup.Tests:{Guid.NewGuid():N}";
    private readonly CredentialManagerTokenStore _store;

    /// <summary>
    /// Creates a store over a unique test credential.
    /// </summary>
    public CredentialManagerTokenStoreTests() => _store = new(_target);

    [Fact]
    public async Task ReadAsync_NothingSaved_ReturnsNull()
    {
        var token = await _store.ReadAsync(TestContext.Current.CancellationToken);

        token.ShouldBeNull();
    }

    [Fact]
    public async Task WriteAsync_ThenRead_ReturnsTokenStoredAsGenericCredential()
    {
        await WriteOrSkipAsync(_token);

        var token = await _store.ReadAsync(TestContext.Current.CancellationToken);

        token.ShouldBe(_token);
        var credential = CredentialManager.ReadCredential(_target, CredentialType.Generic).ShouldNotBeNull();
        credential.UserName.ShouldBe(CredentialManagerTokenStore.UserName);
    }

    [Fact]
    public async Task WriteAsync_TokenSavedBefore_ReplacesIt()
    {
        await WriteOrSkipAsync("ghp_FirstValue");
        await WriteOrSkipAsync(_token);

        var token = await _store.ReadAsync(TestContext.Current.CancellationToken);

        token.ShouldBe(_token);
    }

    [Fact]
    public async Task WriteAsync_TokenTooLong_ThrowsTokenStoreExceptionWithoutToken()
    {
        var token = "ghp_" + new string('x', 2000);

        var exception = await Should.ThrowAsync<TokenStoreException>(
            () => _store.WriteAsync(token, TestContext.Current.CancellationToken));

        SkipWithoutLogonSession(exception);
        exception.Message.ShouldContain(_target);
        exception.Message.ShouldNotContain(token);
        (await _store.ReadAsync(TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public void Location_Always_NamesCredentialManagerAndTarget() =>
        _store.Location.ShouldBe($"Windows Credential Manager ({_target})");

    /// <summary>
    /// Deletes the test credential.
    /// </summary>
    public void Dispose() => CredentialManager.TryDeleteCredential(_target, CredentialType.Generic);

    /// <summary>
    /// Saves <paramref name="token"/>, or skips the test when the process has no logon session to keep credentials
    /// in.
    /// </summary>
    /// <param name="token">The token to save.</param>
    /// <returns>A task that completes when the token is saved.</returns>
    private async Task WriteOrSkipAsync(string token)
    {
        try
        {
            await _store.WriteAsync(token, TestContext.Current.CancellationToken);
        }
        catch (TokenStoreException exception)
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
    private static void SkipWithoutLogonSession(TokenStoreException exception)
    {
        if (exception.InnerException is Win32Exception { NativeErrorCode: _noSuchLogonSession })
        {
            Assert.Skip("Windows Credential Manager is not available without a logon session.");
        }
    }
}
