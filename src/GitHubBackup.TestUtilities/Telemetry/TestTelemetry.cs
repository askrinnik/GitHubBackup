using System.Diagnostics.Metrics;
using GitHubBackup.Core.Telemetry;

namespace GitHubBackup.TestUtilities.Telemetry;

/// <summary>
/// Supplies a <see cref="BackupTelemetry"/> whose meters belong to this instance alone and are disposed with it.
/// </summary>
public sealed class TestTelemetry : IDisposable
{
    /// <summary>Creates and owns the meters of <see cref="Telemetry"/>.</summary>
    private readonly TestMeterFactory _meterFactory = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="TestTelemetry"/> class.
    /// </summary>
    public TestTelemetry() => Telemetry = new(_meterFactory);

    /// <summary>Gets the telemetry to pass to the subject.</summary>
    public BackupTelemetry Telemetry { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        Telemetry.Dispose();
        _meterFactory.Dispose();
    }

    /// <summary>
    /// Creates meters scoped to itself and disposes them with itself.
    /// </summary>
    private sealed class TestMeterFactory : IMeterFactory
    {
        /// <summary>The meters created.</summary>
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
