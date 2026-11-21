using System.Globalization;
using GitHubBackup.Core.Security;
using GitHubBackup.Infrastructure.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Serilog.Events;
using Serilog.Parsing;

namespace GitHubBackup.Infrastructure.Tests.Logging;

/// <summary>
/// Tests <see cref="LiveLogSink"/>: the rendered and masked entry, the subscriptions, the kept entries replayed to a new
/// subscriber and writing from many threads.
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
    /// Creates the sink without kept entries over a masker that knows <see cref="_secret"/>.
    /// </summary>
    public LiveLogSinkTests()
    {
        _masker.Register(_secret);
        _sink = CreateSink(0);
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
    public void Emit_NoSubscribersNoBuffer_DoesNotRenderOrMask()
    {
        var masker = Substitute.For<ISecretMasker>();
        var sink = new LiveLogSink(masker, Options.Create(new LiveLogOptions()));

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

    [Fact]
    public void Subscribe_EntriesBuffered_ReplaysOldestFirstThenDeliversNew()
    {
        var sink = CreateSink(10);
        sink.Emit(LogEvents.Create("First"));
        sink.Emit(LogEvents.Create("Second"));
        var received = new List<LiveLogEntry>();

        using var subscription = sink.Subscribe(received.Add);
        sink.Emit(LogEvents.Create("Third"));

        received.Select(entry => entry.Message).ShouldBe(["First", "Second", "Third"]);
    }

    [Fact]
    public void Subscribe_BufferedEntryWithSecret_ReplaysMasked()
    {
        var sink = CreateSink(10);
        sink.Emit(LogEvents.Create($"Using {_secret} and {_gitHubToken}"));
        var received = new List<LiveLogEntry>();

        using var subscription = sink.Subscribe(received.Add);

        received.ShouldHaveSingleItem().Message.ShouldBe("Using *** and ***");
    }

    [Fact]
    public void Emit_ExactlyCapacity_KeepsAll()
    {
        var sink = CreateSink(3);
        EmitNumbered(sink, 3);
        var received = new List<LiveLogEntry>();

        using var subscription = sink.Subscribe(received.Add);

        received.Select(entry => entry.Message).ShouldBe(["1", "2", "3"]);
    }

    [Fact]
    public void Emit_BeyondCapacity_DropsOldest()
    {
        var sink = CreateSink(3);
        EmitNumbered(sink, 4);
        var received = new List<LiveLogEntry>();

        using var subscription = sink.Subscribe(received.Add);

        received.Select(entry => entry.Message).ShouldBe(["2", "3", "4"]);
    }

    [Fact]
    public void Subscribe_NoBuffer_ReplaysNothing()
    {
        _sink.Emit(LogEvents.Create("Before"));
        var received = new List<LiveLogEntry>();

        using var subscription = _sink.Subscribe(received.Add);

        received.ShouldBeEmpty();
    }

    [Fact]
    public void Subscribe_TwoSubscribers_EachReceivesBuffer()
    {
        var sink = CreateSink(10);
        sink.Emit(LogEvents.Create("Kept"));
        var first = new List<LiveLogEntry>();
        var second = new List<LiveLogEntry>();

        using var firstSubscription = sink.Subscribe(first.Add);
        sink.Emit(LogEvents.Create("New"));
        using var secondSubscription = sink.Subscribe(second.Add);

        first.Select(entry => entry.Message).ShouldBe(["Kept", "New"]);
        second.Select(entry => entry.Message).ShouldBe(["Kept", "New"]);
    }

    [Fact]
    public void Emit_AfterUnsubscribe_DoesNotDeliverButStillBuffers()
    {
        var sink = CreateSink(10);
        var received = new List<LiveLogEntry>();
        var subscription = sink.Subscribe(received.Add);
        subscription.Dispose();

        sink.Emit(LogEvents.Create("After"));
        var later = new List<LiveLogEntry>();
        using var laterSubscription = sink.Subscribe(later.Add);

        received.ShouldBeEmpty();
        later.Select(entry => entry.Message).ShouldBe(["After"]);
    }

    [Fact]
    public async Task Subscribe_WhileOtherThreadEmits_DeliversEveryEntryOnceInOrder()
    {
        const int entries = 3000;
        var sink = CreateSink(entries);
        var cancellationToken = TestContext.Current.CancellationToken;
        using var started = new ManualResetEventSlim();
        var writer = Task.Run(
            () =>
            {
                for (var i = 1; i <= entries; i++)
                {
                    sink.Emit(LogEvents.Create(i.ToString(CultureInfo.InvariantCulture)));
                    if (i == entries / 10)
                    {
                        started.Set();
                    }
                }
            },
            cancellationToken);
        started.Wait(cancellationToken);
        var received = new List<LiveLogEntry>();

        using (sink.Subscribe(received.Add))
        {
            await writer;
        }

        received.Select(entry => entry.Message)
            .ShouldBe(Enumerable.Range(1, entries).Select(i => i.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// Creates a sink over the masker of the test that keeps <paramref name="capacity"/> entries.
    /// </summary>
    /// <param name="capacity">The number of entries kept for later subscribers.</param>
    /// <returns>The sink.</returns>
    private LiveLogSink CreateSink(int capacity) =>
        new(_masker, Options.Create(new LiveLogOptions { BufferCapacity = capacity }));

    /// <summary>
    /// Writes the entries <c>1</c> to <paramref name="count"/> to <paramref name="sink"/>.
    /// </summary>
    /// <param name="sink">The sink.</param>
    /// <param name="count">The number of entries.</param>
    private static void EmitNumbered(LiveLogSink sink, int count)
    {
        for (var i = 1; i <= count; i++)
        {
            sink.Emit(LogEvents.Create(i.ToString(CultureInfo.InvariantCulture)));
        }
    }
}
