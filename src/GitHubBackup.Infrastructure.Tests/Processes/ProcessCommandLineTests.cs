using GitHubBackup.Infrastructure.Processes;

namespace GitHubBackup.Infrastructure.Tests.Processes;

/// <summary>
/// Tests <see cref="ProcessCommandLine"/>.
/// </summary>
public sealed class ProcessCommandLineTests
{
    [Fact]
    public void Format_PlainArguments_JoinsWithSpaces()
    {
        var commandLine = ProcessCommandLine.Format(@"C:\Git\cmd\git.exe", ["fetch", "--prune", "origin"]);

        commandLine.ShouldBe(@"C:\Git\cmd\git.exe fetch --prune origin");
    }

    [Fact]
    public void Format_NoArguments_ReturnsFileName()
    {
        var commandLine = ProcessCommandLine.Format(@"C:\Git\cmd\git.exe", []);

        commandLine.ShouldBe(@"C:\Git\cmd\git.exe");
    }

    [Fact]
    public void Format_FileNameWithSpaces_QuotesFileName()
    {
        var commandLine = ProcessCommandLine.Format(@"C:\Program Files\7-Zip\7z.exe", ["t"]);

        commandLine.ShouldBe(@"""C:\Program Files\7-Zip\7z.exe"" t");
    }

    [Theory]
    [InlineData("", @"""""")]
    [InlineData("a b", @"""a b""")]
    [InlineData("tab\tseparated", "\"tab\tseparated\"")]
    [InlineData(@"say ""hi""", @"""say \""hi\""""")]
    [InlineData(@"C:\folder with space\", @"""C:\folder with space\\""")]
    [InlineData(@"back\""quote", @"""back\\\""quote""")]
    [InlineData(@"C:\plain\path", @"C:\plain\path")]
    public void Format_Argument_QuotesWhenNeeded(string argument, string expected)
    {
        var commandLine = ProcessCommandLine.Format(@"C:\tool.exe", [argument]);

        commandLine.ShouldBe(@"C:\tool.exe " + expected);
    }
}
