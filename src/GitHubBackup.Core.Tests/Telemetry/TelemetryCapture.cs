using System.Diagnostics;
using System.Diagnostics.Metrics;
using GitHubBackup.Core.Telemetry;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

namespace GitHubBackup.Core.Tests.Telemetry;

/// <summary>
/// Supplies a <see cref="BackupTelemetry"/> with its own meters and collects the spans it creates under the span a test
/// starts with <see cref="StartTest"/>.
/// </summary>
/// <remarks>
/// The activity listener is process-wide and tests run in parallel, so only the spans of the trace of the test's own
/// span are kept; the meters belong to this instance alone, so a <see cref="MetricCollector{T}"/> sees only this test.
/// </remarks>
internal sealed class TelemetryCapture : IDisposable
{
    /// <summary>Creates the meters of <see cref="Telemetry"/> with this instance as their scope.</summary>
    private readonly TestMeterFactory _meterFactory = new();

    /// <summary>Receives every stopped span of <see cref="BackupTelemetry.SourceName"/>.</summary>
    private readonly ActivityListener _listener;

    /// <summary>The spans of the test's trace, in the order they stopped.</summary>
    private readonly List<Activity> _stopped = [];

    /// <summary>The trace of the test's span, or <see langword="default"/> before <see cref="StartTest"/>.</summary>
    private ActivityTraceId _traceId;

    /// <summary>
    /// Initializes a new instance of the <see cref="TelemetryCapture"/> class and starts listening.
    /// </summary>
    public TelemetryCapture()
    {
        Telemetry = new(_meterFactory);
        _listener = new()
        {
            ShouldListenTo = source => source.Name == BackupTelemetry.SourceName,
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.TraceId == _traceId)
                {
                    lock (_stopped)
                    {
                        _stopped.Add(activity);
                    }
                }
            },
        };
        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>Gets the telemetry to pass to the subject.</summary>
    public BackupTelemetry Telemetry { get; }

    /// <summary>Gets the spans of the test's trace that have stopped, in the order they stopped.</summary>
    public IReadOnlyList<Activity> Spans
    {
        get
        {
            lock (_stopped)
            {
                return [.. _stopped];
            }
        }
    }

    /// <summary>
    /// Starts the span of the test, which becomes the current span and the parent of the spans the subject creates.
    /// </summary>
    /// <returns>The span of the test; dispose it at the end of the test.</returns>
    public Activity StartTest()
    {
        var activity = new Activity("test").SetIdFormat(ActivityIdFormat.W3C).Start();
        _traceId = activity.TraceId;
        return activity;
    }

    /// <summary>
    /// Returns the stopped spans named <paramref name="name"/>.
    /// </summary>
    /// <param name="name">The span name.</param>
    /// <returns>The spans, in the order they stopped.</returns>
    public List<Activity> Named(string name) => [.. Spans.Where(activity => activity.OperationName == name)];

    /// <summary>
    /// Creates a collector of the instrument <paramref name="instrumentName"/> of <see cref="Telemetry"/>.
    /// </summary>
    /// <typeparam name="T">The type of the measurements.</typeparam>
    /// <param name="instrumentName">The name of the instrument.</param>
    /// <returns>The collector; dispose it at the end of the test.</returns>
    public MetricCollector<T> Collect<T>(string instrumentName)
        where T : struct =>
        new(_meterFactory, BackupTelemetry.SourceName, instrumentName);

    /// <inheritdoc />
    public void Dispose()
    {
        _listener.Dispose();
        Telemetry.Dispose();
        _meterFactory.Dispose();
    }

    /// <summary>
    /// Creates meters scoped to itself, so collectors can tell them from the meters of other tests.
    /// </summary>
    private sealed class TestMeterFactory : IMeterFactory
    {
        /// <summary>The meters created, disposed with the factory.</summary>
        private readonly List<Meter> _meters = [];

        /// <inheritdoc />
        public Meter Create(MeterOptions options)
        {
            var meter = new Meter(options.Name, options.Version, options.Tags, this);
            _meters.Add(meter);
            return meter;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            foreach (var meter in _meters)
            {
                meter.Dispose();
            }
        }
    }
}
