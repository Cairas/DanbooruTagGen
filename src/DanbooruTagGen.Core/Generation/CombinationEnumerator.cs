using DanbooruTagGen.Core.Models;

namespace DanbooruTagGen.Core.Generation;

/// <summary>레시피가 낼 수 있는 슬롯 선택 조합을 전부 펼친다(조합이 작은 레시피 전용). 생성기가 서로 다른 줄이 바닥난 뒤 남은 조합을
/// 정확히 채우는 데 쓴다 — 무작위 재추첨만으로는 가중치가 낮은 마지막 몇 조합을 좀처럼 못 뽑아 일찍 멈춘다
/// (실측: 90종 레시피가 76줄에서 멈춤).
/// <para><see cref="Combo.Choices"/>는 슬롯 번호 자리마다: ALT면 <see cref="AlternativeGroup"/>, 랜덤 풀이면 뽑을 후보 목록(빈 목록 = 안 나옴),
/// 고정·꺼진 슬롯은 null. <see cref="Combo.Probability"/>는 그 조합의 명목 확률(ALT weight 비율 × 풀 발동·개수·균등 부분집합) —
/// 채우는 순서를 정하는 데만 쓰므로 빈도 가중·반복 회피·로컬 필터의 미세한 차이는 무시한다.</para></summary>
internal static class CombinationEnumerator
{
    internal sealed record Combo(object?[] Choices, double Probability);

    /// <summary>전부 펼친다. 조합 수가 <paramref name="max"/>를 넘거나 펼칠 수 없는 슬롯이 있으면 null.</summary>
    internal static List<Combo>? Enumerate(Recipe recipe, IReadOnlyDictionary<string, Pool> poolsById, int max)
    {
        var perSlot = new List<(int Index, List<(object Choice, double P)> Options)>();
        long total = 1;
        for (int si = 0; si < recipe.Slots.Count; si++)
        {
            var slot = recipe.Slots[si];
            if (!slot.IsEnabled) continue;
            List<(object, double)>? options;
            switch (slot)
            {
                case FixedSlot:
                    continue;
                case AlternativeSlot alt:
                {
                    var live = alt.Groups.Where(g => g.Weight > 0).ToList();
                    if (live.Count == 0) return null;
                    double sum = live.Sum(g => (double)g.Weight);
                    options = live.Select(g => ((object)g, g.Weight / sum)).ToList();
                    break;
                }
                case RandomPoolSlot r:
                    options = PoolOptions(r, poolsById, max);
                    if (options is null) return null;
                    break;
                default:
                    return null;
            }
            total *= options.Count;
            if (total > max) return null;
            perSlot.Add((si, options));
        }

        var result = new List<Combo>((int)total);
        var choices = new object?[recipe.Slots.Count];
        void Expand(int depth, double p)
        {
            if (depth == perSlot.Count) { result.Add(new Combo((object?[])choices.Clone(), p)); return; }
            var (index, options) = perSlot[depth];
            foreach (var (choice, q) in options)
            {
                choices[index] = choice;
                Expand(depth + 1, p * q);
            }
            choices[index] = null;
        }
        Expand(0, 1);
        return result;
    }

    /// <summary>풀 슬롯 하나의 결과들: 뽑을 개수 k(= min(MinCount + 균등[0, 폭), 뽑을 수 있는 후보 수))마다 크기 k 부분집합 전부.
    /// 발동 확률이 100 미만이면 "안 나옴"(빈 목록)이 더해진다(k = 0과 합친다).</summary>
    private static List<(object, double)>? PoolOptions(RandomPoolSlot r, IReadOnlyDictionary<string, Pool> poolsById, int max)
    {
        var pickable = WildcardGenerator.ResolveCandidates(r, poolsById)
            .Where(c => TagKey.ParsePickWeight(c).Weight > 0).ToList();
        int n = pickable.Count;
        int width = Math.Max(1, r.MaxCount - r.MinCount + 1);
        var pk = new Dictionary<int, double>();
        for (int j = 0; j < width; j++)
        {
            int k = Math.Min(r.MinCount + j, n);
            pk[k] = pk.GetValueOrDefault(k) + 1.0 / width;
        }
        double chance = Math.Clamp(r.ChancePercent, 0, 100) / 100.0;

        double emptyP = 1 - chance;
        var options = new List<(object, double)>();
        foreach (var (k, p) in pk.OrderBy(kv => kv.Key))
        {
            if (k == 0) { emptyP += chance * p; continue; }
            long count = Binomial(n, k);
            if (count > max) return null;
            double each = chance * p / count;
            foreach (var subset in Subsets(pickable, k)) options.Add((subset, each));
            if (options.Count > max) return null;
        }
        if (emptyP > 0) options.Insert(0, (Array.Empty<string>(), emptyP));
        return options;
    }

    private static long Binomial(int n, int k)
    {
        if (k < 0 || k > n) return 0;
        k = Math.Min(k, n - k);
        long r = 1;
        for (int i = 0; i < k; i++) r = r * (n - i) / (i + 1);
        return r;
    }

    private static IEnumerable<string[]> Subsets(IReadOnlyList<string> items, int k)
    {
        var idx = Enumerable.Range(0, k).ToArray();
        while (true)
        {
            yield return idx.Select(i => items[i]).ToArray();
            int pos = k - 1;
            while (pos >= 0 && idx[pos] == items.Count - k + pos) pos--;
            if (pos < 0) yield break;
            idx[pos]++;
            for (int j = pos + 1; j < k; j++) idx[j] = idx[j - 1] + 1;
        }
    }

    /// <summary>확률에 비례한 무작위 순서(비복원 가중 추첨과 같은 분포 — 지수 키 −ln(u)/p 오름차순). 확률 0인 조합은 뺀다.</summary>
    internal static List<Combo> WeightedOrder(List<Combo> combos, IRandomSource rnd)
    {
        var keyed = new List<(double Key, Combo Combo)>(combos.Count);
        foreach (var c in combos)
        {
            if (c.Probability <= 0) continue;
            double u = (rnd.Next(int.MaxValue) + 1.0) / int.MaxValue;
            keyed.Add((-Math.Log(u) / c.Probability, c));
        }
        keyed.Sort((a, b) => a.Key.CompareTo(b.Key));
        return keyed.Select(x => x.Combo).ToList();
    }
}
