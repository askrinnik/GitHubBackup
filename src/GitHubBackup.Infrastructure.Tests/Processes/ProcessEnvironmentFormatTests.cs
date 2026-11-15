using GitHubBackup.Core.Processes;
using GitHubBackup.Infrastructure.Processes;

namespace GitHubBackup.Infrastructure.Tests.Processes;

/// <summary>
/// Tests <see cref="ProcessEnvironmentFormat"/>.
/// </summary>
public sealed class ProcessEnvironmentFormatTests
{
    [Fact]
    public void Format_PlainVariables_JoinsNameEqualsValue()
    {
        var text = ProcessEnvironmentFormat.Format(
            [new("GIT_TERMINAL_PROMPT", "0"), new("GIT_CONFIG_KEY_0", "core.longpaths")]);

        text.ShouldBe("GIT_TERMINAL_PROMPT=0, GIT_CONFIG_KEY_0=core.longpaths");
    }

    [Fact]
    public void Format_SecretVariable_ShowsStars()
    {
        var text = ProcessEnvironmentFormat.Format(
            [new("GIT_CONFIG_KEY_1", "http.https://github.com/.extraHeader"), new("GIT_CONFIG_VALUE_1", "Authorization: Basic abc", IsSecret: true)]);

        text.ShouldBe("GIT_CONFIG_KEY_1=http.https://github.com/.extraHeader, GIT_CONFIG_VALUE_1=***");
    }

    [Fact]
    public void Format_EmptySecretValue_ShowsStars()
    {
        var text = ProcessEnvironmentFormat.Format([new("SECRET", string.Empty, IsSecret: true)]);

        text.ShouldBe("SECRET=***");
    }

    [Fact]
    public void Format_EmptyPlainValue_ShowsNameAndEquals()
    {
        var text = ProcessEnvironmentFormat.Format(
            [new("GIT_CONFIG_KEY_2", "credential.helper"), new("GIT_CONFIG_VALUE_2", string.Empty)]);

        text.ShouldBe("GIT_CONFIG_KEY_2=credential.helper, GIT_CONFIG_VALUE_2=");
    }

    [Fact]
    public void Format_NoVariables_ReturnsEmpty()
    {
        var text = ProcessEnvironmentFormat.Format([]);

        text.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("two words")]
    [InlineData(@"C:\Program Files\Git\hooks")]
    [InlineData("a, b=c")]
    public void Format_ValueWithSpacesPathOrComma_KeepsValue(string value)
    {
        var text = ProcessEnvironmentFormat.Format([new ProcessEnvironmentVariable("NAME", value)]);

        text.ShouldBe($"NAME={value}");
    }
}
