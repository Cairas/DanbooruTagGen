using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using DanbooruTagGen.Core.Models;

namespace DanbooruTagGen.App.Converters;

/// <summary>슬롯이 참조하는 풀(PoolId 하나 + 체이닝된 ExtraPoolIds)의 후보 태그를 전부 보여줄
/// 텍스트로 만든다. 레시피 빌더는 풀을 골라 참조만 할 뿐 안에 뭐가 들었는지 보려면 풀
/// 라이브러리 창을 따로 열어야 했다 — 여기서 바로 후보를 훑어볼 수 있게(수정은 풀 라이브러리에서만).</summary>
public sealed class PoolCandidatesTextConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var poolId = values.Length > 0 ? values[0] as string : null;
        var extraPoolIds = values.Length > 1 ? values[1] as IEnumerable : null;
        var pools = values.Length > 2 ? values[2] as IEnumerable : null;
        var poolList = pools?.Cast<Pool>().ToList() ?? new List<Pool>();

        var lines = new List<string>();
        if (!string.IsNullOrEmpty(poolId))
        {
            var pool = poolList.FirstOrDefault(p => p.Id == poolId);
            if (pool != null) lines.Add($"풀 '{pool.Name}' 후보 {pool.Candidates.Count}개: {string.Join(", ", pool.Candidates)}");
        }
        if (extraPoolIds != null)
            foreach (var id in extraPoolIds.Cast<string>())
            {
                var pool = poolList.FirstOrDefault(p => p.Id == id);
                if (pool != null) lines.Add($"풀 '{pool.Name}' 후보 {pool.Candidates.Count}개: {string.Join(", ", pool.Candidates)}");
            }
        return string.Join("\n", lines);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
