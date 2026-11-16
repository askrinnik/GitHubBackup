using System.Collections.ObjectModel;
using System.Collections.Specialized;
using GitHubBackup.App.ViewModels;

namespace GitHubBackup.App.Tests.ViewModels;

/// <summary>
/// Tests <see cref="FilteredItems{T}"/>: matching by part of the key, following the source and the no-match state.
/// </summary>
public sealed class FilteredItemsTests
{
    private readonly ObservableCollection<Item> _source = [new("alpha"), new("Beta"), new("gamma"), new("alphabet")];
    private readonly FilteredItems<Item> _view;

    /// <summary>
    /// Creates a view over four items keyed by their name.
    /// </summary>
    public FilteredItemsTests() => _view = new(_source, item => item.Name);

    [Fact]
    public void Constructor_EmptyFilter_ShowsEveryItemInSourceOrder()
    {
        Names().ShouldBe(["alpha", "Beta", "gamma", "alphabet"]);
        _view.HasNoMatch.ShouldBeFalse();
    }

    [Theory]
    [InlineData("ALPHA")]
    [InlineData("lph")]
    [InlineData("  lph  ")]
    public void Filter_PartOfNameIgnoringCaseAndSurroundingSpace_ShowsMatchingItems(string filter)
    {
        _view.Filter = filter;

        Names().ShouldBe(["alpha", "alphabet"]);
    }

    [Fact]
    public void Filter_OnlyWhiteSpace_ShowsEveryItem()
    {
        _view.Filter = "   ";

        Names().ShouldBe(["alpha", "Beta", "gamma", "alphabet"]);
    }

    [Fact]
    public void Filter_NoMatch_ShowsNothingAndReportsNoMatch()
    {
        var notified = new List<string?>();
        _view.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        _view.Filter = "zzz";

        _view.Items.ShouldBeEmpty();
        _view.HasNoMatch.ShouldBeTrue();
        notified.ShouldContain(nameof(FilteredItems<Item>.HasNoMatch));
    }

    [Fact]
    public void Filter_Cleared_ShowsEveryItemAgain()
    {
        _view.Filter = "zzz";

        _view.Filter = string.Empty;

        Names().ShouldBe(["alpha", "Beta", "gamma", "alphabet"]);
        _view.HasNoMatch.ShouldBeFalse();
    }

    [Fact]
    public void Filter_Changed_RaisesOneResetNotification()
    {
        var actions = new List<NotifyCollectionChangedAction>();
        _view.Items.CollectionChanged += (_, e) => actions.Add(e.Action);

        _view.Filter = "a";

        actions.ShouldBe([NotifyCollectionChangedAction.Reset]);
    }

    [Fact]
    public void SourceAdd_MatchingItem_AppendsIt()
    {
        _view.Filter = "alp";

        _source.Add(new("alpine"));

        Names().ShouldBe(["alpha", "alphabet", "alpine"]);
    }

    [Fact]
    public void SourceAdd_ItemNotMatching_KeepsItHidden()
    {
        _view.Filter = "alp";

        _source.Add(new("delta"));

        Names().ShouldBe(["alpha", "alphabet"]);
    }

    [Fact]
    public void SourceRemove_RemovesVisibleItem()
    {
        _source.RemoveAt(1);

        Names().ShouldBe(["alpha", "gamma", "alphabet"]);
    }

    [Fact]
    public void SourceRemove_LastMatch_ReportsNoMatch()
    {
        _view.Filter = "gam";

        _source.RemoveAt(2);

        _view.Items.ShouldBeEmpty();
        _view.HasNoMatch.ShouldBeTrue();
    }

    [Fact]
    public void Filter_EmptySource_ReportsNoMatchFalse()
    {
        var view = new FilteredItems<Item>([], item => item.Name) { Filter = "a" };

        view.Items.ShouldBeEmpty();
        view.HasNoMatch.ShouldBeFalse();
    }

    [Fact]
    public void SourceInsertBeforeEnd_KeepsSourceOrder()
    {
        _source.Insert(0, new("alpaca"));

        Names().ShouldBe(["alpaca", "alpha", "Beta", "gamma", "alphabet"]);
    }

    [Fact]
    public void DeferRefresh_SourceChangedInScope_ShowsItemsWithOneResetAfterScope()
    {
        var actions = new List<NotifyCollectionChangedAction>();
        _view.Items.CollectionChanged += (_, e) => actions.Add(e.Action);

        using (_view.DeferRefresh())
        {
            _source.Clear();
            _source.Add(new("one"));
            _source.Add(new("two"));
            actions.ShouldBeEmpty();
        }

        Names().ShouldBe(["one", "two"]);
        actions.ShouldBe([NotifyCollectionChangedAction.Reset]);
    }

    /// <summary>
    /// Returns the names of the visible items.
    /// </summary>
    /// <returns>The names, in order.</returns>
    private string[] Names() => [.. _view.Items.Select(item => item.Name)];

    /// <summary>
    /// An item keyed by its name.
    /// </summary>
    /// <param name="Name">The name.</param>
    private sealed record Item(string Name);
}
