using GitHubBackup.Core.Git;

namespace GitHubBackup.Core.Tests.Git;

/// <summary>
/// Tests <see cref="RefSnapshot"/>.
/// </summary>
public sealed class RefSnapshotTests
{
    private const string _commit = "1111111111111111111111111111111111111111";
    private const string _otherCommit = "2222222222222222222222222222222222222222";

    [Fact]
    public void Equals_SameRefsInDifferentOrder_ReturnsTrueWithEqualHashCodes()
    {
        var first = Snapshot(("heads/main", _commit), ("tags/v1", _otherCommit));
        var second = Snapshot(("tags/v1", _otherCommit), ("heads/main", _commit));

        first.Equals(second).ShouldBeTrue();
        first.Equals((object)second).ShouldBeTrue();
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }

    [Theory]
    [InlineData("heads/main", _otherCommit)]
    [InlineData("heads/develop", _commit)]
    [InlineData("tags/main", _commit)]
    public void Equals_DifferentRef_ReturnsFalse(string name, string objectId)
    {
        var snapshot = Snapshot(("heads/main", _commit));

        snapshot.Equals(Snapshot((name, objectId))).ShouldBeFalse();
    }

    [Fact]
    public void Equals_ExtraRef_ReturnsFalse()
    {
        var snapshot = Snapshot(("heads/main", _commit));

        snapshot.Equals(Snapshot(("heads/main", _commit), ("tags/v1", _commit))).ShouldBeFalse();
        snapshot.Equals(null).ShouldBeFalse();
    }

    [Fact]
    public void Equals_NamesDifferOnlyInCase_ReturnsFalse() =>
        Snapshot(("heads/Main", _commit)).Equals(Snapshot(("heads/main", _commit))).ShouldBeFalse();

    [Fact]
    public void Empty_NoRefs_EqualsNewEmptySnapshot()
    {
        RefSnapshot.Empty.Refs.ShouldBeEmpty();
        RefSnapshot.Empty.HasBranches.ShouldBeFalse();
        RefSnapshot.Empty.ShouldBe(new RefSnapshot([]));
    }

    [Fact]
    public void HasBranches_OnlyTags_ReturnsFalse()
    {
        Snapshot(("tags/v1", _commit)).HasBranches.ShouldBeFalse();
        Snapshot(("tags/v1", _commit), ("heads/main", _commit)).HasBranches.ShouldBeTrue();
    }

    [Theory]
    [InlineData("main", true)]
    [InlineData("feature/x", true)]
    [InlineData("v1", false)]
    [InlineData("Main", false)]
    public void ContainsBranch_Name_MatchesOnlyBranches(string name, bool expected)
    {
        var snapshot = Snapshot(("heads/main", _commit), ("heads/feature/x", _commit), ("tags/v1", _commit));

        snapshot.ContainsBranch(name).ShouldBe(expected);
    }

    [Theory]
    [InlineData("refs/heads/main")]
    [InlineData("heads/")]
    [InlineData("tags/")]
    [InlineData("HEAD")]
    [InlineData("pull/1/head")]
    public void Constructor_NameOutsideHeadsAndTags_Throws(string name) =>
        Should.Throw<ArgumentException>(() => Snapshot((name, _commit)));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_EmptyObjectId_Throws(string objectId) =>
        Should.Throw<ArgumentException>(() => Snapshot(("heads/main", objectId)));

    [Fact]
    public void Constructor_DuplicateName_Throws() =>
        Should.Throw<ArgumentException>(() => Snapshot(("heads/main", _commit), ("heads/main", _otherCommit)));

    [Fact]
    public void ToString_Refs_ListsThemSortedByName()
    {
        var snapshot = Snapshot(("tags/v1", "b"), ("heads/main", "a"));

        snapshot.ToString().ShouldBe("{ heads/main=a, tags/v1=b }");
        RefSnapshot.Empty.ToString().ShouldBe("{}");
    }

    /// <summary>
    /// Creates a snapshot of the given name and object id pairs.
    /// </summary>
    /// <param name="refs">The refs.</param>
    /// <returns>The snapshot.</returns>
    private static RefSnapshot Snapshot(params (string Name, string ObjectId)[] refs) =>
        new(refs.Select(entry => KeyValuePair.Create(entry.Name, entry.ObjectId)));
}
