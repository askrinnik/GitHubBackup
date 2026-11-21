using GitHubBackup.Infrastructure.Logging;
using Serilog.Events;
using Serilog.Parsing;

namespace GitHubBackup.Infrastructure.Tests.Logging;

/// <summary>
/// Tests <see cref="EndpointLevelResolver"/>.
/// </summary>
public sealed class EndpointLevelResolverTests
{
    /// <summary>A source context below the namespace of the application.</summary>
    private const string _source = "GitHubBackup.Infrastructure.Git.GitClient";

    [Fact]
    public void Resolve_EndpointAndGlobalOverrideMatch_EndpointOverrideWins()
    {
        var resolver = Create(
            endpointOverrides: new() { ["GitHubBackup"] = "Verbose" },
            globalOverrides: new() { ["GitHubBackup.Infrastructure"] = "Error" },
            level: "Warning");

        resolver.Resolve(_source).ShouldBe(LogEventLevel.Verbose);
    }

    [Fact]
    public void Resolve_OnlyGlobalOverrideMatches_GlobalOverrideBeatsEndpointLevel()
    {
        var resolver = Create(globalOverrides: new() { ["GitHubBackup"] = "Error" }, level: "Debug");

        resolver.Resolve(_source).ShouldBe(LogEventLevel.Error);
    }

