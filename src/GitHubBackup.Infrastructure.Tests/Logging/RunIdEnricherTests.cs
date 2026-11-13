using GitHubBackup.Core.Logging;
using GitHubBackup.Core.Runs;
using GitHubBackup.Infrastructure.Logging;
using NSubstitute;
using Serilog.Core;
using Serilog.Events;

namespace GitHubBackup.Infrastructure.Tests.Logging;

/// <summary>
/// Tests <see cref="RunIdEnricher"/>.
/// </summary>
public sealed class RunIdEnricherTests
{
    private static readonly Guid _runId = new("0f8fad5b-d9cb-469f-a165-70867728950e");

    private readonly IRunContext _runContext = Substitute.For<IRunContext>();

    private readonly RunIdEnricher _enricher;

    /// <summary>
    /// Creates the enricher over a run with <see cref="_runId"/>.
    /// </summary>
    public RunIdEnricherTests()
    {
        _runContext.RunId.Returns(_runId);
        _enricher = new(_runContext);
    }

    [Fact]
    public void Enrich_AfterRunIdChanges_AddsCurrentRunId()
    {
        var first = LogEvents.Create("Started");
        _enricher.Enrich(first, Substitute.For<ILogEventPropertyFactory>());
        _runContext.RunId.Returns(new Guid("6f9619ff-8b86-d011-b42d-00c04fc964ff"));
        var second = LogEvents.Create("Started");

        _enricher.Enrich(second, Substitute.For<ILogEventPropertyFactory>());

        first.Properties[LogProperties.RunId].ShouldBe(new ScalarValue("0f8fad5b-d9cb-469f-a165-70867728950e"));
        second.Properties[LogProperties.RunId].ShouldBe(new ScalarValue("6f9619ff-8b86-d011-b42d-00c04fc964ff"));
    }

    [Fact]
    public void Enrich_EntryWithoutRunId_AddsRunIdInDFormat()
    {
        var logEvent = LogEvents.Create("Started");

        _enricher.Enrich(logEvent, Substitute.For<ILogEventPropertyFactory>());

        logEvent.Properties[LogProperties.RunId].ShouldBe(new ScalarValue("0f8fad5b-d9cb-469f-a165-70867728950e"));
    }

    [Fact]
    public void Enrich_EntryWithRunIdFromScope_KeepsScopeValue()
    {
        var logEvent = LogEvents.Create("Started", null, (LogProperties.RunId, "from-scope"));

        _enricher.Enrich(logEvent, Substitute.For<ILogEventPropertyFactory>());

        logEvent.Properties[LogProperties.RunId].ShouldBe(new ScalarValue("from-scope"));
    }
}
