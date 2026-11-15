using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GitHubBackup.App.Converters;

/// <summary>
/// Converts a height in device-independent units to a fixed <see cref="GridLength"/> of a grid row and back, so that a
/// row resized by a <see cref="System.Windows.Controls.GridSplitter"/> writes its new height to the bound property.
/// </summary>
public sealed class PixelGridLengthConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double height ? new GridLength(height) : DependencyProperty.UnsetValue;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is GridLength { IsAbsolute: true } length ? length.Value : Binding.DoNothing;
}
