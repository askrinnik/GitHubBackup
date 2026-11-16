using System.Diagnostics.CodeAnalysis;
using GitHubBackup.Core.Archiving;
using GitHubBackup.Core.Cleanup;
using GitHubBackup.Core.Git;
using GitHubBackup.Core.History;
using GitHubBackup.Core.State;

namespace GitHubBackup.Core.Tests.Cleanup;

/// <summary>
/// Tests <see cref="ArchiveCleanupPlanner"/> and the equality of <see cref="ArchiveCleanupPlan"/>.
/// </summary>
public sealed class ArchiveCleanupPlannerTests
{
    private const string _folder = @"C:\backup\octo\tool";

    private static readonly DateTimeOffset _firstCreatedAt = new(2026, 10, 1, 9, 0, 0, TimeSpan.FromHours(2));

    private readonly List<(BackupTarget Target, string File, bool Exists, long Size)> _records = [];

    private readonly List<(BackupTarget Target, string File, long Size)> _unrecorded = [];

    [Fact]
    public void Plan_NoRecordsAndNoFiles_IsEmpty()
    {
        var plan = Plan();

        plan.IsEmpty.ShouldBeTrue();
        plan.ShouldBe(ArchiveCleanupPlan.Empty);
    }

    [Fact]
    public void Plan_OnlyLastRecordWithFile_IsEmpty()
    {
        Record("last.zip", exists: true);

        Plan().IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Plan_OlderRecordsWithFiles_RecyclesAllButLastInStateOrder()
    {
        Record("a.zip", exists: true, size: 100);
        Record("b.zip", exists: true, size: 200);
        Record("last.zip", exists: true, size: 300);

        var plan = Plan();

        plan.FilesToRecycle.ShouldBe(
        [
            new(BackupTarget.Repository, "a.zip", $@"{_folder}\a.zip", 100, IsInState: true),
            new(BackupTarget.Repository, "b.zip", $@"{_folder}\b.zip", 200, IsInState: true),
        ]);
        plan.RecordsWithoutFiles.ShouldBeEmpty();
        (plan.FileCount, plan.TotalSizeBytes, plan.RecordCount).ShouldBe((2, 300L, 0));
    }

    [Fact]
    public void Plan_OlderRecordsWithoutFiles_RemovesTheirRecords()
    {
        Record("gone1.zip", exists: false);
        Record("gone2.zip", exists: false);
        Record("last.zip", exists: true);

        var plan = Plan();

        plan.FilesToRecycle.ShouldBeEmpty();
        plan.RecordsWithoutFiles.ShouldBe(
            [new(BackupTarget.Repository, "gone1.zip"), new(BackupTarget.Repository, "gone2.zip")]);
        plan.RecordCount.ShouldBe(2);
    }

    [Fact]
    public void Plan_LastFileMissing_KeepsLastRecordAndNewestRecordWithFile()
    {
        Record("old-gone.zip", exists: false);
        Record("old.zip", exists: true);
        Record("newest-on-disk.zip", exists: true);
        Record("middle-gone.zip", exists: false);
        Record("last.zip", exists: false);

        var plan = Plan();

        plan.FilesToRecycle.Select(file => file.File).ShouldBe(["old.zip"]);
        plan.RecordsWithoutFiles.Select(record => record.File).ShouldBe(["old-gone.zip", "middle-gone.zip"]);
    }

    [Fact]
    public void Plan_NoRecordHasFile_KeepsLastRecordAndLeavesUnrecordedFiles()
    {
        Record("old-gone.zip", exists: false);
        Record("last.zip", exists: false);
        Unrecorded("octo_tool_20261003-100000_abcdef0123.zip");

        var plan = Plan();

        plan.FilesToRecycle.ShouldBeEmpty();
        plan.RecordsWithoutFiles.ShouldBe([new(BackupTarget.Repository, "old-gone.zip")]);
    }

    [Fact]
    public void Plan_UnrecordedFilesAndKeptFile_RecyclesThemOrderedByName()
    {
        Record("last.zip", exists: true);
        Unrecorded("z.zip", size: 5);
        Unrecorded("B.zip", size: 7);

        var plan = Plan();

        plan.FilesToRecycle.ShouldBe(
        [
            new(BackupTarget.Repository, "B.zip", $@"{_folder}\B.zip", 7, IsInState: false),
            new(BackupTarget.Repository, "z.zip", $@"{_folder}\z.zip", 5, IsInState: false),
        ]);
    }

    [Fact]
    public void Plan_UnrecordedFilesWhenOnlyNewestRecordWithFileIsKept_RecyclesThem()
    {
        Record("on-disk.zip", exists: true);
        Record("last.zip", exists: false);
        Unrecorded("extra.zip");

        var plan = Plan();

        plan.FilesToRecycle.Select(file => file.File).ShouldBe(["extra.zip"]);
        plan.RecordsWithoutFiles.ShouldBeEmpty();
    }

    [Fact]
    public void Plan_UnrecordedFilesOfTargetWithoutRecords_LeavesThem()
    {
        Unrecorded("extra.zip");

        Plan().IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Plan_RepositoryAndWiki_DecidesEachTargetSeparatelyRepositoryFirst()
    {
        Record("wiki-old.zip", exists: true, target: BackupTarget.Wiki);
        Record("repo-old.zip", exists: true);
        Record("wiki-last.zip", exists: true, target: BackupTarget.Wiki);
        Record("repo-last.zip", exists: true);

        var plan = Plan();

        plan.FilesToRecycle.Select(file => (file.Target, file.File)).ShouldBe(
            [(BackupTarget.Repository, "repo-old.zip"), (BackupTarget.Wiki, "wiki-old.zip")]);
    }

    [Fact]
    public void Plan_WikiKeepsNoFileButRepositoryDoes_LeavesUnrecordedWikiFiles()
    {
        Record("repo-last.zip", exists: true);
        Record("wiki-last.zip", exists: false, target: BackupTarget.Wiki);
        Unrecorded("wiki-extra.zip", target: BackupTarget.Wiki);
        Unrecorded("repo-extra.zip");

        var plan = Plan();

        plan.FilesToRecycle.Select(file => (file.Target, file.File)).ShouldBe(
            [(BackupTarget.Repository, "repo-extra.zip")]);
    }

    [Fact]
    public void Plan_HistoryNamesRecordedFileInOtherCase_MatchesRecord()
    {
        var state = BackupState.CreateEmpty(1, "octo/tool")
            .WithArchive(BackupTarget.Repository, CreateRecord("old.zip", 0), "octo/tool")
            .WithArchive(BackupTarget.Repository, CreateRecord("last.zip", 1), "octo/tool");
        var history = new ArchiveHistory(_folder, true,
        [
            CreateEntry(BackupTarget.Repository, "LAST.ZIP", 1, exists: true, isInState: true, 10),
            CreateEntry(BackupTarget.Repository, "OLD.ZIP", 0, exists: true, isInState: true, 10),
        ]);

        var plan = ArchiveCleanupPlanner.Plan(state, history, new FakeNameBuilder(), "octo/tool");

        plan.FilesToRecycle.ShouldHaveSingleItem().File.ShouldBe("old.zip");
        plan.RecordsWithoutFiles.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(".backup-state.json")]
    [InlineData(".backup-state.json.bak")]
    [InlineData("notes.txt")]
    public void Plan_RecordNamesNonArchiveFile_NeitherRecyclesFileNorDropsRecord(string file)
    {
        Record(file, exists: true);
        Record("last.zip", exists: true);

        Plan().IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Plan_LastRecordNamesNonArchiveFile_KeepsNewestArchiveOnDisk()
    {
        Record("old.zip", exists: true);
        Record("newest.zip", exists: true);
        Record("notes.txt", exists: true);

        var plan = Plan();

        plan.FilesToRecycle.ShouldHaveSingleItem().File.ShouldBe("old.zip");
        plan.RecordsWithoutFiles.ShouldBeEmpty();
    }

    [Fact]
    public void Plan_OnlyNonArchiveRecordOnDisk_LeavesUnrecordedFilesAlone()
    {
        Record("notes.txt", exists: true);
        Unrecorded("orphan.zip");

        Plan().IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Plan_RecordNamesArchiveOfOtherTarget_DoesNotRecycleFile()
    {
        Record("repo-old.zip", exists: true, target: BackupTarget.Wiki);
        Record("wiki-last.zip", exists: true, target: BackupTarget.Wiki);

        Plan().IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Plan_RecordNamesMissingNonArchiveFile_DropsRecord()
    {
        Record(".backup-state.json", exists: false);
        Record("last.zip", exists: true);

        Plan().RecordsWithoutFiles.ShouldBe([new(BackupTarget.Repository, ".backup-state.json")]);
    }

    [Fact]
    public void Plan_SameInputsTwice_PlansAreEqual()
    {
        Record("old.zip", exists: true);
        Record("gone.zip", exists: false);
        Record("last.zip", exists: true);
        Unrecorded("extra.zip");

        var first = Plan();
        var second = Plan();

        first.ShouldNotBeSameAs(second);
        first.ShouldBe(second);
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }

    [Fact]
    public void Equals_FileSizeDiffers_PlansDiffer()
    {
        var file = new ArchiveCleanupFile(BackupTarget.Repository, "a.zip", $@"{_folder}\a.zip", 10, true);

        new ArchiveCleanupPlan([file], []).ShouldNotBe(new ArchiveCleanupPlan([file with { SizeBytes = 11 }], []));
    }

    [Fact]
    public void Equals_RecordsDiffer_PlansDiffer() =>
        new ArchiveCleanupPlan([], [new(BackupTarget.Repository, "a.zip")])
            .ShouldNotBe(new ArchiveCleanupPlan([], [new(BackupTarget.Wiki, "a.zip")]));

    [Fact]
    public void Equals_EmptyLists_EqualEmpty() =>
        new ArchiveCleanupPlan([], []).ShouldBe(ArchiveCleanupPlan.Empty);

    /// <summary>
    /// Adds a record after the existing ones; each record is created one hour after the previous one.
    /// </summary>
    /// <param name="file">The file name.</param>
    /// <param name="exists">Whether the file is on the disk.</param>
    /// <param name="size">The size of the file on the disk.</param>
    /// <param name="target">The target of the record.</param>
    private void Record(string file, bool exists, long size = 10, BackupTarget target = BackupTarget.Repository) =>
        _records.Add((target, file, exists, size));

    /// <summary>
    /// Adds an archive file without a record.
    /// </summary>
    /// <param name="file">The file name.</param>
    /// <param name="size">The size of the file.</param>
    /// <param name="target">The target the name belongs to.</param>
    private void Unrecorded(string file, long size = 10, BackupTarget target = BackupTarget.Repository) =>
        _unrecorded.Add((target, file, size));

    /// <summary>
    /// Builds the state and the history of the added records and files and plans the cleanup.
    /// </summary>
    /// <returns>The plan.</returns>
    private ArchiveCleanupPlan Plan()
    {
        var state = BackupState.CreateEmpty(1, "octo/tool");
        var entries = new List<ArchiveHistoryEntry>();
        for (var index = 0; index < _records.Count; index++)
        {
            var (target, file, exists, size) = _records[index];
            state = state.WithArchive(target, CreateRecord(file, index), "octo/tool");
            entries.Add(CreateEntry(target, file, index, exists, isInState: true, size));
        }

        entries.AddRange(_unrecorded.Select(file =>
            CreateEntry(file.Target, file.File, 100, exists: true, isInState: false, file.Size)));
        return ArchiveCleanupPlanner.Plan(
            state,
            new(_folder, true, [.. entries.OrderByDescending(entry => entry.CreatedAt)]),
            new FakeNameBuilder(),
            "octo/tool");
    }

    /// <summary>
    /// Accepts every <c>.zip</c> name; a name starting with <c>wiki</c> belongs to the wiki, any other to the repository.
    /// </summary>
    private sealed class FakeNameBuilder : IArchiveNameBuilder
    {
        /// <inheritdoc />
        public ArchiveName Build(string owner, string repository, BackupTarget target, string headCommit) =>
            throw new NotSupportedException();

        /// <inheritdoc />
        public bool TryParse(
            string fileName, string owner, string repository, [NotNullWhen(true)] out ParsedArchiveName? parsed)
        {
            var isArchive = fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            parsed = isArchive
                ? new(fileName.StartsWith("wiki", StringComparison.Ordinal) ? BackupTarget.Wiki : BackupTarget.Repository,
                    _firstCreatedAt, "abcdef0123")
                : null;
            return isArchive;
        }
    }

    /// <summary>
    /// Creates a record created <paramref name="hours"/> hours after the first one.
    /// </summary>
    /// <param name="file">The file name.</param>
    /// <param name="hours">The hours after the first record.</param>
    /// <returns>The record.</returns>
    private static ArchiveRecord CreateRecord(string file, int hours) =>
        new(file, _firstCreatedAt.AddHours(hours), "main", "a1b2c3", 10, RefSnapshot.Empty);

    /// <summary>
    /// Creates a history entry.
    /// </summary>
    /// <param name="target">The target.</param>
    /// <param name="file">The file name.</param>
    /// <param name="hours">The hours after the first record.</param>
    /// <param name="exists">Whether the file is on the disk.</param>
    /// <param name="isInState">Whether the state file records the archive.</param>
    /// <param name="size">The size of the file on the disk.</param>
    /// <returns>The entry.</returns>
    private static ArchiveHistoryEntry CreateEntry(
        BackupTarget target, string file, int hours, bool exists, bool isInState, long size) => new(
        target,
        file,
        $@"{_folder}\{file}",
        _firstCreatedAt.AddHours(hours),
        isInState ? "main" : null,
        "a1b2c3",
        10,
        [],
        exists,
        isInState,
        exists ? size : null);
}
