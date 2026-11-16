using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace GitHubBackup.App.Behaviors;

/// <summary>
/// Scrolls an <see cref="ItemsControl"/> to its end when an item is added at the end of its items, so a row the user
/// has just added is visible.
/// </summary>
/// <remarks>
/// A reset of the items, such as a new filter or a new source, does not scroll; neither does an item inserted before
/// the end.
/// </remarks>
public static class ScrollToAddedItem
{
    /// <summary>Identifies the attached property that turns the behavior on.</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(ScrollToAddedItem),
        new(false, OnIsEnabledChanged));

    /// <summary>Identifies the attached property that holds the handler added to the items of a control.</summary>
    private static readonly DependencyProperty _handlerProperty = DependencyProperty.RegisterAttached(
        "Handler", typeof(ItemsHandler), typeof(ScrollToAddedItem), new(null));

    /// <summary>
    /// Returns whether the behavior is on.
    /// </summary>
    /// <param name="element">The items control.</param>
    /// <returns><see langword="true"/> when the behavior is on.</returns>
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    /// <summary>
    /// Turns the behavior on or off.
    /// </summary>
    /// <param name="element">The items control.</param>
    /// <param name="value">Whether the behavior is on.</param>
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    /// <summary>
    /// Starts or stops following the items of the control.
    /// </summary>
    /// <param name="element">The items control.</param>
    /// <param name="e">The old and the new value.</param>
    private static void OnIsEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not ItemsControl control)
        {
            return;
        }

        INotifyCollectionChanged items = control.Items;
        if (control.GetValue(_handlerProperty) is ItemsHandler existing)
        {
            items.CollectionChanged -= existing.OnItemsChanged;
            control.ClearValue(_handlerProperty);
        }

        if ((bool)e.NewValue)
        {
            var handler = new ItemsHandler(control);
            items.CollectionChanged += handler.OnItemsChanged;
            control.SetValue(_handlerProperty, handler);
        }
    }

    /// <summary>
    /// Returns the first <see cref="ScrollViewer"/> in the visual tree below <paramref name="element"/>.
    /// </summary>
    /// <param name="element">The element whose descendants are searched.</param>
    /// <returns>The scroll viewer, or <see langword="null"/>.</returns>
    private static ScrollViewer? FindScrollViewer(DependencyObject element)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
        {
            var child = VisualTreeHelper.GetChild(element, index);
            if ((child as ScrollViewer ?? FindScrollViewer(child)) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Scrolls one items control when an item is added at the end of its items.
    /// </summary>
    /// <param name="control">The items control.</param>
    private sealed class ItemsHandler(ItemsControl control)
    {
        /// <summary>
        /// Scrolls to the end after the layout has created the container of an item added at the end.
        /// </summary>
        /// <param name="sender">The items of the control.</param>
        /// <param name="e">What changed.</param>
        public void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add
                || e.NewItems is not { Count: > 0 } added
                || e.NewStartingIndex != control.Items.Count - added.Count)
            {
                return;
            }

            control.Dispatcher.BeginInvoke(DispatcherPriority.Background, () => FindScrollViewer(control)?.ScrollToEnd());
        }
    }
}
