using GitHubBackup.Core.Logging;
using GitHubBackup.Core.Runs;
using GitHubBackup.Core.Security;
using GitHubBackup.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Extensions.Logging;
using Serilog.Formatting;
using Serilog.Formatting.Compact;

namespace GitHubBackup.Infrastructure.Logging;

/// <summary>
/// Registers Serilog as the provider behind <c>ILogger&lt;T&gt;</c>, writing to the log endpoints: the daily files named
/// by <see cref="LogFiles"/>, the console, the <see cref="ILiveLogFeed"/> and the logger providers of the container.
/// </summary>
public static class FileLoggingServiceCollectionExtensions
{
    /// <param name="services">The service collection of the host.</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds Serilog with the levels of <see cref="SerilogOptions"/> and <see cref="SerilogEndpointsOptions"/>, the
        /// run identifier in every entry and secrets masked in the files, the console and the
        /// <see cref="ILiveLogFeed"/>.
        /// </summary>
        /// <remarks>
        /// The logger is created when the host is built, so an invalid level fails <c>Build()</c> with an
        /// <see cref="OptionsValidationException"/> that holds the failures of both options. The logger belongs to the
        /// service provider and is disposed, with the files flushed and closed, when the host is disposed; the static
        /// <see cref="Log.Logger"/> is left untouched. The files are always attached; the live log feed only after
        /// <see cref="AddGitHubBackupLiveLogBuffer"/>, the console only after <see cref="AddGitHubBackupConsoleLog"/>,
        /// and the logger providers of the container only when there are any, which <c>AddGitHubBackupTelemetry</c>
        /// registers when telemetry is enabled. The providers receive the entries that pass the levels of the
        /// <c>OpenTelemetry</c> endpoint, with the run identifier and the scope properties; Serilog does not mask them,
        /// so such a provider masks secrets itself. Requires <see cref="IHostEnvironment"/>, <see cref="IRunContext"/>
        /// and <see cref="ISecretMasker"/> to be registered.
        /// </remarks>
        /// <param name="configuration">The application configuration.</param>
        /// <returns>The same service collection, for chaining.</returns>
        public IServiceCollection AddGitHubBackupLogging(IConfiguration configuration)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SerilogOptions>, SerilogOptionsValidator>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SerilogEndpointsOptions>>(
                new SerilogEndpointsOptionsValidator(configuration)));
            services.AddOptions<SerilogOptions>().BindSection(configuration, SerilogOptions.SectionName).ValidateOnStart();
            services.AddOptions<SerilogEndpointsOptions>()
                .BindSection(configuration, SerilogEndpointsOptions.SectionName)
                .ValidateOnStart();
            services.AddOptions<LiveLogOptions>()
                .Validate(
                    static options => options.BufferCapacity is >= 0 and <= LiveLogOptions.MaxBufferCapacity,
                    $"{nameof(LiveLogOptions)}:{nameof(LiveLogOptions.BufferCapacity)} must be from 0 to {LiveLogOptions.MaxBufferCapacity}.")
                .ValidateOnStart();
            services.TryAddSingleton<LiveLogSink>();
            services.TryAddSingleton<ILiveLogFeed>(provider => provider.GetRequiredService<LiveLogSink>());
            services.AddSerilog(ConfigureLogger, preserveStaticLogger: true, writeToProviders: false);

            return services;
        }

        /// <summary>
        /// Attaches the live log feed endpoint and makes the <see cref="ILiveLogFeed"/> keep the last
        /// <paramref name="capacity"/> entries, so a subscriber that comes later still receives the entries written
        /// since the host started.
        /// </summary>
        /// <remarks>
        /// Only a host that shows the feed calls it; without it the feed receives and keeps no entries. The value is
        /// validated on start with the other options of <see cref="AddGitHubBackupLogging"/>.
        /// </remarks>
        /// <param name="capacity">The number of entries kept, from <c>0</c> to <see cref="LiveLogOptions.MaxBufferCapacity"/>.</param>
        /// <returns>The same service collection, for chaining.</returns>
        public IServiceCollection AddGitHubBackupLiveLogBuffer(int capacity)
        {
            services.Configure<LiveLogOptions>(options =>
            {
                options.BufferCapacity = capacity;
                options.Enabled = true;
            });
            return services;
        }

        /// <summary>
        /// Attaches the console endpoint, which writes entries to <paramref name="writer"/> in the layout of the text
        /// log file with secrets masked.
        /// </summary>
        /// <param name="writer">The writer of the console log, normally standard error.</param>
        /// <returns>The same service collection, for chaining.</returns>
        public IServiceCollection AddGitHubBackupConsoleLog(TextWriter writer)
        {
            services.AddSingleton(new ConsoleLogTarget(writer));
            return services;
        }
    }

    /// <summary>
    /// Configures the root threshold, the run identifier and one sub-logger per attached endpoint.
    /// </summary>
    /// <remarks>
    /// The root has no overrides: its threshold is the lowest level any attached endpoint needs, and each sub-logger
    /// filters the entries by the levels of its endpoint. An endpoint whose level is <c>Off</c> is not attached.
    /// </remarks>
    /// <param name="provider">The service provider of the host.</param>
    /// <param name="configuration">The logger configuration to fill.</param>
    private static void ConfigureLogger(IServiceProvider provider, LoggerConfiguration configuration)
    {
        var (options, endpoints) = GetValidatedOptions(provider);
        var contentRootPath = provider.GetRequiredService<IHostEnvironment>().ContentRootPath;
        var masker = provider.GetRequiredService<ISecretMasker>();

        List<(EndpointLevelResolver Resolver, Action<LoggerConfiguration> WriteTo)> attached = [];
        void Attach(LogEndpoint endpoint, Action<LoggerConfiguration> writeTo)
        {
            var endpointOptions = endpoints.Get(endpoint);
            if (!string.IsNullOrEmpty(endpointOptions.Level) && LogLevels.ParseEndpointLevel(endpointOptions.Level) is null)
            {
                return;
            }

            attached.Add((EndpointLevelResolver.Create(options, endpointOptions), writeTo));
        }

        Attach(LogEndpoint.File, endpoint => WriteToDailyFiles(endpoint, contentRootPath, masker));
        if (provider.GetService<ConsoleLogTarget>() is { } console)
        {
            Attach(LogEndpoint.Console, endpoint => endpoint.WriteTo.Sink(new TextWriterLogSink(console.Writer, masker)));
        }

        if (provider.GetRequiredService<IOptions<LiveLogOptions>>().Value.Enabled)
        {
            Attach(LogEndpoint.LiveLog, endpoint => endpoint.WriteTo.Sink(provider.GetRequiredService<LiveLogSink>()));
        }

        var loggerProviders = provider.GetServices<ILoggerProvider>().ToArray();
        if (loggerProviders.Length > 0)
        {
            Attach(LogEndpoint.OpenTelemetry, endpoint => WriteToProviders(endpoint, loggerProviders));
        }

        configuration.MinimumLevel.Is(EndpointLevelResolver.Lowest(attached.Select(pair => pair.Resolver.LowestLevel)));
        configuration.Enrich.With(new RunIdEnricher(provider.GetRequiredService<IRunContext>()));
        foreach (var (resolver, writeTo) in attached)
        {
            configuration.WriteTo.Logger(endpoint =>
            {
                // A sub-logger has a threshold of its own, which must not drop what the resolver lets through.
                endpoint.MinimumLevel.Verbose().Filter.ByIncludingOnly(resolver.IsEnabled);
                writeTo(endpoint);
            });
        }
    }

    /// <summary>
    /// Returns the validated <see cref="SerilogOptions"/> and <see cref="SerilogEndpointsOptions"/>.
    /// </summary>
    /// <param name="provider">The service provider of the host.</param>
    /// <returns>Both options.</returns>
    /// <exception cref="OptionsValidationException">Either options are invalid; it holds the failures of both.</exception>
    private static (SerilogOptions Options, SerilogEndpointsOptions Endpoints) GetValidatedOptions(IServiceProvider provider)
    {
        List<string> failures = [];
        var options = GetValue<SerilogOptions>(provider, failures);
        var endpoints = GetValue<SerilogEndpointsOptions>(provider, failures);
        if (options is null || endpoints is null)
        {
            throw new OptionsValidationException(Options.DefaultName, typeof(SerilogOptions), failures);
        }

        return (options, endpoints);
    }

    /// <summary>
    /// Returns the validated value of <typeparamref name="TOptions"/>, or collects its failures.
    /// </summary>
    /// <typeparam name="TOptions">The options type.</typeparam>
    /// <param name="provider">The service provider of the host.</param>
    /// <param name="failures">The list that receives the failures.</param>
    /// <returns>The options, or <see langword="null"/> when they are invalid.</returns>
    private static TOptions? GetValue<TOptions>(IServiceProvider provider, List<string> failures)
        where TOptions : class
    {
        try
        {
            return provider.GetRequiredService<IOptions<TOptions>>().Value;
        }
        catch (OptionsValidationException exception)
        {
            failures.AddRange(exception.Failures);
            return null;
        }
    }

    /// <summary>
    /// Adds a sink that hands entries to the logger providers of the container.
    /// </summary>
    /// <remarks>
    /// Each provider receives an entry once, because the logger factory of Serilog is registered without forwarding
    /// entries to the providers itself.
    /// </remarks>
    /// <param name="configuration">The logger configuration of the endpoint.</param>
    /// <param name="loggerProviders">The logger providers of the container.</param>
    private static void WriteToProviders(LoggerConfiguration configuration, IEnumerable<ILoggerProvider> loggerProviders)
    {
        var collection = new LoggerProviderCollection();
        foreach (var loggerProvider in loggerProviders)
        {
            collection.AddProvider(loggerProvider);
        }

        configuration.WriteTo.Providers(collection);
    }

    /// <summary>
    /// Adds the sinks of the text and the structured daily files in the log folder of the content root, both masked.
    /// </summary>
    /// <param name="configuration">The logger configuration.</param>
    /// <param name="contentRootPath">The folder of the executable.</param>
    /// <param name="masker">The masker applied to every rendered entry.</param>
    internal static void WriteToDailyFiles(
        LoggerConfiguration configuration, string contentRootPath, ISecretMasker masker)
    {
        WriteToDailyFile(
            configuration,
            new TextLogFormatter(),
            masker,
            LogFiles.GetPathTemplate(contentRootPath, LogFiles.TextExtension));
        WriteToDailyFile(
            configuration,
            new CompactJsonFormatter(),
            masker,
            LogFiles.GetPathTemplate(contentRootPath, LogFiles.JsonExtension));
    }

    /// <summary>
    /// Adds a sink that writes masked entries to one file per local day and never deletes old files.
    /// </summary>
    /// <remarks>
    /// The file is shared so that the console and the WPF executables can write to the same day's file at once.
    /// </remarks>
    /// <param name="configuration">The logger configuration.</param>
    /// <param name="formatter">The formatter that renders an entry.</param>
    /// <param name="masker">The masker applied to the rendered entry.</param>
    /// <param name="pathTemplate">The path returned by <see cref="LogFiles.GetPathTemplate"/>.</param>
    private static void WriteToDailyFile(
        LoggerConfiguration configuration, ITextFormatter formatter, ISecretMasker masker, string pathTemplate) =>
        configuration.WriteTo.File(
            new MaskingTextFormatter(formatter, masker),
            pathTemplate,
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: null,
            shared: true);
}
