using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DropShelf.Ui;

/// <summary>
/// Turns a count into <see cref="Visibility.Visible"/> when it is above zero.
/// </summary>
/// <remarks>
/// Used for controls that only make sense once the shelf is holding something.
/// A DataTrigger could do the same job inline, but it would have to be repeated
/// on every control that needs it, and each copy is a chance to get the sense of
/// the test backwards.
/// </remarks>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("Visibility never flows back into a count.");
}
