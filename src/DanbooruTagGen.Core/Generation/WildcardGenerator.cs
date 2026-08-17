using DanbooruTagGen.Core.Models;

namespace DanbooruTagGen.Core.Generation;

public sealed class WildcardGenerator
{
    private const int MaxDedupeRetries = 50;

    public static void Validate(Recipe recipe, IReadOnlyDictionary<string, Pool> poolsById)
    {
        if (recipe.Slots.Count == 0)
            throw new GenerationValidationException("레시피에 슬롯이 하나도 없습니다.");
        if (recipe.Slots.All(s => !s.IsEnabled))
            throw new GenerationValidationException("모든 슬롯이 꺼져 있습니다. 최소 하나는 켜세요.");

        foreach (var slot in recipe.Slots)
        {
            if (!slot.IsEnabled) continue; // 꺼둔 슬롯은 비어 있어도 검증 대상에서 제외

            switch (slot)
            {
                case FixedSlot f:
                    if (f.Tags.Count == 0)
                        throw new GenerationValidationException($"고정 슬롯 '{f.Label}'에 태그가 없습니다.");
                    break;

                case RandomPoolSlot r:
                    if (r.MinCount < 0)
                        throw new GenerationValidationException($"슬롯 '{r.Label}'의 MinCount가 음수입니다.");
                    if (r.MaxCount < r.MinCount)
                        throw new GenerationValidationException($"슬롯 '{r.Label}'의 MaxCount가 MinCount보다 작습니다.");

                    // Tags가 비어 있을 때만 풀 참조 자체의 유효성을 엄격히 따진다 — Tags가
                    // 이미 있으면 풀이 없거나 삭제됐어도(예: 참조가 끊긴 오래된 PoolId)
                    // Tags만으로 충분히 생성 가능하므로 여기서 막지 않는다.
                    if (r.Tags.Count == 0)
                    {
                        if (string.IsNullOrEmpty(r.PoolId))
                            throw new GenerationValidationException($"랜덤 슬롯 '{r.Label}'에 후보 태그가 없습니다. 태그를 넣으세요.");
                        if (!poolsById.ContainsKey(r.PoolId))
                            throw new GenerationValidationException($"슬롯 '{r.Label}'이 참조하는 풀 '{r.PoolId}'을 찾을 수 없습니다.");
                    }

                    var candidates = ResolveCandidates(r, poolsById);
                    if (candidates.Count == 0)
                        throw new GenerationValidationException($"랜덤 슬롯 '{r.Label}'에 후보 태그가 없습니다.");
                    if (r.MinCount > candidates.Count)
                        throw new GenerationValidationException($"슬롯 '{r.Label}'의 MinCount({r.MinCount})가 후보 수({candidates.Count})보다 많습니다.");
                    break;

                case AlternativeSlot alt:
                    if (alt.Groups.Count == 0)
                        throw new GenerationValidationException($"대안 슬롯 '{alt.Label}'에 그룹이 하나도 없습니다.");
                    if (alt.Groups.Any(g => g.Tags.Count == 0))
                        throw new GenerationValidationException($"대안 슬롯 '{alt.Label}'에 태그가 없는 그룹이 있습니다.");
                    if (alt.Groups.Any(g => g.Weight < 0))
                        throw new GenerationValidationException($"대안 슬롯 '{alt.Label}'에 가중치가 음수인 그룹이 있습니다.");
                    if (alt.Groups.Sum(g => (long)g.Weight) == 0)
                        throw new GenerationValidationException($"대안 슬롯 '{alt.Label}'의 모든 그룹 가중치가 0입니다. 최소 하나는 1 이상이어야 합니다.");
                    break;
            }
        }
    }

