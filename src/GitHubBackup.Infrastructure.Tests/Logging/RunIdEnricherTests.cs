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

    private readonly RunIdEnricher _enricher;

    /// <summary>
    /// Creates the enricher over a run with <see cref="_runId"/>.
    /// </summary>
    public RunIdEnricherTests()
    {
        var runContext = Substitute.For<IRunContext>();
        runContext.RunId.Returns(_runId);
        _enricher = new(runContext);
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
