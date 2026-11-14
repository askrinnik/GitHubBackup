using GitHubBackup.Core.Clones;
using GitHubBackup.Core.State;

namespace GitHubBackup.Core.Tests.Clones;

/// <summary>
/// Tests <see cref="CloneLocations"/>.
/// </summary>
public sealed class CloneLocationsTests
{
    private const string _url = "https://github.com/octo/app.git";
    private const string _wikiUrl = "https://github.com/octo/app.wiki.git";

    [Fact]
    public void Get_EntryRecorded_ReturnsFolderAndUrlOfTarget()
    {
        var locations = new CloneLocations(
            [KeyValuePair.Create(1L, new CloneLocation(@"D:\Work\App", @"D:\Work\App.wiki", _url, _wikiUrl))]);

        locations.Get(1, BackupTarget.Repository).ShouldBe(@"D:\Work\App");
        locations.Get(1, BackupTarget.Wiki).ShouldBe(@"D:\Work\App.wiki");
        locations.GetRemoteUrl(1, BackupTarget.Repository).ShouldBe(_url);
        locations.GetRemoteUrl(1, BackupTarget.Wiki).ShouldBe(_wikiUrl);
        locations.Get(2, BackupTarget.Repository).ShouldBeNull();
        locations.GetRemoteUrl(2, BackupTarget.Repository).ShouldBeNull();
        locations.IsChanged.ShouldBeFalse();
    }

    [Fact]
    public void Set_NewFolder_RecordsItForTargetOnlyAndMarksChanged()
    {
        var locations = new CloneLocations();

        locations.Set(1, BackupTarget.Wiki, @"D:\Work\App.wiki", _wikiUrl);

        locations.Entries[1].ShouldBe(new CloneLocation(null, @"D:\Work\App.wiki", null, _wikiUrl));
        locations.IsChanged.ShouldBeTrue();
    }

    [Fact]
    public void Set_SameFolderAndUrl_DoesNotMarkChanged()
    {
        var locations = new CloneLocations([KeyValuePair.Create(1L, new CloneLocation(@"D:\Work\App", null, _url))]);

        locations.Set(1, BackupTarget.Repository, @"D:\Work\App", _url);

        locations.IsChanged.ShouldBeFalse();
    }

    [Fact]
    public void Set_IdNotPositive_Throws() =>
        Should.Throw<ArgumentOutOfRangeException>(() => new CloneLocations().Set(0, BackupTarget.Repository, @"D:\x", _url));
}
