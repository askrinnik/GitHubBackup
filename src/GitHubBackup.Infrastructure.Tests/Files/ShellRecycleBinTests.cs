using GitHubBackup.Infrastructure.Files;

namespace GitHubBackup.Infrastructure.Tests.Files;

/// <summary>
/// Tests the decision of <see cref="ShellRecycleBin"/> that refuses a deletion the Shell would make permanent.
/// </summary>
public sealed class ShellRecycleBinTests
{
    [Theory]
    [InlineData(0x80u, true)]
    [InlineData(0x80u | 0x40u | 0x800u, true)]
    [InlineData(0u, false)]
    [InlineData(0x40u | 0x800u, false)]
    [InlineData(0xFFFF_FF7Fu, false)]
    public void ShouldAllowDelete_Flags_AllowsOnlyRecycling(uint flags, bool expected) =>
        ShellRecycleBin.ShouldAllowDelete(flags).ShouldBe(expected);

    [Fact]
    public void OperationFlags_Always_AskForUndoNoUiRecycleAndEarlyFailure() =>
        ShellRecycleBin.OperationFlags.ShouldBe(0x0018_0654u);
}
