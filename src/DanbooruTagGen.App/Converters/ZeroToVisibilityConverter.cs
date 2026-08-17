using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DanbooruTagGen.App.Converters;

/// <summary>정수 값이 0이면 Visible, 그 외에는 Collapsed.
/// (랜덤 슬롯에 후보가 0개일 때만 안내 문구를 보여주는 용도.)</summary>
public sealed class ZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value is int n && n == 0) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
