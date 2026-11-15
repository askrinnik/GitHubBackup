using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using GitHubBackup.App.ViewModels;

namespace GitHubBackup.App.Views;

/// <summary>
/// Shows the tree of accounts and repositories, the commands that start a backup and the live log of a run.
/// </summary>
public sealed partial class RepositoriesView : UserControl
{
    /// <summary>Whether a scroll of the live log to its newest entry is queued and has not run yet.</summary>
    private bool _scrollToNewestQueued;

    /// <summary>The scroll viewer of the live log's template, found on the first scroll.</summary>
    private ScrollViewer? _runLogScrollViewer;

    /// <summary>
    /// Initializes the view and loads its XAML-defined content.
    /// </summary>
    public RepositoriesView()
    {
        InitializeComponent();
        ((INotifyCollectionChanged)RunLogList.Items).CollectionChanged += OnRunLogChanged;
    }

    /// <summary>
    /// Reads the tree when the view first appears; the ViewModel reads nothing while the container creates it.
    /// </summary>
    /// <param name="sender">The view.</param>
    /// <param name="e">No data.</param>
    private void OnLoaded(object sender, RoutedEventArgs e) => (DataContext as RepositoriesViewModel)?.RefreshCommand.Execute(null);

    /// <summary>
    /// Resizes the summary panel by the vertical movement of the splitter above it; the tree gives or takes the
    /// difference and keeps its minimum height.
    /// </summary>
    /// <remarks>
    /// The new height goes to the row, whose two-way binding hands it to the preferences that are saved when the
    /// window closes.
    /// </remarks>
    /// <param name="sender">The splitter.</param>
    /// <param name="e">The vertical movement since the last call.</param>
    private void OnLogSummarySplitterDragDelta(object sender, DragDeltaEventArgs e)
    {
        var current = SummaryRow.ActualHeight;
        var largest = Math.Min(SummaryRow.MaxHeight, current + Math.Max(0, TreeRow.ActualHeight - TreeRow.MinHeight));
        var height = Math.Clamp(current - e.VerticalChange, SummaryRow.MinHeight, Math.Max(SummaryRow.MinHeight, largest));
        SummaryRow.Height = new GridLength(height);
    }

    /// <summary>
    /// Queues one scroll of the live log to its newest entry when entries are added.
    /// </summary>
    /// <remarks>
    /// A batch adds many entries at once; the scroll runs once, after the batch, at background priority, so it does
    /// not compete with the layout of each entry.
    /// </remarks>
    /// <param name="sender">The items of the live log.</param>
    /// <param name="e">The change.</param>
    private void OnRunLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || _scrollToNewestQueued)
        {
            return;
        }

        _scrollToNewestQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, ScrollToNewest);
    }

    /// <summary>
    /// Scrolls the live log to its newest entry, vertically only.
    /// </summary>
    /// <remarks>
    /// <see cref="ListBox.ScrollIntoView(object)"/> would also scroll horizontally to the end of a long entry and hide
    /// the start of every line; scrolling the viewer to its bottom keeps the horizontal position.
    /// </remarks>
    private void ScrollToNewest()
    {
        _scrollToNewestQueued = false;
        if (_runLogScrollViewer is null || !_runLogScrollViewer.IsDescendantOf(RunLogList))
        {
            _runLogScrollViewer = FindDescendant<ScrollViewer>(RunLogList);
        }

        _runLogScrollViewer?.ScrollToBottom();
    }

    /// <summary>
    /// Returns the first descendant of <paramref name="root"/> of type <typeparamref name="T"/>, searching the visual
    /// tree breadth first.
    /// </summary>
    /// <typeparam name="T">The type of the element to find.</typeparam>
    /// <param name="root">The element whose visual tree is searched.</param>
    /// <returns>The element, or <see langword="null"/> when the template is not applied yet or holds none.</returns>
    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        var pending = new Queue<DependencyObject>([root]);
        while (pending.TryDequeue(out var current))
        {
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(current); index++)
            {
                var child = VisualTreeHelper.GetChild(current, index);
                if (child is T match)
                {
                    return match;
                }

                pending.Enqueue(child);
            }
        }

        return null;
    }
}