    public GenerationResult Generate(
        Recipe recipe,
        IReadOnlyDictionary<string, Pool> poolsById,
        GenerationOptions options,
        ConflictRules? conflicts = null,
        ITagLookup? tagInfo = null,
        AnimaPhraseBook? phrases = null)
    {
        Validate(recipe, poolsById);

        var rules = conflicts ?? ConflictRules.Empty;
        bool checkConflicts = options.AvoidConflicts && rules.Count > 0;
        // 메타데이터(빈도·그룹)가 없으면 가중/정렬은 자동으로 비활성(Uniform·미정렬)으로 폴백.
        var orderer = options.AutoOrderTags && tagInfo is not null ? new TagOrdering(tagInfo) : null;

        var rnd = new SystemRandomSource(options.Seed);
        var lines = new List<string>(options.LineCount);
        var warnings = new List<string>();
        var lineConflicts = new List<LineConflict>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        bool dupWarned = false, conflictWarned = false;

        var book = phrases ?? AnimaPhraseBook.Empty;

        // 한 줄을 생성하고(가중 추첨) 표준 순서로 정렬한다. 재추첨 루프와 공유.
        // 태그의 출처(SlotRole)를 같이 들고 다녀야 Anima 형식이 COSMETIC을 버릴 수 있다.
        List<(string Tag, SlotRole Role)> NextParts()
        {
            var p = GenerateLineParts(recipe, poolsById, options, rnd, tagInfo);
            if (orderer is null) return p;
            // 정렬은 태그 기준이므로, 정렬 결과 순서에 맞춰 역할을 다시 붙인다.
            var roleOf = new Dictionary<string, SlotRole>(StringComparer.Ordinal);
            foreach (var (t, r) in p) roleOf[t] = r;
            return orderer.Reorder(p.Select(x => x.Tag).ToList())
                          .Select(t => (t, roleOf.TryGetValue(t, out var r) ? r : SlotRole.Unknown))
                          .ToList();
        }

        string Render(List<(string Tag, SlotRole Role)> parts) =>
            options.Format == PromptFormat.Anima
                ? book.Render(parts, options.UnderscoreToSpace)
                : JoinLine(parts.Select(p => p.Tag).ToList(), options);

        for (int i = 0; i < options.LineCount; i++)
        {
            var parts = NextParts();
            var tags = parts.Select(p => p.Tag).ToList();
            string line = Render(parts);

            // 중복/모순을 한 번의 재추첨 루프에서 함께 피한다.
            int retries = 0;
            while (retries < MaxDedupeRetries &&
                   ((options.AvoidDuplicateLines && seen.Contains(line)) ||
                    (checkConflicts && rules.HasConflict(tags))))
            {
                parts = NextParts();
                tags = parts.Select(p => p.Tag).ToList();
                line = Render(parts);
                retries++;
            }

            if (options.AvoidDuplicateLines && seen.Contains(line) && !dupWarned)
            {
                warnings.Add("조합 공간이 부족해 일부 줄이 중복될 수 있습니다.");
                dupWarned = true;
            }

            // 최종 줄에 남은 모순은 항상 기록(회피 옵션과 무관하게 표시).
            if (rules.Count > 0)
            {
                var hits = rules.Detect(tags);
                if (hits.Count > 0)
                {
                    lineConflicts.Add(new LineConflict(i, hits));
                    if (!conflictWarned)
                    {
                        warnings.Add("일부 줄에 모순 태그가 함께 들어 있습니다.");
                        conflictWarned = true;
                    }
                }
            }

            seen.Add(line);
            lines.Add(line);
        }

        return new GenerationResult(lines, warnings) { Conflicts = lineConflicts };
    }

    /// <summary>한 줄의 태그 목록을 정식 태그명(언더스코어)으로 생성한다(블록리스트/줄내중복 적용).</summary>
    internal List<string> GenerateLineTags(
        Recipe recipe,
        IReadOnlyDictionary<string, Pool> poolsById,
        GenerationOptions options,
        IRandomSource rnd,
        ITagLookup? tagInfo = null)
    {
        var picked = GenerateLineParts(recipe, poolsById, options, rnd, tagInfo);
        return picked.Select(p => p.Tag).ToList();
    }

