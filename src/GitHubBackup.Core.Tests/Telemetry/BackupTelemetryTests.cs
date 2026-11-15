using System.Diagnostics;
using GitHubBackup.Core.Changes;
using GitHubBackup.Core.Git;
using GitHubBackup.Core.Runs;
using GitHubBackup.Core.State;
using GitHubBackup.Core.Telemetry;

namespace GitHubBackup.Core.Tests.Telemetry;

/// <summary>
/// Tests <see cref="BackupTelemetry"/> through a process-wide activity listener limited to the trace of each test and
/// meters that belong to the test alone.
/// </summary>
public sealed class BackupTelemetryTests : IDisposable
{
    private readonly TelemetryCapture _capture = new();

    private BackupTelemetry Telemetry => _capture.Telemetry;

    /// <inheritdoc />
    public void Dispose() => _capture.Dispose();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StartRun_Listening_StartsRunSpanWithDryRunTag(bool dryRun)
    {
        using var test = _capture.StartTest();

        using (var run = Telemetry.StartRun(dryRun))
        {
            run.ShouldNotBeNull().OperationName.ShouldBe(BackupTelemetry.RunSpanName);
            run.Source.Name.ShouldBe(BackupTelemetry.SourceName);
        }

        _capture.Spans.ShouldHaveSingleItem().GetTagItem(BackupTelemetry.DryRunTag).ShouldBe(dryRun);
    }

    [Fact]
    public void StartRepository_Listening_IsChildOfCurrentSpanWithTags()
    {
        using var test = _capture.StartTest();

        using (Telemetry.StartRepository("owner/repo.wiki", BackupTarget.Wiki))
        {
        }

        var span = _capture.Named(BackupTelemetry.RepositorySpanName).ShouldHaveSingleItem();
        span.ParentSpanId.ShouldBe(test.SpanId);
        span.GetTagItem(BackupTelemetry.RepositoryTag).ShouldBe("owner/repo.wiki");
        span.GetTagItem(BackupTelemetry.TargetTag).ShouldBe(nameof(BackupTarget.Wiki));
    }

    [Fact]
    public void StartDiscovery_Listening_StartsDiscoverySpan()
    {
        using var test = _capture.StartTest();

        using (Telemetry.StartDiscovery())
        {
        }

        _capture.Spans.ShouldHaveSingleItem().OperationName.ShouldBe(BackupTelemetry.DiscoverySpanName);
    }

    [Fact]
    public async Task TraceOperationAsync_ActionSucceeds_ReturnsResultInsideOperationSpan()
    {
        using var test = _capture.StartTest();

        var result = await Telemetry.TraceOperationAsync(BackupTelemetry.FetchOperation, () => Task.FromResult(42));

        result.ShouldBe(42);
        var span = _capture.Spans.ShouldHaveSingleItem();
        span.OperationName.ShouldBe(BackupTelemetry.FetchOperation);
        span.GetTagItem(BackupTelemetry.OperationTag).ShouldBe(BackupTelemetry.FetchOperation);
        span.Status.ShouldBe(ActivityStatusCode.Unset);
    }

    [Fact]
    public async Task TraceOperationAsync_ActionThrows_MarksSpanFailedAndRethrows()
    {
        using var test = _capture.StartTest();

        await Should.ThrowAsync<IOException>(() => Telemetry.TraceOperationAsync<int>(
            BackupTelemetry.ArchiveOperation, () => throw new IOException("disk full at C:\\secret")));

        var span = _capture.Spans.ShouldHaveSingleItem();
        span.Status.ShouldBe(ActivityStatusCode.Error);
        span.StatusDescription.ShouldBeNull();
        span.GetTagItem(BackupTelemetry.ErrorTypeTag).ShouldBe(typeof(IOException).FullName);
    }

    [Theory]
    [InlineData(false, false, ActivityStatusCode.Error)]
    [InlineData(true, false, ActivityStatusCode.Unset)]
    [InlineData(true, true, ActivityStatusCode.Error)]
    public async Task TraceOperationAsync_RemoteMayBeMissing_MarksOnlyOtherFailuresAsError(
        bool remoteMayBeMissing, bool otherFailure, ActivityStatusCode expected)
    {
        using var test = _capture.StartTest();
        Exception failure = otherFailure
            ? new IOException("disk full")
            : new GitRepositoryNotFoundException("Repository not found.", "git clone", 128);

        await Should.ThrowAsync(
            Telemetry.TraceOperationAsync<int>(
                BackupTelemetry.CloneOperation, () => throw failure, remoteMayBeMissing),
            failure.GetType());

        _capture.Spans.ShouldHaveSingleItem().Status.ShouldBe(expected);
    }

