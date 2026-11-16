using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GitHubBackup.App.ViewModels;

/// <summary>
/// Shows the items of a collection whose key contains a filter text, in the order of the collection.
/// </summary>
/// <remarks>
/// The filter ignores case and the white space around it; an empty filter shows every item. An item added to the end
/// of the collection is appended when it matches, and a removed item disappears; editing an item does not filter the
/// list again, so a row the user is typing in stays visible. A change of the filter replaces the visible items with
/// one reset notification, so a list does not treat them as added.
/// </remarks>
/// <typeparam name="T">The type of the items.</typeparam>
public sealed partial class FilteredItems<T> : ObservableObject
    where T : class
{
    /// <summary>The collection whose items are filtered.</summary>
    private readonly ObservableCollection<T> _source;

    /// <summary>Returns the text of an item that the filter searches.</summary>
    private readonly Func<T, string> _key;

    /// <summary>The visible items.</summary>
    private readonly ResettableCollection _items = [];

    /// <summary>The number of open <see cref="DeferRefresh"/> scopes; the source is not followed while it is positive.</summary>
    private int _deferDepth;

    /// <summary>The text that the key of a visible item contains.</summary>
    [ObservableProperty]
    private string _filter = string.Empty;

    /// <summary>
    /// Initializes the view with every item of <paramref name="source"/> and follows its changes.
    /// </summary>
    /// <param name="source">The collection whose items are filtered.</param>
    /// <param name="key">Returns the text of an item that the filter searches.</param>
    public FilteredItems(ObservableCollection<T> source, Func<T, string> key)
    {
        _source = source;
        _key = key;
        _source.CollectionChanged += OnSourceChanged;
        Rebuild();
    }

    /// <summary>Gets the visible items, in the order of the source.</summary>
    public ObservableCollection<T> Items => _items;

    /// <summary>Gets whether the source has items but none of them matches the filter.</summary>
    public bool HasNoMatch => _items.Count == 0 && _source.Count > 0;

    /// <summary>
    /// Stops following the source until the returned scope is disposed, then shows the matching items at once.
    /// </summary>
    /// <returns>The scope.</returns>
    public IDisposable DeferRefresh()
    {
        _deferDepth++;
        return new DeferScope(this);
    }

    /// <summary>
    /// Returns whether an item matches the filter.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns><see langword="true"/> when the filter is empty or the key of the item contains it.</returns>
    private bool Matches(T item)
    {
        var filter = Filter.Trim();
        return filter.Length == 0 || _key(item).Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Replaces the visible items with the matching items of the source.
    /// </summary>
    private void Rebuild()
    {
        _items.ResetTo(_source.Where(Matches));
        OnPropertyChanged(nameof(HasNoMatch));
    }

    /// <summary>
    /// Follows a change of the source.
    /// </summary>
    /// <param name="sender">The source.</param>
    /// <param name="e">What changed.</param>
    private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_deferDepth > 0)
        {
            return;
        }

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add
                when e.NewItems is { } added && e.NewStartingIndex == _source.Count - added.Count:
                foreach (var item in added.Cast<T>().Where(Matches))
                {
                    _items.Add(item);
                }

                break;
            case NotifyCollectionChangedAction.Remove when e.OldItems is { } removed:
                foreach (var item in removed.Cast<T>())
                {
                    _items.Remove(item);
                }

                break;
            default:
                Rebuild();
                return;
        }

        OnPropertyChanged(nameof(HasNoMatch));
    }

    /// <summary>Shows the items that match the new filter.</summary>
    /// <param name="value">The new filter.</param>
    partial void OnFilterChanged(string value) => Rebuild();

    /// <summary>
    /// An observable collection whose content can be replaced with one reset notification.
    /// </summary>
    private sealed class ResettableCollection : ObservableCollection<T>
    {
        /// <summary>
        /// Replaces the content with <paramref name="items"/> and raises one reset notification.
        /// </summary>
        /// <param name="items">The new content.</param>
        public void ResetTo(IEnumerable<T> items)
        {
            CheckReentrancy();
            Items.Clear();
            foreach (var item in items)
            {
                Items.Add(item);
            }

            OnPropertyChanged(new(nameof(Count)));
            OnPropertyChanged(new("Item[]"));
            OnCollectionChanged(new(NotifyCollectionChangedAction.Reset));
        }
    }

    /// <summary>
    /// Ends a <see cref="DeferRefresh"/> scope once.
    /// </summary>
    /// <param name="owner">The view whose refresh is deferred.</param>
    private sealed class DeferScope(FilteredItems<T> owner) : IDisposable
    {
        /// <summary>Whether the scope has ended.</summary>
        private bool _disposed;

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (--owner._deferDepth == 0)
            {
                owner.Rebuild();
            }
        }
    }
}
