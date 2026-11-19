using GitHubBackup.Infrastructure.Hosting;

namespace GitHubBackup.Infrastructure.Tests.Hosting;

/// <summary>
/// Tests <see cref="GitHubBackupHostSettings"/>.
/// </summary>
public sealed class GitHubBackupHostSettingsTests
{
    [Fact]
    public void ResolveOtlpHeadersCredentialTarget_TestTarget_ReturnsIt()
    {
        var target = GitHubBackupHostSettings.TestOtlpHeadersCredentialTargetPrefix + "abc:OpenTelemetry:Headers";

        GitHubBackupHostSettings.ResolveOtlpHeadersCredentialTarget(target).ShouldBe(target);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("GitHubBackup.Tests:")]
    [InlineData("GitHubBackup:https://github.com")]
    [InlineData("githubbackup.tests:abc")]
    [InlineData("Other:GitHubBackup.Tests:abc")]
    public void ResolveOtlpHeadersCredentialTarget_NotATestTarget_ReturnsDefault(string? variableValue) =>
        GitHubBackupHostSettings.ResolveOtlpHeadersCredentialTarget(variableValue)
            .ShouldBe(GitHubBackupHostSettings.DefaultOtlpHeadersCredentialTarget);
}