    [Fact]
    public async Task TraceOperationAsync_ActivityOverload_HandsSpanToAction()
    {
        using var test = _capture.StartTest();

        await Telemetry.TraceOperationAsync(
            BackupTelemetry.CloneOperation,
            activity =>
            {
                activity.ShouldNotBeNull().SetTag("custom", 1);
                return Task.FromResult(0);
            });

        _capture.Spans.ShouldHaveSingleItem().GetTagItem("custom").ShouldBe(1);
    }

    [Fact]
    public async Task TraceRemoteRefsAsync_Succeeds_NamesSpanAndTagsQueryAndRefsCount()
    {
        using var test = _capture.StartTest();
        RefSnapshot refs = new([KeyValuePair.Create("heads/main", "a"), KeyValuePair.Create("tags/v1", "b")]);

        var result = await Telemetry.TraceRemoteRefsAsync(() => Task.FromResult(refs));

        result.ShouldBe(refs);
        var span = _capture.Spans.ShouldHaveSingleItem();
        span.OperationName.ShouldBe(BackupTelemetry.LsRemoteRefsSpanName);
        span.GetTagItem(BackupTelemetry.OperationTag).ShouldBe(BackupTelemetry.LsRemoteOperation);
        span.GetTagItem(BackupTelemetry.GitQueryTag).ShouldBe(BackupTelemetry.RemoteRefsQuery);
        span.GetTagItem(BackupTelemetry.GitRefsCountTag).ShouldBe(2);
    }

    [Theory]
    [InlineData(false, ActivityStatusCode.Error)]
    [InlineData(true, ActivityStatusCode.Unset)]
    public async Task TraceRemoteRefsAsync_NoRepository_SetsNoRefsCountAndMarksErrorOnlyWhenRemoteMustExist(
        bool remoteMayBeMissing, ActivityStatusCode expected)
    {
        using var test = _capture.StartTest();

        await Should.ThrowAsync<GitRepositoryNotFoundException>(() => Telemetry.TraceRemoteRefsAsync(
            () => throw new GitRepositoryNotFoundException("Repository not found.", "git ls-remote", 128),
            remoteMayBeMissing));

        var span = _capture.Spans.ShouldHaveSingleItem();
        span.Status.ShouldBe(expected);
        span.GetTagItem(BackupTelemetry.GitRefsCountTag).ShouldBeNull();
    }

