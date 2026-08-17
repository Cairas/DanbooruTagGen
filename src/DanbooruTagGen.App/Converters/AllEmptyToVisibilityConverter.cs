using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DanbooruTagGen.App.Converters;

/// <summary>랜덤 슬롯 안내 문구용: 후보 태그 개수(int)와 PoolId(string)를 함께 받아
/// 둘 다 비어 있을 때만(직접 태그도 없고 풀도 지정 안 됨) Visible.</summary>
public sealed class AllEmptyToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var tagCount = values.Length > 0 && values[0] is int n ? n : 0;
        var poolId = values.Length > 1 ? values[1] as string : null;
        return tagCount == 0 && string.IsNullOrEmpty(poolId) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
