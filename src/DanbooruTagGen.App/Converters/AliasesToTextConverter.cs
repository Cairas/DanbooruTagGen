using System.Collections;
using System.Globalization;
using System.Windows.Data;

namespace DanbooruTagGen.App.Converters;

/// <summary>별칭 리스트 -> "별칭: a, b, c" (없으면 빈 문자열).</summary>
public sealed class AliasesToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is IEnumerable e and not string)
        {
            var items = e.Cast<object?>().Select(x => x?.ToString()).Where(s => !string.IsNullOrEmpty(s));
            var joined = string.Join(", ", items);
            return joined.Length > 0 ? "별칭: " + joined : "";
        }
        return "";
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}
