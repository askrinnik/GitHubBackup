using GitHubBackup.Core.Logging;
using GitHubBackup.Core.Runs;
using Serilog.Core;
using Serilog.Events;

namespace GitHubBackup.Infrastructure.Logging;

/// <summary>
/// Adds the <see cref="LogProperties.RunId"/> of the host to every log entry that does not carry one from a scope.
/// </summary>
/// <param name="runContext">The run whose identifier is added.</param>
internal sealed class RunIdEnricher(IRunContext runContext) : ILogEventEnricher
{
    /// <summary>The property, created once because the identifier does not change.</summary>
    private readonly LogEventProperty _property = new(LogProperties.RunId, new ScalarValue(runContext.RunId.ToString("D")));

    /// <inheritdoc />
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory) => logEvent.AddPropertyIfAbsent(_property);
}
