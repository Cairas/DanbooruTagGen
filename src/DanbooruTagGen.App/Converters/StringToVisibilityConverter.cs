using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DanbooruTagGen.App.Converters;

/// <summary>비어있지 않은 문자열이면 Visible, 비어있으면 Collapsed.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}