    /// <summary>한 줄의 태그를 "어느 역할의 슬롯에서 나왔는지"와 함께 생성한다.
    /// Anima 형식이 COSMETIC 태그를 버리고 MAJOR 태그로 서술문을 만들어야 해서 출처가 필요하다.
    /// 기존 <see cref="GenerateLineTags"/>는 여기서 태그만 뽑아 쓴다(동작 동일).</summary>
    internal List<(string Tag, SlotRole Role)> GenerateLineParts(
        Recipe recipe,
        IReadOnlyDictionary<string, Pool> poolsById,
        GenerationOptions options,
        IRandomSource rnd,
        ITagLookup? tagInfo = null)
    {
        var blocklist = new HashSet<string>(options.Blocklist, StringComparer.Ordinal);
        var seenTags = options.DedupeWithinLine ? new HashSet<string>(StringComparer.Ordinal) : null;
        var emitted = new List<(string, SlotRole)>();

        foreach (var slot in recipe.Slots)
        {
            if (!slot.IsEnabled) continue; // 토글로 꺼둔 슬롯은 이번 줄에서 완전히 건너뜀

            var role = SlotRoleClassifier.Classify(slot);
            switch (slot)
            {
                case FixedSlot f:
                    foreach (var t in f.Tags) Add(t, role);
                    break;
                case RandomPoolSlot r:
                    foreach (var t in PickRandom(ResolveCandidates(r, poolsById), r, rnd, options, tagInfo)) Add(t, role);
                    break;
                case AlternativeSlot alt:
                    if (alt.Groups.Count > 0)
                    {
                        var group = PickGroup(alt, rnd);
                        foreach (var t in group.Tags) Add(t, role);
                    }
                    break;
            }
        }
        return emitted;

        void Add(string tag, SlotRole r)
        {
            if (blocklist.Contains(tag)) return;
            if (seenTags != null && !seenTags.Add(tag)) return;
            emitted.Add((tag, r));
        }
    }

    /// <summary>태그 목록을 출력 한 줄로 합친다. 출력 시점에만 '_'→공백 변환.</summary>
    private static string JoinLine(List<string> tags, GenerationOptions options) =>
        options.UnderscoreToSpace
            ? string.Join(", ", tags.Select(AnimaPhraseBook.ToDisplay))
            : string.Join(", ", tags);

    internal string GenerateLine(
        Recipe recipe,
        IReadOnlyDictionary<string, Pool> poolsById,
        GenerationOptions options,
        IRandomSource rnd) => JoinLine(GenerateLineTags(recipe, poolsById, options, rnd), options);

    /// <summary>랜덤 슬롯의 후보를 결정한다: 인라인 Tags, PoolId가 가리키는 풀, ExtraPoolIds로
    /// 체이닝된 풀들의 후보를 전부 합친다(중복 제거). 예전엔 Tags가 하나라도 있으면 PoolId를
    /// 통째로 무시했는데, 그러면 풀을 연결해 둔 슬롯에 태그 하나만 실수로 더블클릭해 넣어도
    /// 나머지 풀 후보 전부가 조용히 사라져 "태그 몇 개가 안 들어간다"는 증상으로 나타났다.
    /// 합치는 쪽이 덜 놀랍다.</summary>
    private static IReadOnlyList<string> ResolveCandidates(RandomPoolSlot slot, IReadOnlyDictionary<string, Pool> poolsById)
    {
        var merged = new List<string>(slot.Tags.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in slot.Tags) if (seen.Add(t)) merged.Add(t);

        if (!string.IsNullOrEmpty(slot.PoolId) && poolsById.TryGetValue(slot.PoolId, out var pool))
            foreach (var t in pool.Candidates) if (seen.Add(t)) merged.Add(t);

        foreach (var extraId in slot.ExtraPoolIds)
            if (!string.IsNullOrEmpty(extraId) && poolsById.TryGetValue(extraId, out var extraPool))
                foreach (var t in extraPool.Candidates) if (seen.Add(t)) merged.Add(t);

        return merged;
    }

    private static IEnumerable<string> PickRandom(
        IReadOnlyList<string> candidates, RandomPoolSlot slot, IRandomSource rnd,
        GenerationOptions options, ITagLookup? tagInfo)
    {
        int rangeWidth = slot.MaxCount - slot.MinCount + 1;
        int count = slot.MinCount + (rangeWidth > 1 ? rnd.Next(rangeWidth) : 0);
        count = Math.Min(count, candidates.Count);

        // 빈도 메타데이터가 있을 때만 가중 추첨. 없으면 균등으로 폴백.
        return options.Sampling == SamplingMode.Weighted && tagInfo is not null
            ? PickWeighted(candidates, count, rnd, tagInfo)
            : PickUniform(candidates, count, rnd);
    }

