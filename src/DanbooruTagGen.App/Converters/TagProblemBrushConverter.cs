using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using DanbooruTagGen.Core.Generation;

namespace DanbooruTagGen.App.Converters;

/// <summary>태그 칩 글자색: 문제(사전에 없는 태그·괄호/가중치 문법 오류)가 있으면 빨강, 없으면 원래 색.
/// values[0] = 후보 문자열, values[1] = ITagLookup. 전체 검사를 누르지 않아도 넣는 순간 보이게 하려는 것
/// (판정은 RecipeValidator.QuickCheck — 전체 검사와 같은 기준).</summary>
public sealed class TagProblemBrushConverter : IMultiValueConverter
{
    private static readonly Brush Problem = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));

    static TagProblemBrushConverter() => Problem.Freeze();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var raw = values.Length > 0 ? values[0] as string : null;
        var lookup = values.Length > 1 ? values[1] as ITagLookup : null;
        return raw != null && RecipeValidator.QuickCheck(raw, lookup, CurrentRules()) != null ? Problem : DependencyProperty.UnsetValue;
    }

    /// <summary>모순 규칙(implies: 표식 판정용). 메인 창의 뷰모델에서 가져온다 — 컨버터는 리소스라 생성자 주입이 안 된다.</summary>
    internal static ConflictRules? CurrentRules() =>
        (Application.Current?.MainWindow?.DataContext as ViewModels.MainViewModel)?.Conflicts;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
