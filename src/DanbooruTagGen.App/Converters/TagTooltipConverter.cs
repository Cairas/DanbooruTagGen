using System;
using System.Globalization;
using System.Text;
using System.Windows.Data;
using DanbooruTagGen.Core.Generation;

namespace DanbooruTagGen.App.Converters;

/// <summary>태그 이름(string)과 ITagLookup을 함께 받아 호버 툴팁용 설명 텍스트를 만든다.
/// 레시피 빌더의 태그 칩은 순수 문자열(ObservableCollection&lt;string&gt;)이라 Description 같은
/// 속성이 없으므로, Pool 라이브러리처럼 여기서 조회해 채운다.</summary>
public sealed class TagTooltipConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var name = values.Length > 0 ? values[0] as string : null;
        if (string.IsNullOrEmpty(name)) return null;
        var lookup = values.Length > 1 ? values[1] as ITagLookup : null;
        var tag = lookup?.Lookup(name);

        var sb = new StringBuilder(name.Replace('_', ' '));
        if (tag is null) return sb.ToString();

        if (!string.IsNullOrWhiteSpace(tag.Description)) sb.Append('\n').Append(tag.Description);
        sb.Append('\n').Append("카테고리: ").Append(tag.Category);
        sb.Append('\n').Append("빈도: ").Append(tag.PostCount.ToString("N0"));
        if (tag.Aliases.Count > 0) sb.Append('\n').Append(string.Join(", ", tag.Aliases));
        return sb.ToString();
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
