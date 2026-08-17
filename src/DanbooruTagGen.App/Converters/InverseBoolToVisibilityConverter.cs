using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DanbooruTagGen.App.Converters;

/// <summary>bool이 false일 때 Visible, true면 Collapsed — 표준 BooleanToVisibilityConverter의
/// 반대. 슬롯 라벨 인라인 편집: 편집 중(true)엔 TextBlock을 숨기고 TextBox를 보여줘야 하는데,
/// 그 반대쪽(TextBlock)에 쓴다.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value is bool b && b) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