    /// <summary>비복원 균등 추첨.</summary>
    private static List<string> PickUniform(IReadOnlyList<string> candidates, int count, IRandomSource rnd)
    {
        var remaining = new List<string>(candidates);
        var picked = new List<string>(count);
        for (int k = 0; k < count && remaining.Count > 0; k++)
        {
            int idx = rnd.Next(remaining.Count);
            picked.Add(remaining[idx]);
            remaining.RemoveAt(idx);
        }
        return picked;
    }

    /// <summary>비복원 가중 추첨. 가중치 = max(1, round(sqrt(postCount))).
    /// IRandomSource가 정수만 주므로 정수 누적 가중치 룰렛으로 구현해 결정적 테스트가 가능하다.
    /// 제곱근으로 누르는 이유: danbooru 빈도는 편차가 수천 배라 선형 가중 시 희귀 태그가
    /// 사실상 사라진다. 제곱근은 인기 태그를 우대하되 큐레이션 태그도 살려 둔다.</summary>
    private static List<string> PickWeighted(IReadOnlyList<string> candidates, int count, IRandomSource rnd, ITagLookup tagInfo)
    {
        var remaining = new List<string>(candidates);
        var weights = new List<int>(remaining.Count);
        foreach (var c in remaining) weights.Add(WeightOf(c, tagInfo));

        var picked = new List<string>(count);
        for (int k = 0; k < count && remaining.Count > 0; k++)
        {
            long total = 0;
            for (int j = 0; j < weights.Count; j++) total += weights[j];

            // total은 int 범위를 넘을 수 있어(많은 후보) long으로 누적하되, 추첨 눈금은
            // IRandomSource(int) 한계 안에서 안전하게 정규화한다.
            int roll = total <= int.MaxValue ? rnd.Next((int)total) : rnd.Next(int.MaxValue);
            long target = total <= int.MaxValue ? roll : (long)((double)roll / int.MaxValue * total);

            int idx = 0;
            long acc = 0;
            for (; idx < weights.Count; idx++)
            {
                acc += weights[idx];
                if (target < acc) break;
            }
            if (idx >= remaining.Count) idx = remaining.Count - 1; // 부동소수 경계 보호

            picked.Add(remaining[idx]);
            remaining.RemoveAt(idx);
            weights.RemoveAt(idx);
        }
        return picked;
    }

    /// <summary>대안 그룹 하나를 가중치에 비례해 선택한다. 모든 가중치가 기본값 1이면
    /// 기존과 똑같은 균등 추첨이 된다(하위 호환). 가중치 0인 그룹은 절대 뽑히지 않는다.</summary>
    private static AlternativeGroup PickGroup(AlternativeSlot alt, IRandomSource rnd)
    {
        long total = 0;
        foreach (var g in alt.Groups) total += g.Weight;

        // Validate가 total>0을 보장하지만, Generate를 거치지 않는 호출 경로에서도 안전하도록
        // 방어적으로 균등 폴백을 둔다.
        if (total <= 0) return alt.Groups[rnd.Next(alt.Groups.Count)];

        int roll = total <= int.MaxValue ? rnd.Next((int)total) : rnd.Next(int.MaxValue);
        long target = total <= int.MaxValue ? roll : (long)((double)roll / int.MaxValue * total);

        long acc = 0;
        foreach (var g in alt.Groups)
        {
            acc += g.Weight;
            if (target < acc) return g;
        }
        return alt.Groups[^1]; // 부동소수 경계 보호
    }

    /// <summary>태그의 추첨 가중치. 빈도를 모르면 1(균등에 가깝게).</summary>
    private static int WeightOf(string tag, ITagLookup tagInfo)
    {
        int postCount = tagInfo.Lookup(tag)?.PostCount ?? 0;
        if (postCount <= 0) return 1;
        return Math.Max(1, (int)Math.Round(Math.Sqrt(postCount)));
    }
}
