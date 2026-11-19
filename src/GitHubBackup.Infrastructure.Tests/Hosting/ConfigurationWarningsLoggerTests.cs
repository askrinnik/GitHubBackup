using GitHubBackup.Infrastructure.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace GitHubBackup.Infrastructure.Tests.Hosting;

/// <summary>
/// Tests <see cref="ConfigurationWarningsLogger"/>.
/// </summary>
public sealed class ConfigurationWarningsLoggerTests
{
    private const int _configurationWarningEventId = 1;

    private readonly ConfigurationWarnings _warnings = new();
    private readonly FakeLogger<ConfigurationWarningsLogger> _logger = new();

    [Fact]
    public async Task StartAsync_Warnings_LogsEachAsWarning()
    {
        _warnings.Add("first warning");
        _warnings.Add("second warning");

        await new ConfigurationWarningsLogger(_warnings, _logger).StartAsync(TestContext.Current.CancellationToken);

        var records = _logger.Collector.GetSnapshot().Where(record => record.Id.Id == _configurationWarningEventId).ToList();
        records.Count.ShouldBe(2);
        records.ShouldAllBe(record => record.Level == LogLevel.Warning);
        records.Select(record => record.GetStructuredStateValue("Warning")).ShouldBe(["first warning", "second warning"]);
    }

    [Fact]
    public async Task StartAsync_NoWarnings_LogsNothing()
    {
        await new ConfigurationWarningsLogger(_warnings, _logger).StartAsync(TestContext.Current.CancellationToken);

        _logger.Collector.GetSnapshot().ShouldBeEmpty();
    }
}
