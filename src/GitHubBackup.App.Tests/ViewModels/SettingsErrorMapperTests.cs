using GitHubBackup.App.ViewModels;

namespace GitHubBackup.App.Tests.ViewModels;

/// <summary>
/// Tests <see cref="SettingsErrorMapper"/>.
/// </summary>
public sealed class SettingsErrorMapperTests
{
    private static readonly string[] _locations =
    [
        "backupRoot",
        "exclude",
        "accounts[1]",
        "accounts[1].url",
        "accounts[1].repositories[0]",
        "accounts[1].repositories[0].branch",
        "repositories[0]",
    ];

    [Theory]
    [InlineData("backupRoot must be an absolute path.", "backupRoot")]
    [InlineData("exclude[2] must not be null.", "exclude")]
    [InlineData("accounts[1].url names the same account as accounts[0].url.", "accounts[1].url")]
    [InlineData("accounts[1].repositories[0].branch must be null or a branch name.", "accounts[1].repositories[0].branch")]
    [InlineData("accounts[1].repositories[0].id is the same as repositories[0].id.", "accounts[1].repositories[0]")]
    [InlineData("accounts[1] must not be null.", "accounts[1]")]
    [InlineData("repositories[0] has the same archive folder 'D:\\a' as accounts[1].repositories[0].", "repositories[0]")]
    public void FindLocation_KnownLocation_ReturnsLongestMatch(string message, string expected) =>
        SettingsErrorMapper.FindLocation(message, _locations).ShouldBe(expected);

    [Theory]
    [InlineData("accounts[10].url must be an account URL.")]
    [InlineData("backupRootX must be set.")]
    [InlineData("schemaVersion 2 is not supported; the supported version is 1.")]
    [InlineData("")]
    public void FindLocation_UnknownLocation_ReturnsNull(string message) =>
        SettingsErrorMapper.FindLocation(message, _locations).ShouldBeNull();
}
