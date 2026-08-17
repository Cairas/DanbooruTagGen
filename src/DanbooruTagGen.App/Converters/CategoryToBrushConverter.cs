using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using DanbooruTagGen.Core.Models;

namespace DanbooruTagGen.App.Converters;

public sealed class CategoryToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        TagCategory.Character => Brushes.MediumSeaGreen,
        TagCategory.Copyright => Brushes.Orchid,
        TagCategory.Artist => Brushes.IndianRed,
        TagCategory.Meta => Brushes.Goldenrod,
        _ => Brushes.CornflowerBlue,
    };

    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}
