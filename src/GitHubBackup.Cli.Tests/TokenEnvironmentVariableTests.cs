using GitHubBackup.Core.Authentication;
using GitHubBackup.TestUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace GitHubBackup.Cli.Tests;

/// <summary>
/// Tests that the console host finds the token in the prefixed environment variable.
/// </summary>
[Collection(ProcessEnvironmentTestGroup.Name)]
public sealed class TokenEnvironmentVariableTests : IDisposable
{
    /// <summary>The prefixed variable that sets <c>GitHub:Token</c>.</summary>
    internal const string TokenVariable = "GitHubBackup__GitHub__Token";

    private readonly string? _previousValue = Environment.GetEnvironmentVariable(TokenVariable);
    private readonly TemporaryContentRoot _contentRoot = new();

    [Fact]
    public async Task BuildHost_PrefixedTokenVariable_TokenProviderFindsToken()
    {
        Environment.SetEnvironmentVariable(TokenVariable, "env-token-value");

        var token = await GetTokenAsync();

        token.Value.ShouldBe("env-token-value");
        token.IsAnonymous.ShouldBeFalse();
    }

    [Fact]
    public async Task BuildHost_NoTokenVariable_TokenProviderReturnsAnonymous()
    {
        Environment.SetEnvironmentVariable(TokenVariable, null);

        var token = await GetTokenAsync();

        token.IsAnonymous.ShouldBeTrue();
    }

    /// <summary>
    /// Restores the variable the tests change and deletes the content root.
    /// </summary>
    public void Dispose()
    {
        Environment.SetEnvironmentVariable(TokenVariable, _previousValue);
        _contentRoot.Dispose();
    }

    /// <summary>
    /// Builds the console host over the valid settings file and asks its token provider for the token.
    /// </summary>
    /// <returns>The token found.</returns>
    private async Task<GitHubToken> GetTokenAsync()
    {
        using var host = CliApplication.BuildHost(
            CliApplicationTests.CreateSettings(_contentRoot.FolderPath, CliApplicationTests.ValidAppSettings));
        return await host.Services.GetRequiredService<ITokenProvider>().GetTokenAsync(TestContext.Current.CancellationToken);
    }
}
