using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace YsfUtil.Ui.Views;

/// <summary>Collapsed for null or empty text, visible otherwise.</summary>
internal sealed class CollapsedWhenNull : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value == null || value is string { Length: 0 } ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
