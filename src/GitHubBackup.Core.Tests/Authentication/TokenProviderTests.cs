using GitHubBackup.Core.Authentication;
using GitHubBackup.Core.Security;
using GitHubBackup.Core.Tests.Logging;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GitHubBackup.Core.Tests.Authentication;

/// <summary>
/// Tests <see cref="TokenProvider"/>.
/// </summary>
public sealed class TokenProviderTests
{
    internal const string Secret = "ghp_secret-token-value";

    private readonly SecretMasker _masker = new();
    private readonly RecordingLogger<TokenProvider> _logger = new();

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

        var entry = _logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldContain("anonymous");
        entry.Message.ShouldContain("60 requests per hour");
    }

    [Fact]
    public async Task GetTokenAsync_TokenFound_LogsSourceNameWithoutValue()
    {
        await Provider(Source("my source", Secret)).GetTokenAsync(TestContext.Current.CancellationToken);

        var entry = _logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Message.ShouldContain("my source");
        entry.Message.ShouldNotContain(Secret);
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