    [Fact]
    public void Resolve_NoOverrideMatches_ReturnsEndpointLevel()
    {
        var resolver = Create(
            endpointOverrides: new() { ["Microsoft"] = "Error" },
            globalOverrides: new() { ["System"] = "Error" },
            level: "Debug");

        resolver.Resolve(_source).ShouldBe(LogEventLevel.Debug);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Resolve_EndpointLevelNotSet_ReturnsGlobalDefault(string? level)
    {
        var resolver = Create(level: level, defaultLevel: "Warning");

        resolver.Resolve(_source).ShouldBe(LogEventLevel.Warning);
    }

    [Fact]
    public void Resolve_SeveralEndpointOverridesMatch_LongestKeyWins()
    {
        var resolver = Create(endpointOverrides: new()
        {
            ["GitHubBackup"] = "Error",
            ["GitHubBackup.Infrastructure.Git"] = "Debug",
            ["GitHubBackup.Infrastructure"] = "Warning",
        });

        resolver.Resolve(_source).ShouldBe(LogEventLevel.Debug);
    }

    [Fact]
    public void Resolve_SeveralGlobalOverridesMatch_LongestKeyWins()
    {
        var resolver = Create(globalOverrides: new()
        {
            ["GitHubBackup.Infrastructure"] = "Debug",
            ["GitHubBackup"] = "Error",
        });

        resolver.Resolve(_source).ShouldBe(LogEventLevel.Debug);
    }

    [Theory]
    [InlineData("GitHubBackupX.Service")]
    [InlineData("GitHubBackupX")]
    public void Resolve_KeyIsPrefixWithoutDot_DoesNotMatch(string sourceContext)
    {
        var resolver = Create(endpointOverrides: new() { ["GitHubBackup"] = "Error" }, level: "Debug");

        resolver.Resolve(sourceContext).ShouldBe(LogEventLevel.Debug);
    }

    [Fact]
    public void Resolve_KeyEqualsSourceContext_Matches()
    {
        var resolver = Create(endpointOverrides: new() { [_source] = "Error" }, level: "Debug");

        resolver.Resolve(_source).ShouldBe(LogEventLevel.Error);
    }

    [Fact]
    public void Resolve_KeyInOtherCase_Matches()
    {
        var resolver = Create(endpointOverrides: new() { ["githubbackup.INFRASTRUCTURE"] = "Error" }, level: "Debug");

        resolver.Resolve(_source).ShouldBe(LogEventLevel.Error);
    }

    [Fact]
    public void Resolve_NoSourceContext_ReturnsEndpointLevelIgnoringOverrides()
    {
        var resolver = Create(
            endpointOverrides: new() { ["GitHubBackup"] = "Error" },
            globalOverrides: new() { ["GitHubBackup"] = "Error" },
            level: "Debug");

        resolver.Resolve(null).ShouldBe(LogEventLevel.Debug);
    }

    [Fact]
    public void Resolve_EndpointOverrideOff_ReturnsNull()
    {
        var resolver = Create(endpointOverrides: new() { ["GitHubBackup"] = "Off" }, level: "Verbose");

        resolver.Resolve(_source).ShouldBeNull();
    }

    [Theory]
    [InlineData(LogEventLevel.Warning, true)]
    [InlineData(LogEventLevel.Error, true)]
    [InlineData(LogEventLevel.Information, false)]
    public void IsEnabled_LevelOfEntry_ComparesWithResolvedLevel(LogEventLevel level, bool expected)
    {
        var resolver = Create(endpointOverrides: new() { ["GitHubBackup"] = "Warning" }, level: "Verbose");

        resolver.IsEnabled(CreateEvent(level, _source)).ShouldBe(expected);
    }

    [Fact]
    public void IsEnabled_SourceDroppedByOff_ReturnsFalse()
    {
        var resolver = Create(endpointOverrides: new() { ["GitHubBackup"] = "Off" });

        resolver.IsEnabled(CreateEvent(LogEventLevel.Fatal, _source)).ShouldBeFalse();
    }

    [Fact]
    public void IsEnabled_EntryWithoutSourceContext_UsesEndpointLevel()
    {
        var resolver = Create(level: "Warning");

        resolver.IsEnabled(CreateEvent(LogEventLevel.Information, null)).ShouldBeFalse();
        resolver.IsEnabled(CreateEvent(LogEventLevel.Warning, null)).ShouldBeTrue();
    }

    [Fact]
    public void LowestLevel_OverridesBelowLevel_ReturnsLowestOfAllRules()
    {
        var resolver = Create(
            endpointOverrides: new() { ["GitHubBackup"] = "Debug", ["System"] = "Off" },
            globalOverrides: new() { ["Microsoft"] = "Warning" },
            level: "Error");

        resolver.LowestLevel.ShouldBe(LogEventLevel.Debug);
    }

    [Fact]
    public void LowestLevel_GlobalOverrideBelowLevel_ReturnsGlobalOverride()
    {
        var resolver = Create(globalOverrides: new() { ["Microsoft"] = "Verbose" }, level: "Error");

        resolver.LowestLevel.ShouldBe(LogEventLevel.Verbose);
    }

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Debug, "Debug", false)]
    [InlineData("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Information, "Debug", true)]
    [InlineData("Microsoft.EntityFrameworkCore.Migrations", LogEventLevel.Information, "Warning", false)]
    [InlineData("Microsoft.EntityFrameworkCore.Migrations", LogEventLevel.Warning, "Warning", true)]
    [InlineData("System.Net.Http.HttpClient", LogEventLevel.Debug, "Debug", false)]
    [InlineData("System.Net.Http.HttpClient", LogEventLevel.Information, "Warning", false)]
    [InlineData("Microsoft", LogEventLevel.Debug, "Debug", false)]
    [InlineData("system.net", LogEventLevel.Debug, "Debug", false)]
    [InlineData("MicrosoftFoo.Component", LogEventLevel.Debug, "Debug", true)]
    [InlineData("SystemX", LogEventLevel.Debug, "Debug", true)]
    [InlineData("GitHubBackup.Infrastructure", LogEventLevel.Debug, "Debug", true)]
    public void IsEnabled_LibrarySourceWithoutOverride_NeedsAtLeastInformation(
        string sourceContext, LogEventLevel entryLevel, string endpointLevel, bool expected)
    {
        var resolver = Create(level: endpointLevel);

        resolver.IsEnabled(CreateEvent(entryLevel, sourceContext)).ShouldBe(expected);
    }

    [Fact]
    public void Resolve_LibrarySourceWithGlobalOverride_BypassesFloor()
    {
        var resolver = Create(globalOverrides: new() { ["Microsoft"] = "Debug" }, level: "Warning");

        resolver.Resolve("Microsoft.EntityFrameworkCore.Database.Command").ShouldBe(LogEventLevel.Debug);
        resolver.Resolve("System.Net.Http").ShouldBe(LogEventLevel.Warning);
    }

    [Fact]
    public void Resolve_LibrarySourceWithEndpointOverride_BypassesFloor()
    {
        var resolver = Create(endpointOverrides: new() { ["Microsoft.EntityFrameworkCore"] = "Debug" }, level: "Debug");

        resolver.Resolve("Microsoft.EntityFrameworkCore.Database.Command").ShouldBe(LogEventLevel.Debug);
        resolver.Resolve("Microsoft.Extensions.Hosting").ShouldBe(LogEventLevel.Information);
    }

    [Fact]
    public void Resolve_DefaultOptions_KeepsHostingLifetimeAtWarningAndFloorsOtherLibraries()
    {
        var resolver = EndpointLevelResolver.Create(new SerilogOptions(), new SerilogEndpointOptions { Level = "Debug" });

        resolver.Resolve(SerilogOptions.HostingLifetimeSource).ShouldBe(LogEventLevel.Warning);
        resolver.Resolve("Microsoft.Extensions.Hosting.Internal.Host").ShouldBe(LogEventLevel.Information);
        resolver.Resolve("GitHubBackup.Infrastructure.Processes.ProcessRunner").ShouldBe(LogEventLevel.Debug);
        resolver.LowestLevel.ShouldBe(LogEventLevel.Debug);
    }

    [Fact]
    public void Resolve_LibrarySourceEndpointOff_StaysOff()
    {
        var resolver = Create(level: "Off");

        resolver.Resolve("Microsoft.Extensions.Hosting").ShouldBeNull();
    }

    [Fact]
    public void LowestLevel_LibraryDebugOverride_LetsDebugThroughRoot()
    {
        var resolver = Create(globalOverrides: new() { ["Microsoft"] = "Debug" }, level: "Warning");

        resolver.LowestLevel.ShouldBe(LogEventLevel.Debug);
    }

    [Fact]
    public void Lowest_SeveralEndpoints_ReturnsLowestIgnoringNull() =>
        EndpointLevelResolver.Lowest([LogEventLevel.Warning, null, LogEventLevel.Debug, LogEventLevel.Error])
            .ShouldBe(LogEventLevel.Debug);

    [Fact]
    public void Lowest_NoEndpoint_ReturnsFatal() => EndpointLevelResolver.Lowest([]).ShouldBe(LogEventLevel.Fatal);

    /// <summary>
    /// Creates a resolver from configuration values, as <see cref="EndpointLevelResolver.Create"/> receives them.
    /// </summary>
    /// <param name="endpointOverrides">The overrides of the endpoint.</param>
    /// <param name="globalOverrides">The global overrides, which replace the default ones.</param>
    /// <param name="level">The level of the endpoint.</param>
    /// <param name="defaultLevel">The global default level.</param>
    /// <returns>The resolver.</returns>
    private static EndpointLevelResolver Create(
        Dictionary<string, string?>? endpointOverrides = null,
        Dictionary<string, string?>? globalOverrides = null,
        string? level = null,
        string defaultLevel = "Information")
    {
        var global = new SerilogOptions { Default = defaultLevel };
        global.Override.Clear();
        foreach (var (source, value) in globalOverrides ?? [])
        {
            global.Override[source] = value;
        }

        var endpoint = new SerilogEndpointOptions { Level = level };
        foreach (var (source, value) in endpointOverrides ?? [])
        {
            endpoint.Override[source] = value;
        }

        return EndpointLevelResolver.Create(global, endpoint);
    }

    /// <summary>
    /// Creates an entry of <paramref name="level"/> with the source context <paramref name="sourceContext"/>.
    /// </summary>
    /// <param name="level">The level of the entry.</param>
    /// <param name="sourceContext">The source context, or <see langword="null"/> for an entry without one.</param>
    /// <returns>The entry.</returns>
    private static LogEvent CreateEvent(LogEventLevel level, string? sourceContext) =>
        new(
            DateTimeOffset.UnixEpoch,
            level,
            null,
            new MessageTemplateParser().Parse("Entry"),
            sourceContext is null ? [] : [new LogEventProperty("SourceContext", new ScalarValue(sourceContext))]);
}
