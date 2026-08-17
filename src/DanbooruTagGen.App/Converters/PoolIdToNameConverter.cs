using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using DanbooruTagGen.Core.Models;

namespace DanbooruTagGen.App.Converters;

/// <summary>풀 Id(string)와 Pools 목록을 받아 그 풀의 이름을 돌려준다. 체이닝된 풀 칩은
/// ExtraPoolIds(원시 문자열 컬렉션)에 바인딩되므로, 화면엔 Id 대신 이름을 보여주려고 쓴다.</summary>
public sealed class PoolIdToNameConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var poolId = values.Length > 0 ? values[0] as string : null;
        var pools = values.Length > 1 ? values[1] as IEnumerable : null;
        if (string.IsNullOrEmpty(poolId)) return "";
        var pool = pools?.Cast<Pool>().FirstOrDefault(p => p.Id == poolId);
        return pool?.Name ?? poolId;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
