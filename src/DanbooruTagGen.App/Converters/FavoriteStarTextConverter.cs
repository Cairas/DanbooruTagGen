using System;
using System.Globalization;
using System.Windows.Data;

namespace DanbooruTagGen.App.Converters;

/// <summary>즐겨찾기 토글 버튼 라벨: 즐겨찾기 상태면 채워진 별(해제 유도), 아니면 빈 별(추가 유도).</summary>
public sealed class FavoriteStarTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value is true) ? "★ 즐겨찾기 해제" : "☆ 즐겨찾기";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
