using Microsoft.Extensions.DependencyInjection;

namespace GitHubBackup.Cli.Tests;

/// <summary>
/// Fails the <c>Build()</c> of a host by throwing a given exception when the service provider is created.
/// </summary>
/// <param name="exception">The exception to throw.</param>
internal sealed class ThrowingServiceProviderFactory(Exception exception) : IServiceProviderFactory<IServiceCollection>
{
    /// <inheritdoc />
    public IServiceCollection CreateBuilder(IServiceCollection services) => services;

    /// <inheritdoc />
    public IServiceProvider CreateServiceProvider(IServiceCollection containerBuilder) => throw exception;
}
