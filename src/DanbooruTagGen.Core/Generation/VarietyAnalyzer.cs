using DanbooruTagGen.Core.Models;

namespace DanbooruTagGen.Core.Generation;

/// <summary><c>tools/visual_variety_scan.py</c>의 MAJOR 조합 수 계산을 C#으로 포팅한 것.
/// 축 등급 판정은 <see cref="SlotRoleClassifier"/>를 그대로 재사용하고(같은 기준을 두 곳에
/// 두면 갈라진다), 여기서는 "슬롯 하나가 만들 수 있는 서로 다른 결과 가짓수"만 계산한다.
/// <para>파이썬 쪽 <c>slot_cardinality</c>를 고치면 이쪽도 같이 고쳐야 한다.</para></summary>
public static class VarietyAnalyzer
{
    /// <summary>파이썬 스캐너와 동일한 기준(200 미만이면 "시각 축 부족").</summary>
    public const int Threshold = 200;

    /// <summary>슬롯 하나가 만들 수 있는 서로 다른 결과의 가짓수.
    /// 고정=1, 대안=weight≠0인 그룹 수(최소 1), 랜덤 풀=Σ C(n,k) for k in [minCount,maxCount]
    /// (n=인라인 태그+참조 풀 후보를 합쳐 중복 제거한 개수).</summary>
    public static long SlotCardinality(Slot slot, IReadOnlyDictionary<string, Pool> poolsById)
    {
        switch (slot)
        {
            case FixedSlot:
                return 1;

            case AlternativeSlot alt:
                var live = alt.Groups.Count(g => g.Weight != 0);
                return Math.Max(live, 1);

            case RandomPoolSlot r:
                var candidates = new HashSet<string>(StringComparer.Ordinal);
                foreach (var t in r.Tags) candidates.Add(t);
                if (!string.IsNullOrEmpty(r.PoolId) && poolsById.TryGetValue(r.PoolId, out var pool))
                    foreach (var t in pool.Candidates) candidates.Add(t);
                foreach (var extraId in r.ExtraPoolIds)
                    if (poolsById.TryGetValue(extraId, out var extraPool))
                        foreach (var t in extraPool.Candidates) candidates.Add(t);

                int n = candidates.Count;
                if (n == 0) return 1;
                int lo = r.MinCount;
                int hi = Math.Min(r.MaxCount, n);
                if (hi < lo) return 1;

                long total = 0;
                for (int k = lo; k <= hi; k++) total += Combinations(n, k);
                return Math.Max(total, 1);

            default:
                return 1;
        }
    }

    /// <summary>활성 슬롯 중 MAJOR로 분류되고 가짓수가 1보다 큰 것만 곱한 조합 수.
    /// 레시피 라이브러리(저장된 <see cref="Recipe"/>)와 레시피 빌더(아직 저장 안 된
    /// 슬롯 목록) 양쪽에서 쓸 수 있게 <see cref="IEnumerable{Slot}"/>을 직접 받는다.</summary>
    public static long ComputeMajorCombinations(IEnumerable<Slot> slots, IReadOnlyDictionary<string, Pool> poolsById)
    {
        long major = 1;
        foreach (var slot in slots)
        {
            if (!slot.IsEnabled) continue;
            if (SlotRoleClassifier.Classify(slot) != SlotRole.Major) continue;
            var card = SlotCardinality(slot, poolsById);
            if (card > 1) major *= card;
        }
        return major;
    }

    public static long ComputeMajorCombinations(Recipe recipe, IReadOnlyDictionary<string, Pool> poolsById)
        => ComputeMajorCombinations(recipe.Slots, poolsById);

    /// <summary>이항계수 C(n,k). 증분 곱셈 순서(× (n-i), ÷ (i+1))는 매 단계에서 항상 정수로
    /// 나눠떨어지므로 중간에 반올림 오차가 생기지 않는다.</summary>
    private static long Combinations(int n, int k)
    {
        if (k < 0 || k > n) return 0;
        k = Math.Min(k, n - k);
        long result = 1;
        for (int i = 0; i < k; i++)
            result = result * (n - i) / (i + 1);
        return result;
    }
}
