using GitHubBackup.Core.Security;
using GitHubBackup.Infrastructure.Logging;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Serilog.Events;
using Serilog.Parsing;

namespace GitHubBackup.Infrastructure.Tests.Logging;

/// <summary>
/// Tests <see cref="LiveLogSink"/>: the rendered and masked entry, the subscriptions and writing from many threads.
/// </summary>
public sealed class LiveLogSinkTests
{
    /// <summary>A registered secret that has no recognisable format.</summary>
    private const string _secret = "s3cret-value-without-format";

    /// <summary>A token in the classic GitHub format that the masker recognises by its prefix.</summary>
    private const string _gitHubToken = "ghp_TestTokenValue0123456789abcdefABCDEF";

    private readonly SecretMasker _masker = new();

    private readonly LiveLogSink _sink;

    /// <summary>
    /// Creates the sink over a masker that knows <see cref="_secret"/>.
    /// </summary>
    public LiveLogSinkTests()
    {
        _masker.Register(_secret);
        _sink = new(_masker);
    }

    [Fact]
    public void Emit_Subscribed_DeliversTimestampLevelAndRenderedMessage()
    {
        var received = new List<LiveLogEntry>();
        using var subscription = _sink.Subscribe(received.Add);

        _sink.Emit(LogEvents.Create("Cloning {Repository} with {Count} refs", null, ("Repository", "octo/tool"), ("Count", 3)));

        received.ShouldBe([new LiveLogEntry(LogEvents.Timestamp, LogLevel.Information, "Cloning octo/tool with 3 refs")]);
    }

    [Fact]
    public void Emit_SecretsInMessagePropertyAndException_AreMasked()
    {
        var received = new List<LiveLogEntry>();
        using var subscription = _sink.Subscribe(received.Add);

        _sink.Emit(LogEvents.Create(
            $"Using {_secret} for {{Header}}",
            new InvalidOperationException($"Failed with {_gitHubToken}"),
            ("Header", $"Authorization: Bearer {_secret}")));

        var message = received.ShouldHaveSingleItem().Message;
        message.ShouldNotContain(_secret);
        message.ShouldNotContain(_gitHubToken);
        message.ShouldBe("Using *** for Authorization: Bearer *** (InvalidOperationException: Failed with ***)");
    }

    [Theory]
    [InlineData(LogEventLevel.Verbose, LogLevel.Trace)]
    [InlineData(LogEventLevel.Debug, LogLevel.Debug)]
    [InlineData(LogEventLevel.Warning, LogLevel.Warning)]
    [InlineData(LogEventLevel.Error, LogLevel.Error)]
    [InlineData(LogEventLevel.Fatal, LogLevel.Critical)]
    public void Emit_Level_DeliversMatchingLogLevel(LogEventLevel level, LogLevel expected)
    {
        var received = new List<LiveLogEntry>();
        using var subscription = _sink.Subscribe(received.Add);

        _sink.Emit(new LogEvent(LogEvents.Timestamp, level, null, new MessageTemplateParser().Parse("Entry"), []));

        received.ShouldHaveSingleItem().Level.ShouldBe(expected);
    }

    [Fact]
    public void Emit_NoSubscribers_DoesNotRenderOrMask()
    {
        var masker = Substitute.For<ISecretMasker>();
        var sink = new LiveLogSink(masker);

        sink.Emit(LogEvents.Create("Entry"));

        masker.DidNotReceiveWithAnyArgs().Mask(default!);
    }

    [Fact]
    public void Emit_AfterUnsubscribe_DoesNotDeliver()
    {
        var received = new List<LiveLogEntry>();
        var subscription = _sink.Subscribe(received.Add);
        _sink.Emit(LogEvents.Create("Before"));

        subscription.Dispose();
        subscription.Dispose();
        _sink.Emit(LogEvents.Create("After"));

        received.Select(entry => entry.Message).ShouldBe(["Before"]);
    }

    [Fact]
    public void Emit_SameHandlerSubscribedTwiceAndOneDisposed_DeliversOnce()
    {
        var received = new List<LiveLogEntry>();
        Action<LiveLogEntry> handler = received.Add;
        using var kept = _sink.Subscribe(handler);
        var disposed = _sink.Subscribe(handler);

        disposed.Dispose();
        _sink.Emit(LogEvents.Create("Entry"));

        received.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Emit_WhileOtherThreadsSubscribeAndUnsubscribe_DeliversEveryEntryToSteadySubscriber()
    {
        const int entries = 2000;
        var received = 0;
        using var steady = _sink.Subscribe(_ => Interlocked.Increment(ref received));
        var cancellationToken = TestContext.Current.CancellationToken;

        var writer = Task.Run(
            () =>
            {
                for (var i = 0; i < entries; i++)
                {
                    _sink.Emit(LogEvents.Create("Entry"));
                }
            },
            cancellationToken);
        var churn = Enumerable.Range(0, 4).Select(_ => Task.Run(
            () =>
            {
                for (var i = 0; i < 500; i++)
                {
                    _sink.Subscribe(_ => { }).Dispose();
                }
            },
            cancellationToken));
        await Task.WhenAll([writer, .. churn]);

        received.ShouldBe(entries);
    }
}
