using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using GitHubBackup.App.ViewModels;
using GitHubBackup.Core.Ui;

namespace GitHubBackup.App.Views;

/// <summary>
/// Shows the tree of accounts and repositories, the commands that start a backup, the live log of a run and its results, each in its own card.
/// </summary>
public sealed partial class RepositoriesView : UserControl
{
    /// <summary>The smallest change, in device-independent pixels, of the results row limit that is applied.</summary>
    private const double _layoutTolerance = 0.5;

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
    /// Resizes the results panel by the vertical movement of the splitter above it; the tree gives or takes the
    /// difference and keeps its minimum height.
    /// </summary>
    /// <remarks>
    /// The new height goes to the row, whose two-way binding hands it to the preferences that are saved when the
    /// window closes. The totals card below the results panel keeps its content height and is not resized.
    /// </remarks>
    /// <param name="sender">The splitter.</param>
    /// <param name="e">The vertical movement since the last call.</param>
    private void OnLogResultsSplitterDragDelta(object sender, DragDeltaEventArgs e)
    {
        var current = ResultsRow.ActualHeight;
        var smallest = Math.Max(ResultsRow.MinHeight, ResultsContent.MinHeight + VerticalChrome(ResultsCard));
        var largest = Math.Min(ResultsRow.MaxHeight, current + Math.Max(0, TreeRow.ActualHeight - TreeRow.MinHeight));
        var height = Math.Clamp(current - e.VerticalChange, Math.Min(smallest, current), Math.Max(smallest, largest));
        ResultsRow.Height = new GridLength(height);
    }

    /// <summary>
    /// Keeps the minimum height of the results content equal to its fixed lines plus the smallest table, and grows
    /// the results panel to that height when the tree can give the space.
    /// </summary>
    /// <remarks>
    /// A saved panel height can be lower than the content of the next results panel; the panel then grows instead of
    /// cutting the table or the lines below it, and scrolls only when the window has no room left.
    /// </remarks>
    /// <param name="sender">A part of the results panel with its natural height.</param>
    /// <param name="e">The change of its size.</param>
    private void OnResultsPartSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var rows = ResultsContent.RowDefinitions;
        var fixedLines = rows.Where(row => row != ResultsTableRow).Sum(row => row.ActualHeight);
        ResultsContent.MinHeight = fixedLines + ResultsTableRow.MinHeight;

        var needed = ResultsContent.MinHeight + VerticalChrome(ResultsCard);
        var current = ResultsRow.ActualHeight;
        if (current >= needed)
        {
            return;
        }

        var height = Math.Min(Math.Min(needed, Math.Max(current, AvailableResultsHeight())), UiSettings.MaxSummaryPanelHeight);
        if (height > current)
        {
            ResultsRow.Height = new GridLength(height);
        }
    }

    /// <summary>
    /// Limits the results panel to the room that the window leaves it, so that the totals card below it is never cut.
    /// </summary>
    /// <remarks>
    /// The limit is the maximum height of the row, not its height: the saved panel height stays as the user set it
    /// and comes back when the window grows. The panel gives way down to its smallest height, the fixed lines and
    /// the smallest table, and its table scrolls inside. The limit depends on every row but the results row, so
    /// setting it causes one more layout pass and no loop.
    /// </remarks>
    /// <param name="sender">The grid of the panels.</param>
    /// <param name="e">No data.</param>
    private void OnPanelsLayoutUpdated(object? sender, EventArgs e)
    {
        if (!ResultsCard.IsVisible)
        {
            return;
        }

        var smallest = Math.Max(ResultsRow.MinHeight, ResultsContent.MinHeight + VerticalChrome(ResultsCard));
        var limit = Math.Min(UiSettings.MaxSummaryPanelHeight, Math.Max(smallest, AvailableResultsHeight()));
        if (Math.Abs(ResultsRow.MaxHeight - limit) > _layoutTolerance)
        {
            ResultsRow.MaxHeight = limit;
        }
    }

    /// <summary>
    /// Returns the height that the results row can take while the tree keeps its minimum height and every other row,
    /// the totals card included, keeps its own.
    /// </summary>
    /// <remarks>
    /// The arranged slot of the grid is the room the window gives it; the grid itself reports a larger height when
    /// its rows do not fit.
    /// </remarks>
    /// <returns>The height in device-independent pixels, never negative.</returns>
    private double AvailableResultsHeight()
    {
        var room = LayoutInformation.GetLayoutSlot(PanelsGrid).Height - PanelsGrid.Margin.Top - PanelsGrid.Margin.Bottom;
        var taken = PanelsGrid.RowDefinitions
            .Where(row => row != TreeRow && row != ResultsRow)
            .Sum(row => row.ActualHeight);
        return Math.Max(0, room - taken - TreeRow.MinHeight);
    }

    /// <summary>
    /// Returns the height that the border and the padding of a card add to its content.
    /// </summary>
    /// <param name="card">The card.</param>
    /// <returns>The height in device-independent pixels.</returns>
    private static double VerticalChrome(Border card) =>
        card.BorderThickness.Top + card.BorderThickness.Bottom + card.Padding.Top + card.Padding.Bottom;

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
