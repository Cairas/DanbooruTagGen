using System.Collections.ObjectModel;
using DanbooruTagGen.Core.Models;

namespace DanbooruTagGen.Core.Generation;

/// <summary>생성 직전에 훅을 레시피 슬롯 목록에 끼워 넣는다.
/// <para>
/// 훅은 평범한 <see cref="FixedSlot"/>·<see cref="RandomPoolSlot"/>으로 변환되므로
/// <see cref="WildcardGenerator"/>는 훅의 존재를 전혀 모른다 — 검증·모순 검사·중복 제거·
/// 태그 정렬이 전부 기존 경로 그대로 적용된다. 생성기를 수정하지 않아도 되는 이유가 이것이다.
/// </para>
/// <para>
/// 호출부는 반드시 <b>복사본</b> 레시피를 넘겨야 한다. 이 메서드는 넘겨받은
/// <see cref="Recipe.Slots"/>를 제자리에서 수정한다.
/// </para></summary>
public static class HookApplier
{
    /// <summary>켜져 있는 훅들을 recipe에 끼워 넣는다.</summary>
    /// <returns>후보 태그가 없어 건너뛴 훅의 이름들. 생성 결과 경고로 보여 주면 된다.</returns>
    public static IReadOnlyList<string> Apply(
        Recipe recipe,
        IEnumerable<GenerationHook> hooks,
        IReadOnlyDictionary<string, Pool> poolsById)
    {
        var skipped = new List<string>();
        // 목표 인덱스는 전부 "훅을 하나도 안 넣은 원본" 기준으로 계산한다. 하나씩 넣으면서
        // 계산하면 먼저 들어간 훅이 뒤 훅의 위치를 밀어 결과가 직관과 어긋난다.
        int originalCount = recipe.Slots.Count;
        var planned = new List<(int Index, int Order, Slot Slot)>();

        foreach (var hook in hooks)
        {
            if (!hook.IsEnabled) continue;
            var candidates = CollectCandidates(hook, poolsById);
            if (candidates.Count == 0)
            {
                skipped.Add(hook.Name);
                continue;
            }
            planned.Add((ResolveIndex(hook, originalCount), hook.Order, ToSlot(hook, candidates)));
        }

        // 인덱스가 큰 것부터 넣어야 앞선 삽입이 뒤 훅의 목표 인덱스를 밀지 않는다.
        // 같은 인덱스면 Order가 작은 훅이 앞에 와야 하므로 Order 내림차순으로 넣는다.
        foreach (var p in planned.OrderByDescending(p => p.Index).ThenByDescending(p => p.Order))
            recipe.Slots.Insert(p.Index, p.Slot);

        return skipped;
    }

    private static int ResolveIndex(GenerationHook hook, int slotCount) => hook.Placement switch
    {
        HookPlacement.Front => 0,
        HookPlacement.AtIndex => Math.Clamp(hook.Index, 0, slotCount),
        _ => slotCount,
    };

    /// <summary>직접 입력 태그와 참조 풀 후보를 하나로 합친다(RandomPoolSlot의 기존 의미).
    /// 공백뿐인 입력은 버리고, 중복은 먼저 나온 것을 남긴다.</summary>
    private static List<string> CollectCandidates(
        GenerationHook hook, IReadOnlyDictionary<string, Pool> poolsById)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in hook.Tags)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var tag = raw.Trim();
            if (seen.Add(tag)) result.Add(tag);
        }

        if (!string.IsNullOrEmpty(hook.PoolId) && poolsById.TryGetValue(hook.PoolId, out var pool))
            foreach (var candidate in pool.Candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                var tag = candidate.Trim();
                if (seen.Add(tag)) result.Add(tag);
            }

        return result;
    }

    private static Slot ToSlot(GenerationHook hook, List<string> candidates)
    {
        if (hook.Kind == HookSlotKind.Fixed)
            // 고정 훅은 후보를 전부 펼쳐 넣는다 — FixedSlot에는 풀 참조 개념이 없고,
            // "이 풀의 태그를 항상 붙인다"가 고정 훅의 자연스러운 의미다.
            return new FixedSlot
            {
                Label = hook.Name,
                Tags = new ObservableCollection<string>(candidates),
            };

        // 개수를 여기서 보정한다. MinCount가 후보 수보다 크거나 MaxCount가 MinCount보다
        // 작으면 WildcardGenerator.Validate가 예외를 던져 생성 전체가 죽는다 — 훅 설정
        // 하나가 잘못됐다고 생성을 못 하게 만들 이유가 없다.
        int min = Math.Clamp(hook.MinCount, 0, candidates.Count);
        int max = Math.Clamp(hook.MaxCount, min, candidates.Count);

        return new RandomPoolSlot
        {
            Label = hook.Name,
            Tags = new ObservableCollection<string>(hook.Tags
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())),
            PoolId = hook.PoolId,
            MinCount = min,
            MaxCount = max,
        };
    }
}
