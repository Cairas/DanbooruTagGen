using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using DanbooruTagGen.App.ViewModels;

namespace DanbooruTagGen.App.Converters;

/// <summary>일반/성인 필터 버튼 3개 중 지금 활성인 것만 배경색을 바꾼다. 예전엔 활성 상태가
/// 작은 회색 텍스트 하나로만 표시돼(NsfwFilter 바인딩), 버튼 자체는 눌러도 안 눌린 것처럼
/// 보였다. ConverterParameter로 각 버튼이 자기 모드("all"/"sfw"/"nsfw")를 알려준다.</summary>
public sealed class NsfwFilterToBrushConverter : IValueConverter
{
    private static readonly Brush Active = new SolidColorBrush(Color.FromRgb(0x42, 0x85, 0xF4));
    private static readonly Brush Inactive = SystemColors.ControlBrush;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not NsfwFilterMode mode || parameter is not string target) return Inactive;
        var match = target switch
        {
            "all" => mode == NsfwFilterMode.All,
            "sfw" => mode == NsfwFilterMode.SfwOnly,
            "nsfw" => mode == NsfwFilterMode.NsfwOnly,
            _ => false,
        };
        return match ? Active : Inactive;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