    [Fact]
    public async Task TraceDefaultBranchAsync_Succeeds_NamesSpanAndTagsQueryOnly()
    {
        using var test = _capture.StartTest();

        var result = await Telemetry.TraceDefaultBranchAsync(() => Task.FromResult<string?>("main"));

        result.ShouldBe("main");
        var span = _capture.Spans.ShouldHaveSingleItem();
        span.OperationName.ShouldBe(BackupTelemetry.LsRemoteDefaultBranchSpanName);
        span.GetTagItem(BackupTelemetry.OperationTag).ShouldBe(BackupTelemetry.LsRemoteOperation);
        span.GetTagItem(BackupTelemetry.GitQueryTag).ShouldBe(BackupTelemetry.DefaultBranchQuery);
        span.TagObjects.Select(tag => tag.Value).ShouldNotContain("main");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SetSyncResult_Sync_TagsLfsAndSubmodulesByCheckout(bool hasBranch)
    {
        using var test = _capture.StartTest();

        using (var span = Telemetry.StartOperation(BackupTelemetry.FetchOperation))
        {
            BackupTelemetry.SetSyncResult(
                span, new GitSyncResult(hasBranch ? "main" : null, false, RefSnapshot.Empty));
        }

        var stopped = _capture.Spans.ShouldHaveSingleItem();
        stopped.GetTagItem(BackupTelemetry.GitLfsTag).ShouldBe(hasBranch);
        stopped.GetTagItem(BackupTelemetry.GitSubmodulesTag).ShouldBe(hasBranch);
    }

    [Fact]
    public void SetArchiveSize_Size_TagsBytes()
    {
        using var test = _capture.StartTest();

        using (var span = Telemetry.StartOperation(BackupTelemetry.ArchiveOperation))
        {
            BackupTelemetry.SetArchiveSize(span, 4096);
        }

        _capture.Spans.ShouldHaveSingleItem().GetTagItem(BackupTelemetry.ArchiveSizeTag).ShouldBe(4096L);
    }

    [Theory]
    [InlineData("main", true)]
    [InlineData(null, false)]
    public void SetDetection_Detection_TagsDecisionReasonAndKnownBranch(string? branch, bool branchTagged)
    {
        using var test = _capture.StartTest();

        using (var span = Telemetry.StartRepository("owner/repo", BackupTarget.Repository))
        {
            BackupTelemetry.SetDetection(
                span, new ChangeDetection(ChangeDecision.ArchiveNeeded, ChangeReason.RefsChanged, branch, false, RefSnapshot.Empty));
        }

        var stopped = _capture.Spans.ShouldHaveSingleItem();
        stopped.GetTagItem(BackupTelemetry.ChangeDecisionTag).ShouldBe(nameof(ChangeDecision.ArchiveNeeded));
        stopped.GetTagItem(BackupTelemetry.ChangeReasonTag).ShouldBe(nameof(ChangeReason.RefsChanged));
        (stopped.GetTagItem(BackupTelemetry.BranchTag) is not null).ShouldBe(branchTagged);
    }

    [Fact]
    public void SetHelpers_NoSpan_DoNothing() =>
        Should.NotThrow(() =>
        {
            BackupTelemetry.SetSyncResult(null, new GitSyncResult("main", false, RefSnapshot.Empty));
            BackupTelemetry.SetArchiveSize(null, 1);
            BackupTelemetry.SetDetection(
                null, new ChangeDetection(ChangeDecision.UpToDate, ChangeReason.Unchanged, "main", false, RefSnapshot.Empty));
        });

    [Fact]
    public void SetError_NoException_SetsErrorStatusWithoutType()
    {
        using var test = _capture.StartTest();

        using (var run = Telemetry.StartRun(dryRun: false))
        {
            BackupTelemetry.SetError(run, null);
        }

        var span = _capture.Spans.ShouldHaveSingleItem();
        span.Status.ShouldBe(ActivityStatusCode.Error);
        span.GetTagItem(BackupTelemetry.ErrorTypeTag).ShouldBeNull();
    }

    [Theory]
    [InlineData(BackupItemStatus.Archived, ActivityStatusCode.Unset)]
    [InlineData(BackupItemStatus.Skipped, ActivityStatusCode.Unset)]
    [InlineData(BackupItemStatus.Cancelled, ActivityStatusCode.Unset)]
    [InlineData(BackupItemStatus.Failed, ActivityStatusCode.Error)]
    public void SetStatus_Status_TagsStatusAndMarksOnlyFailedAsError(BackupItemStatus status, ActivityStatusCode expected)
    {
        using var test = _capture.StartTest();

        using (var repository = Telemetry.StartRepository("owner/repo", BackupTarget.Repository))
        {
            BackupTelemetry.SetStatus(repository, status, new InvalidOperationException("message"));
        }

        var span = _capture.Spans.ShouldHaveSingleItem();
        span.GetTagItem(BackupTelemetry.StatusTag).ShouldBe(status.ToString());
        span.Status.ShouldBe(expected);
    }

    [Fact]
    public void SetStatus_NoSpan_DoesNothing() =>
        Should.NotThrow(() => BackupTelemetry.SetStatus(null, BackupItemStatus.Failed, new InvalidOperationException()));

    [Fact]
    public void RecordItem_Item_CountsOneAndRecordsSecondsWithTags()
    {
        using var counter = _capture.Collect<long>(BackupTelemetry.RepositoriesMetric);
        using var durations = _capture.Collect<double>(BackupTelemetry.RepositoryDurationMetric);

        Telemetry.RecordItem(
            new("owner/repo.wiki", BackupItemStatus.UpToDate, null, TimeSpan.FromMilliseconds(1500), null) { Target = BackupTarget.Wiki },
            dryRun: true);

        var count = counter.GetMeasurementSnapshot().ShouldHaveSingleItem();
        count.Value.ShouldBe(1);
        count.Tags[BackupTelemetry.StatusTag].ShouldBe(nameof(BackupItemStatus.UpToDate));
        count.Tags[BackupTelemetry.TargetTag].ShouldBe(nameof(BackupTarget.Wiki));
        count.Tags[BackupTelemetry.DryRunTag].ShouldBe(true);
        var duration = durations.GetMeasurementSnapshot().ShouldHaveSingleItem();
        duration.Value.ShouldBe(1.5);
        duration.Tags.ShouldBe(count.Tags, ignoreOrder: true);
    }

    [Fact]
    public void RecordRun_Outcome_RecordsSecondsWithOutcomeAndMode()
    {
        using var runs = _capture.Collect<double>(BackupTelemetry.RunDurationMetric);

        Telemetry.RecordRun(BackupTelemetry.CancelledOutcome, dryRun: false, TimeSpan.FromSeconds(90));

        var run = runs.GetMeasurementSnapshot().ShouldHaveSingleItem();
        run.Value.ShouldBe(90);
        run.Tags[BackupTelemetry.RunOutcomeTag].ShouldBe(BackupTelemetry.CancelledOutcome);
        run.Tags[BackupTelemetry.DryRunTag].ShouldBe(false);
    }

    [Fact]
    public void RecordArchiveSize_Size_RecordsBytesWithTarget()
    {
        using var sizes = _capture.Collect<long>(BackupTelemetry.ArchiveSizeMetric);

        Telemetry.RecordArchiveSize(4096, BackupTarget.Repository);

        var size = sizes.GetMeasurementSnapshot().ShouldHaveSingleItem();
        size.Value.ShouldBe(4096);
        size.Tags[BackupTelemetry.TargetTag].ShouldBe(nameof(BackupTarget.Repository));
    }
}
