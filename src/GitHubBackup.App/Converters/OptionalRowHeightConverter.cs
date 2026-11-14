using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GitHubBackup.App.Converters;

/// <summary>
/// Converts a height and whether the row is shown to the <see cref="GridLength"/> of a grid row that takes no space
/// while it is hidden, and writes a height set by a <see cref="System.Windows.Controls.GridSplitter"/> back to the
/// height only.
/// </summary>
/// <remarks>The first binding is the height in device-independent units, the second whether the row is shown.</remarks>
public sealed class OptionalRowHeightConverter : IMultiValueConverter
{
    /// <inheritdoc />
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values is [double height, bool isShown]
            ? new GridLength(isShown ? height : 0)
            : DependencyProperty.UnsetValue;

    /// <inheritdoc />
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        value is GridLength { IsAbsolute: true, Value: > 0 } length
            ? [length.Value, Binding.DoNothing]
            : [Binding.DoNothing, Binding.DoNothing];
}
