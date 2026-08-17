using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace DanbooruTagGen.App.Converters;

/// <summary>17개 카테고리 버튼 중 지금 활성인 것만 배경색을 바꾼다. values[0]=이 버튼 자신의
/// Keyword(아이템 DataContext), values[1]=ViewModel.ActiveCategoryKeyword. 검색어 입력 중이면
/// (ActiveCategoryKeyword가 null) 어떤 버튼도 활성으로 안 보인다.</summary>
public sealed class CategoryActiveToBrushConverter : IMultiValueConverter
{
    private static readonly Brush Active = new SolidColorBrush(Color.FromRgb(0x42, 0x85, 0xF4));
    private static readonly Brush Inactive = SystemColors.ControlBrush;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var own = values.Length > 0 ? values[0] as string : null;
        var active = values.Length > 1 ? values[1] as string : null;
        return active != null && own == active ? Active : Inactive;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
