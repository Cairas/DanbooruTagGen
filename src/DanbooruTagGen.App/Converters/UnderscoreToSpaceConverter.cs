using System.Globalization;
using System.Windows.Data;

namespace DanbooruTagGen.App.Converters;

/// <summary>표시용: '_'를 공백으로(long_hair -> long hair). 저장값은 변경하지 않음.</summary>
public sealed class UnderscoreToSpaceConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value as string)?.Replace('_', ' ') ?? value;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}
