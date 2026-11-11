using GitHubBackup.Core;

namespace GitHubBackup.Cli.Tests;

/// <summary>
/// Tests <see cref="SetTokenCommandHandler"/>.
/// </summary>
public sealed class SetTokenCommandHandlerTests
{
    [Fact]
    public async Task RunAsync_Called_ReturnsCriticalWithOneLine()
    {
        using var error = new StringWriter();
        var handler = new SetTokenCommandHandler(TestCliConsole.Create(error));

        var exitCode = await handler.RunAsync();

        exitCode.ShouldBe(ExitCode.Critical);
        error.ToString().ShouldBe("Error: The set-token command is not implemented yet." + Environment.NewLine);
    }
}
