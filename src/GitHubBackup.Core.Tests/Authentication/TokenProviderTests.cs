using GitHubBackup.Core.Authentication;
using GitHubBackup.Core.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;

namespace GitHubBackup.Core.Tests.Authentication;

/// <summary>
/// Tests <see cref="TokenProvider"/>.
/// </summary>
public sealed class TokenProviderTests
{
    internal const string Secret = "ghp_secret-token-value";

    private const int _tokenFoundEventId = 1;
    private const int _anonymousModeEventId = 2;

    private readonly SecretMasker _masker = new();
    private readonly FakeLogger<TokenProvider> _logger = new();

    [Fact]
    public async Task GetTokenAsync_FirstSourceHasToken_ReturnsItAndSkipsOthers()
    {
        var first = Source("first", Secret);
        var second = Source("second", "other-token");

        var token = await Provider(first, second).GetTokenAsync(TestContext.Current.CancellationToken);

        token.Value.ShouldBe(Secret);
        token.SourceName.ShouldBe("first");
        token.IsAnonymous.ShouldBeFalse();
        await second.DidNotReceive().GetTokenAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0, "configuration")]
    [InlineData(1, "credential manager")]
    [InlineData(2, "gh")]
    [InlineData(-1, "anonymous")]
    public async Task GetTokenAsync_ThreeSources_TakesFirstNonBlankInOrder(int sourceWithToken, string expectedSource)
    {
        string[] names = ["configuration", "credential manager", "gh"];
        var sources = names.Select((name, index) => Source(name, index == sourceWithToken ? Secret : null)).ToArray();

        var token = await Provider(sources).GetTokenAsync(TestContext.Current.CancellationToken);

        token.SourceName.ShouldBe(expectedSource);
        token.Value.ShouldBe(sourceWithToken < 0 ? null : Secret);
    }

    [Fact]
    public async Task GetTokenAsync_EarlierSourceHasToken_LaterSourcesNotQueried()
    {
        var configuration = Source("configuration", null);
        var credentialManager = Source("credential manager", Secret);
        var gh = Source("gh", "gh-token");

        await Provider(configuration, credentialManager, gh).GetTokenAsync(TestContext.Current.CancellationToken);

        await configuration.Received(1).GetTokenAsync(Arg.Any<CancellationToken>());
        await credentialManager.Received(1).GetTokenAsync(Arg.Any<CancellationToken>());
        await gh.DidNotReceive().GetTokenAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task GetTokenAsync_FirstSourceBlank_FallsThroughToNextSource(string? blank)
    {
        var token = await Provider(Source("first", blank), Source("second", Secret))
            .GetTokenAsync(TestContext.Current.CancellationToken);

        token.Value.ShouldBe(Secret);
        token.SourceName.ShouldBe("second");
    }

    [Fact]
    public async Task GetTokenAsync_NoSourceHasToken_ReturnsAnonymous()
    {
        var token = await Provider(Source("first", null), Source("second", " "))
            .GetTokenAsync(TestContext.Current.CancellationToken);

        token.ShouldBeSameAs(GitHubToken.Anonymous);
    }

    [Fact]
    public async Task GetTokenAsync_NoSources_ReturnsAnonymous()
    {
        var token = await Provider().GetTokenAsync(TestContext.Current.CancellationToken);

        token.IsAnonymous.ShouldBeTrue();
    }

    [Fact]
    public async Task GetTokenAsync_NoToken_LogsWarning()
    {
        await Provider().GetTokenAsync(TestContext.Current.CancellationToken);

        var record = _logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        record.Id.Id.ShouldBe(_anonymousModeEventId);
        record.Level.ShouldBe(LogLevel.Warning);
    }

    [Fact]
    public async Task GetTokenAsync_TokenFound_LogsSourceNameWithoutValue()
    {
        await Provider(Source("my source", Secret)).GetTokenAsync(TestContext.Current.CancellationToken);

        var record = _logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        record.Id.Id.ShouldBe(_tokenFoundEventId);
        record.Level.ShouldBe(LogLevel.Information);
        record.GetStructuredStateValue("Source").ShouldBe("my source");
        record.Message.ShouldNotContain(Secret);
        record.StructuredState.ShouldNotBeNull().ShouldAllBe(pair => pair.Value == null || !pair.Value.Contains(Secret));
    }

    [Fact]
    public async Task GetTokenAsync_TokenFound_RegistersTrimmedTokenInMasker()
    {
        var token = await Provider(Source("first", $"  {Secret}\n")).GetTokenAsync(TestContext.Current.CancellationToken);

        token.Value.ShouldBe(Secret);
        _masker.Mask($"value {Secret} end").ShouldBe("value *** end");
    }

    [Fact]
    public async Task GetTokenAsync_CancelledToken_ThrowsOperationCanceled()
    {
        var source = Source("first", Secret);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => Provider(source).GetTokenAsync(cancellation.Token));

        await source.DidNotReceive().GetTokenAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Creates a token source substitute that returns <paramref name="value"/>.
    /// </summary>
    /// <param name="name">The source name.</param>
    /// <param name="value">The value the source returns.</param>
    /// <returns>The substitute.</returns>
    private static ITokenSource Source(string name, string? value)
    {
        var source = Substitute.For<ITokenSource>();
        source.Name.Returns(name);
        source.GetTokenAsync(Arg.Any<CancellationToken>()).Returns(value);
        return source;
    }

    /// <summary>
    /// Creates the provider over <paramref name="sources"/>.
    /// </summary>
    /// <param name="sources">The sources, in priority order.</param>
    /// <returns>The provider.</returns>
    private TokenProvider Provider(params ITokenSource[] sources) => new(sources, _masker, _logger);
}
