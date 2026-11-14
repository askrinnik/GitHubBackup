using System.Windows;
using System.Windows.Controls;

namespace GitHubBackup.App.Behaviors;

/// <summary>
/// Adds a bindable <c>Password</c> to <see cref="PasswordBox"/>, whose own <see cref="PasswordBox.Password"/> is not a
/// dependency property.
/// </summary>
/// <remarks>
/// The text follows the box in both directions: typing updates the bound value, and a value cleared by the ViewModel
/// clears the box.
/// </remarks>
public static class PasswordBoxBinding
{
    /// <summary>Identifies the attached property that holds the text of the box.</summary>
    /// <remarks>
    /// The default is <see langword="null"/> so that the first bound value, even an empty text, counts as a change and
    /// the box starts being followed.
    /// </remarks>
    public static readonly DependencyProperty PasswordProperty = DependencyProperty.RegisterAttached(
        "Password",
        typeof(string),
        typeof(PasswordBoxBinding),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPasswordChanged));

    /// <summary>Identifies the attached property that marks a box whose changes are being copied, to stop the echo.</summary>
    private static readonly DependencyProperty _isUpdatingProperty = DependencyProperty.RegisterAttached(
        "IsUpdating", typeof(bool), typeof(PasswordBoxBinding), new(false));

    /// <summary>Identifies the attached property that marks a box whose <see cref="PasswordBox.PasswordChanged"/> is handled.</summary>
    private static readonly DependencyProperty _isAttachedProperty = DependencyProperty.RegisterAttached(
        "IsAttached", typeof(bool), typeof(PasswordBoxBinding), new(false));

    /// <summary>
    /// Returns the text of the box.
    /// </summary>
    /// <param name="element">The box.</param>
    /// <returns>The text.</returns>
    public static string GetPassword(DependencyObject element) => element.GetValue(PasswordProperty) as string ?? string.Empty;

    /// <summary>
    /// Sets the text of the box.
    /// </summary>
    /// <param name="element">The box.</param>
    /// <param name="value">The text.</param>
    public static void SetPassword(DependencyObject element, string value) => element.SetValue(PasswordProperty, value);

    /// <summary>
    /// Copies a new bound value into the box and starts following the box on first use.
    /// </summary>
    /// <param name="element">The box.</param>
    /// <param name="e">The old and the new value.</param>
    private static void OnPasswordChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not PasswordBox box)
        {
            return;
        }

        if (!(bool)box.GetValue(_isAttachedProperty))
        {
            box.SetValue(_isAttachedProperty, true);
            box.PasswordChanged += OnBoxPasswordChanged;
        }

        if (!(bool)box.GetValue(_isUpdatingProperty))
        {
            var value = e.NewValue as string ?? string.Empty;
            if (box.Password != value)
            {
                box.Password = value;
            }
        }
    }

    /// <summary>
    /// Copies the text the user typed to the bound value.
    /// </summary>
    /// <param name="sender">The box.</param>
    /// <param name="e">No data.</param>
    private static void OnBoxPasswordChanged(object sender, RoutedEventArgs e)
    {
        var box = (PasswordBox)sender;
        box.SetValue(_isUpdatingProperty, true);
        try
        {
            SetPassword(box, box.Password);
        }
        finally
        {
            box.SetValue(_isUpdatingProperty, false);
        }
    }
}
