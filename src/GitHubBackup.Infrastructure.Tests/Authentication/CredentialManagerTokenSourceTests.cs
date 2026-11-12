using GitHubBackup.Core.Authentication;
using GitHubBackup.Infrastructure.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GitHubBackup.Infrastructure.Tests.Authentication;

/// <summary>
/// Tests <see cref="CredentialManagerTokenSource"/> over a substitute store.
/// </summary>
public sealed class CredentialManagerTokenSourceTests
{
    private const string _location = "Windows Credential Manager (test-target)";
    private const string _token = "ghp_StoredTokenValue0123456789abcdefAB";
    private const int _storeReadFailedEventId = 1;

    private readonly ITokenStore _store = Substitute.For<ITokenStore>();
    private readonly FakeLogger<CredentialManagerTokenSource> _logger = new();

    /// <summary>
    /// Gives the store substitute its location.
    /// </summary>
    public CredentialManagerTokenSourceTests() => _store.Location.Returns(_location);

    [Fact]
    public void Name_Always_IsStoreLocation() => CreateSource().Name.ShouldBe(_location);

    [Fact]
    public async Task GetTokenAsync_TokenSaved_ReturnsIt()
    {
        _store.ReadAsync(Arg.Any<CancellationToken>()).Returns(_token);

        var token = await CreateSource().GetTokenAsync(TestContext.Current.CancellationToken);

        token.ShouldBe(_token);
        _logger.Collector.GetSnapshot().ShouldBeEmpty();
    }

    [Fact]
    public async Task GetTokenAsync_NothingSaved_ReturnsNull()
    {
        _store.ReadAsync(Arg.Any<CancellationToken>()).Returns((string?)null);

        var token = await CreateSource().GetTokenAsync(TestContext.Current.CancellationToken);

        token.ShouldBeNull();
    }

    [Fact]
    public async Task GetTokenAsync_StoreFails_LogsWarningAndReturnsNull()
    {
        _store.ReadAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new TokenStoreException("Cannot read the credential 'test-target': Access is denied."));

        var token = await CreateSource().GetTokenAsync(TestContext.Current.CancellationToken);

        token.ShouldBeNull();
        var record = _logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        record.Id.Id.ShouldBe(_storeReadFailedEventId);
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Source").ShouldBe(_location);
        record.GetStructuredStateValue("Error").ShouldBe("Cannot read the credential 'test-target': Access is denied.");
    }

    [Fact]
    public async Task GetTokenAsync_Cancelled_Throws()
    {
        _store.ReadAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(
            () => CreateSource().GetTokenAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Creates the source over the store substitute.
    /// </summary>
    /// <returns>The source.</returns>
    private CredentialManagerTokenSource CreateSource() => new(_store, _logger);
}
