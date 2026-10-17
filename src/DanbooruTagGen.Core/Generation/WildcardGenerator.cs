using DanbooruTagGen.Core.Models;

namespace DanbooruTagGen.Core.Generation;

public sealed class WildcardGenerator
{
    private const int MaxDedupeRetries = 50;
    /// <summary>진행률을 보고하는 간격(줄). 매 줄 보고하면 UI 스레드로 넘어가는 알림이
    /// 생성 자체보다 비싸진다.</summary>
    private const int ProgressChunk = 25;

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
                    if (r.ChancePercent == RandomPoolSlot.UnparsedChance)
                        throw new GenerationValidationException($"슬롯 '{r.Label}'의 발동 확률(chancePercent)이 정수가 아닙니다. 0~100 사이 정수로 쓰세요.");
                    if (r.ChancePercent < 0 || r.ChancePercent > 100)
                        throw new GenerationValidationException($"슬롯 '{r.Label}'의 발동 확률({r.ChancePercent}%)이 0~100 범위를 벗어났습니다.");

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
                    // 추첨 가중치 0(0::tag)인 후보는 절대 안 뽑히므로 "뽑을 수 있는 후보 수"에서 뺀다.
                    // 접두가 없는 기존 후보는 전부 1이라 예전 검사와 같다.
                    int pickable = candidates.Count(c => TagKey.ParsePickWeight(c).Weight > 0);
                    if (pickable == 0)
                        throw new GenerationValidationException($"랜덤 슬롯 '{r.Label}'의 모든 후보 추첨 가중치가 0입니다. 최소 하나는 1 이상이어야 합니다.");
                    if (r.MinCount > pickable)
                        throw new GenerationValidationException($"슬롯 '{r.Label}'의 MinCount({r.MinCount})가 후보 수({pickable})보다 많습니다.");
                    break;

                case AlternativeSlot alt:
                    if (alt.Groups.Count == 0)
                        throw new GenerationValidationException($"대안 슬롯 '{alt.Label}'에 그룹이 하나도 없습니다.");
                    // 태그가 없는 그룹("없음"/"기본" 같은 무발동 분기)은 의도된 패턴이다 —
                    // 가중치로 "아무것도 안 나올 확률"을 정밀 제어할 때 randomPool의
                    // min/max 방식 대신 이 방식을 쓴다(n534/ztest/n535에서 이미 사용 중).
                    if (alt.Groups.Any(g => g.Weight < 0))
                        throw new GenerationValidationException($"대안 슬롯 '{alt.Label}'에 가중치가 음수인 그룹이 있습니다.");
                    if (alt.Groups.Sum(g => (long)g.Weight) == 0)
                        throw new GenerationValidationException($"대안 슬롯 '{alt.Label}'의 모든 그룹 가중치가 0입니다. 최소 하나는 1 이상이어야 합니다.");
                    break;
            }

            // 가중치 범위 표기((tag:1.1~1.3))의 오타·역순을 생성 전에 막는다. 생성 중에 만나면
            // 원문을 그대로 흘려보낼 수밖에 없어, 잘못된 표기가 프롬프트에 섞인 줄 모르게 된다.
            foreach (var tag in TagsOf(slot, poolsById))
            {
                if (WeightRange.FindError(tag) is { } rangeError)
                    throw new GenerationValidationException($"슬롯 '{slot.Label}'의 '{tag}': {rangeError}");
                // 추첨 가중치 접두의 소수·음수도 같은 이유로 생성 전에 막는다(조용히 1로 취급하지 않는다).
                if (TagKey.ParsePickWeight(tag).Error is { } pickError)
                    throw new GenerationValidationException($"슬롯 '{slot.Label}'의 '{tag}': {pickError}");
            }
        }
    }

    /// <summary>슬롯이 출력할 수 있는 모든 태그 문자열(대안 슬롯은 전 그룹).</summary>
    private static IEnumerable<string> TagsOf(Slot slot, IReadOnlyDictionary<string, Pool> poolsById) => slot switch
    {
        FixedSlot f => f.Tags,
        RandomPoolSlot r => ResolveCandidates(r, poolsById),
        AlternativeSlot alt => alt.Groups.SelectMany(g => g.Tags),
        _ => Enumerable.Empty<string>(),
    };

    /// <param name="progress">완성된 줄 수를 보고한다(백그라운드 실행 시 진행률 표시용).
    /// 줄마다 호출하면 알림이 폭주하므로 <see cref="ProgressChunk"/>줄마다 한 번만 보고한다.</param>
    /// <param name="cancellationToken">사용자가 생성을 중단하면 <see cref="OperationCanceledException"/>.
    /// 레시피 수십 개 × 수백 줄이면 수 초가 걸려, 중간에 그만둘 방법이 필요하다.</param>
    public GenerationResult Generate(
        Recipe recipe,
        IReadOnlyDictionary<string, Pool> poolsById,
        GenerationOptions options,
        ConflictRules? conflicts = null,
        ITagLookup? tagInfo = null,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default) =>
        GenerateCore(recipe, poolsById, options, conflicts, tagInfo, progress, cancellationToken, observer: null);

    /// <summary><see cref="Generate"/>의 본체. <paramref name="observer"/>는 최종 채택된 줄마다(재추첨으로 버린
    /// 시도는 빼고) 그 줄에서 실제로 뽑힌 ALT 그룹·풀 후보를 받는다 — 실효 확률 리포트
    /// (<see cref="PickRateReport"/>)가 생성기를 다시 구현하지 않고 진짜 추첨 경로의 통계를 내려고 쓴다.</summary>
    internal GenerationResult GenerateCore(
        Recipe recipe,
        IReadOnlyDictionary<string, Pool> poolsById,
        GenerationOptions options,
        ConflictRules? conflicts,
        ITagLookup? tagInfo,
        IProgress<int>? progress,
        CancellationToken cancellationToken,
        Action<LineTrace>? observer)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(recipe, poolsById);

        var rules = conflicts ?? ConflictRules.Empty;
        bool checkConflicts = options.AvoidConflicts && rules.Count > 0;
        var local = checkConflicts && options.LocalConflictFiltering ? new LocalFilter(rules) : null;
        var trace = observer != null ? new List<PickRecord>() : null;
        // 메타데이터(빈도·그룹)가 없으면 가중/정렬은 자동으로 비활성(Uniform·미정렬)으로 폴백.
        var orderer = options.AutoOrderTags && tagInfo is not null ? new TagOrdering(tagInfo) : null;

        var rnd = new SystemRandomSource(options.Seed);
        var lines = new List<string>(options.LineCount);
        var warnings = new List<string>();
        var lineConflicts = new List<LineConflict>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        bool dupWarned = false, conflictWarned = false;

        // ALT 슬롯마다 "바로 전 줄에서 뽑힌 그룹"을 기억해 뒀다가, 이번 줄에서 같은 그룹이
        // 다시 뽑히면 몇 번만 다시 굴려 본다("장면 도입"처럼 옵션이 2~3개뿐인 슬롯이 같은
        // 문장을 여러 줄 연달아 뽑아 화면에서 "다 똑같다"는 인상을 주는 걸 줄이려는 목적,
        // 2026-08-26). 이 딕셔너리는 Generate() 호출 하나 안에서만 산다 — 다른 레시피/다른
        // Generate() 호출로 새지 않는다.
        var lastPickedByAlt = new Dictionary<AlternativeSlot, AlternativeGroup>();

        // 한 줄을 생성하고(가중 추첨) 표준 순서로 정렬한다. 재추첨 루프와 공유.
        List<(string Tag, SlotRole Role)> NextParts()
        {
            trace?.Clear();   // 재추첨으로 버린 시도의 기록은 남기지 않는다
            var p = GenerateLineParts(recipe, poolsById, options, rnd, tagInfo, lastPickedByAlt, local, trace);
            if (orderer is null) return p;
            // 정렬은 태그 기준이므로, 정렬 결과 순서에 맞춰 역할을 다시 붙인다.
            var roleOf = new Dictionary<string, SlotRole>(StringComparer.Ordinal);
            foreach (var (t, r) in p) roleOf[t] = r;
            return orderer.Reorder(p.Select(x => x.Tag).ToList())
                          .Select(t => (t, roleOf.TryGetValue(t, out var r) ? r : SlotRole.Unknown))
                          .ToList();
        }

        string Render(List<(string Tag, SlotRole Role)> parts) =>
            JoinLine(parts.Select(p => p.Tag).ToList(), options);

        for (int i = 0; i < options.LineCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (progress != null && i % ProgressChunk == 0) progress.Report(i);

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
            bool leftover = false;
            if (rules.Count > 0)
            {
                var hits = rules.Detect(tags);
                if (hits.Count > 0)
                {
                    leftover = true;
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
            observer?.Invoke(new LineTrace(i, trace!.ToArray(), retries, leftover));
        }

        progress?.Report(lines.Count);
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

    /// <summary>한 줄의 태그를 "어느 역할의 슬롯에서 나왔는지"와 함께 생성한다. 태그 순서
    /// 자동 배치(TagOrdering)가 역할별로 재배치할 때 이 출처 정보가 필요하다.
    /// 기존 <see cref="GenerateLineTags"/>는 여기서 태그만 뽑아 쓴다(동작 동일).</summary>
    internal List<(string Tag, SlotRole Role)> GenerateLineParts(
        Recipe recipe,
        IReadOnlyDictionary<string, Pool> poolsById,
        GenerationOptions options,
        IRandomSource rnd,
        ITagLookup? tagInfo = null,
        Dictionary<AlternativeSlot, AlternativeGroup>? lastPickedByAlt = null,
        LocalFilter? local = null,
        List<PickRecord>? trace = null)
    {
        // 블록리스트·줄 내 중복은 원문이 아니라 판정 키(TagKey)로 본다 — "(lowres:1.2)"나
        // "(문장:1.1), lowres"도 lowres로 식별돼야 블록리스트가 새지 않는다.
        var blocklist = TagKey.KeySet(options.Blocklist);
        var seenTags = options.DedupeWithinLine ? new HashSet<string>(StringComparer.Ordinal) : null;
        // implies:만 있는 문자열의 중복 판정용. 출력 조각의 중복(seenTags)과 섞지 않는다 — 섞으면 앞서 나온
        // implies:voyeurism 때문에 뒤의 진짜 voyeurism 태그가 "이미 나옴"으로 빠진다.
        var seenImplied = options.DedupeWithinLine ? new HashSet<string>(StringComparer.Ordinal) : null;
        // 로컬 필터용: 지금까지 이 줄에 실제로 들어간 판정 키(implies: 포함).
        var lineKeys = local != null ? new HashSet<string>(StringComparer.Ordinal) : null;
        var emitted = new List<(string, SlotRole)>();

        for (int si = 0; si < recipe.Slots.Count; si++)
        {
            var slot = recipe.Slots[si];
            if (!slot.IsEnabled) continue; // 토글로 꺼둔 슬롯은 이번 줄에서 완전히 건너뜀

            var role = SlotRoleClassifier.Classify(slot);
            switch (slot)
            {
                case FixedSlot f:
                    foreach (var t in f.Tags) Add(t, role);
                    break;
                case RandomPoolSlot r:
                    foreach (var t in PickRandom(ResolveCandidates(r, poolsById), r, rnd, options, tagInfo, local, lineKeys))
                        if (Add(t, role)) trace?.Add(new PickRecord(si, null, t));
                    break;
                case AlternativeSlot alt:
                    if (alt.Groups.Count > 0)
                    {
                        AlternativeGroup? avoid = null;
                        lastPickedByAlt?.TryGetValue(alt, out avoid);
                        var group = PickGroup(alt, rnd, avoid, local, lineKeys);
                        if (lastPickedByAlt != null) lastPickedByAlt[alt] = group;
                        trace?.Add(new PickRecord(si, group, null));
                        foreach (var t in group.Tags) Add(t, role);
                    }
                    break;
            }
        }
        return emitted;

        // 모든 슬롯(훅 포함)의 태그가 합류하는 단일 지점. 가중치 범위는 여기서, 줄마다 새로
        // 뽑는다 — 같은 rnd를 써야 시드 재현성이 유지된다. 블록리스트/중복 판정은 판정 키 기준이라
        // 범위 치환 전후 어느 쪽이든 결과가 같다(키는 가중치 표기를 벗긴다).
        // 출력 문자열은 원문 그대로 — 묶음 안 조각 하나만 빼는 식으로 고치지 않고 후보 단위로 넣고 뺀다:
        //   • 추첨 가중치 접두(N::)는 여기서 떼어 낸다(추첨이 끝났으니 더 쓸 데가 없다).
        //   • implies: 조각은 남겨 둔다 — 줄 단위 모순 검사가 이 문자열들을 보므로, 출력에서 빼는 건
        //     렌더링(JoinLine) 때만 한다.
        //   • 블록리스트: 조각 하나라도 막힌 태그면 그 후보 전체를 뺀다(implies:도 "그 태그를 뜻함"이라 포함).
        //   • 줄 내 중복: **출력** 조각 키가 전부 이미 나왔을 때만 뺀다(묶음에 새 조각이 있으면 남긴다).
        //     implies:만 있는 문자열은 그 키가 전부 이미 있을 때만 뺀다.
        // 반환값: 실제로 줄에 들어갔는가(통계 기록용).
        bool Add(string tag, SlotRole r)
        {
            if (tag.Contains("::", StringComparison.Ordinal)) tag = TagKey.ParsePickWeight(tag).Body;
            tag = WeightRange.Resolve(tag, rnd);
            var frags = TagKey.Split(tag);
            if (blocklist.Count > 0 && frags.Any(f => blocklist.Contains(f.Key))) return false;
            if (seenTags != null)
            {
                bool hasOutput = false, allSeen = true;
                foreach (var f in frags)
                {
                    if (f.IsImplied) continue;
                    hasOutput = true;
                    if (!seenTags.Contains(f.Key)) allSeen = false;
                }
                if (!hasOutput) allSeen = frags.All(f => seenImplied!.Contains(f.Key) || seenTags.Contains(f.Key));
                if (allSeen) return false;
                foreach (var f in frags) (f.IsImplied ? seenImplied! : seenTags).Add(f.Key);
            }
            emitted.Add((tag, r));
            if (lineKeys != null) foreach (var f in frags) lineKeys.Add(f.Key);
            return true;
        }
    }

    /// <summary>태그 목록을 출력 한 줄로 합친다. 출력 시점에만 '_'→공백 변환.
    /// <c>implies:</c> 조각과 추첨 가중치 접두는 여기서 뺀다(<see cref="TagKey.OutputText"/> — 둘 다 없는
    /// 문자열은 원문 그대로라 기존 출력은 한 글자도 안 바뀐다). 빼고 나서 남는 게 없는 문자열(implies:만 있던
    /// 후보)은 통째로 건너뛴다 — 안 그러면 ", ," 빈 칸이 생긴다. 원래부터 빈 문자열은 예전처럼 둔다.</summary>
    private static string JoinLine(List<string> tags, GenerationOptions options)
    {
        var outputs = new List<string>(tags.Count);
        foreach (var t in tags)
        {
            var o = TagKey.OutputText(t);
            if (o.Length == 0 && t.Length > 0) continue;
            outputs.Add(o);
        }
        return options.UnderscoreToSpace
            ? string.Join(", ", outputs.Select(ToDisplay))
            : string.Join(", ", outputs);
    }

    /// <summary>출력용 표기. '_'를 공백으로 바꾸되 <c>@_@</c>처럼 **글자가 없는 기호 태그**는
    /// 건드리지 않는다 — 바꾸면 "@ @"가 되어 원래 뜻(어질어질한 눈)을 잃는다.</summary>
    private static string ToDisplay(string tag) =>
        tag.Any(char.IsLetter) ? tag.Replace('_', ' ') : tag;

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
    internal static IReadOnlyList<string> ResolveCandidates(RandomPoolSlot slot, IReadOnlyDictionary<string, Pool> poolsById)
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

    /// <summary>랜덤 풀 슬롯 하나의 추첨(발동 여부 → 개수 → 후보). 실효 확률 리포트가 슬롯 단독의 명목
    /// 분포를 잴 때도 이 함수를 그대로 쓴다(재구현 금지).
    /// <para>후보 가중치 = 기본 가중치(균등이면 1, 빈도 가중이면 <see cref="SamplingWeights"/>) × 추첨 가중치
    /// 접두(<c>N::</c>, 없으면 1). 접두는 "이 풀 안에서 이 후보를 몇 배로 띄울지"라서, 어느 추첨 방식이든
    /// 배율로 곱한다. 0이면 절대 안 뽑힌다.</para>
    /// <para><paramref name="local"/>이 있으면 슬롯 단위 로컬 필터(<see cref="GenerationOptions.LocalConflictFiltering"/>):
    /// <paramref name="lineKeys"/>(앞쪽 슬롯에서 확정된 판정 키) + 이 슬롯에서 이미 뽑은 후보와 새로 충돌하는
    /// 후보를 그 회차에서 뺀다.</para>
    /// <para>접두도 필터도 없는 균등 추첨은 예전 <see cref="PickUniform"/>을, 빈도 가중은 예전과 같은 정수 룰렛을
    /// 그대로 탄다 — 난수 소비가 한 번도 달라지지 않아 기존 레시피는 같은 시드에서 같은 줄이 나온다.</para></summary>
    internal static IEnumerable<string> PickRandom(
        IReadOnlyList<string> candidates, RandomPoolSlot slot, IRandomSource rnd,
        GenerationOptions options, ITagLookup? tagInfo,
        LocalFilter? local = null, HashSet<string>? lineKeys = null)
    {
        // 발동 여부를 개수보다 먼저 굴린다. 100(기본값)이면 굴림 자체를 생략한다 — 난수를 한 번이라도
        // 더 소비하면 뒤따르는 모든 추첨이 밀려, chancePercent가 없는 기존 레시피가 같은 시드에서
        // 예전과 다른 줄을 내게 된다.
        if (slot.ChancePercent < 100 && rnd.Next(100) >= slot.ChancePercent)
            return Enumerable.Empty<string>();

        var pickWeights = PickWeightsOf(candidates);
        int pickable = pickWeights is null ? candidates.Count : pickWeights.Count(w => w > 0);

        int rangeWidth = slot.MaxCount - slot.MinCount + 1;
        int count = slot.MinCount + (rangeWidth > 1 ? rnd.Next(rangeWidth) : 0);
        count = Math.Min(count, pickable);

        // 빈도 메타데이터가 있을 때만 가중 추첨. 없으면 균등으로 폴백.
        bool byFrequency = options.Sampling == SamplingMode.Weighted && tagInfo is not null;
        bool filtering = local != null && lineKeys != null;
        if (!byFrequency && pickWeights is null && !filtering)
            return PickUniform(candidates, count, rnd);

        var weights = byFrequency ? SamplingWeights(candidates, tagInfo!) : Enumerable.Repeat(1, candidates.Count).ToArray();
        if (pickWeights != null)
            for (int i = 0; i < weights.Length; i++)
                weights[i] = (int)Math.Min(int.MaxValue, (long)weights[i] * pickWeights[i]);

        return PickRoulette(candidates, weights, count, slot.MinCount, rnd, filtering ? local : null, lineKeys);
    }

    /// <summary>후보별 추첨 가중치 접두(<c>N::</c>). 접두가 하나도 없으면 null(=전부 1, 기존 경로).</summary>
    internal static int[]? PickWeightsOf(IReadOnlyList<string> candidates)
    {
        int[]? result = null;
        for (int i = 0; i < candidates.Count; i++)
        {
            if (!candidates[i].Contains("::", StringComparison.Ordinal)) continue;
            var spec = TagKey.ParsePickWeight(candidates[i]);
            if (!spec.Explicit) continue;
            if (result is null)
            {
                result = new int[candidates.Count];
                Array.Fill(result, 1);
            }
            result[i] = spec.Error is null ? spec.Weight : 1;   // 잘못된 접두는 Validate가 이미 막는다
        }
        return result;
    }

    /// <summary>비복원 가중 룰렛(정수 누적 가중치 — IRandomSource가 정수만 주므로 결정적 테스트가 가능하다).
    /// 가중치 0 후보는 처음부터 뺀다.
    /// <para><paramref name="local"/>이 있으면 회차마다 "지금 줄 + 이 슬롯에서 이미 뽑은 것"과 새로 충돌하는 후보를
    /// 빼고 굴린다. 다 빠지면: 이미 MinCount를 채웠으면 거기서 멈추고, 못 채웠으면 그 회차만 필터 없이 굴려
    /// 줄 단위 재추첨에 맡긴다(폴백 — MinCount 보장이 필터보다 우선).</para></summary>
    private static List<string> PickRoulette(
        IReadOnlyList<string> candidates, int[] weightsIn, int count, int minCount, IRandomSource rnd,
        LocalFilter? local, HashSet<string>? lineKeys)
    {
        var remaining = new List<string>(candidates.Count);
        var weights = new List<int>(candidates.Count);
        for (int i = 0; i < candidates.Count; i++)
            if (weightsIn[i] > 0) { remaining.Add(candidates[i]); weights.Add(weightsIn[i]); }

        HashSet<string>? work = local != null ? new HashSet<string>(lineKeys!, StringComparer.Ordinal) : null;
        var eligible = local != null ? new List<int>(remaining.Count) : null;

        var picked = new List<string>(count);
        for (int k = 0; k < count && remaining.Count > 0; k++)
        {
            bool filtered = false;
            if (local != null)
            {
                eligible!.Clear();
                for (int j = 0; j < remaining.Count; j++)
                    if (local.Allows(work!, remaining[j])) eligible.Add(j);
                if (eligible.Count == 0 && picked.Count >= minCount) break;
                filtered = eligible.Count > 0 && eligible.Count < remaining.Count;
            }

            int count2 = filtered ? eligible!.Count : weights.Count;
            long total = 0;
            for (int j = 0; j < count2; j++) total += weights[filtered ? eligible![j] : j];

            // total은 int 범위를 넘을 수 있어(많은 후보) long으로 누적하되, 추첨 눈금은
            // IRandomSource(int) 한계 안에서 안전하게 정규화한다.
            int roll = total <= int.MaxValue ? rnd.Next((int)total) : rnd.Next(int.MaxValue);
            long target = total <= int.MaxValue ? roll : (long)((double)roll / int.MaxValue * total);

            int pos = 0;
            long acc = 0;
            for (; pos < count2; pos++)
            {
                acc += weights[filtered ? eligible![pos] : pos];
                if (target < acc) break;
            }
            if (pos >= count2) pos = count2 - 1; // 부동소수 경계 보호
            int idx = filtered ? eligible![pos] : pos;

            picked.Add(remaining[idx]);
            work?.UnionWith(local!.KeysOf(remaining[idx]));
            remaining.RemoveAt(idx);
            weights.RemoveAt(idx);
        }
        return picked;
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

    /// <summary>대안 그룹 하나를 가중치에 비례해 선택한다. <paramref name="avoid"/>가 주어지고
    /// (=바로 전 줄에서 이 슬롯이 뽑은 그룹) **뽑을 수 있는 그룹의 가중치가 전부 같으면**, 같은
    /// 그룹이 다시 나온 경우 최대 <see cref="MaxAvoidRepeatRetries"/>번만 다시 굴려 본다.
    /// 가중치가 서로 다르면(예: 샷 크기 3/2/1) 건드리지 않는다 — 무거운 쪽이 연달아 나오는 건
    /// 의도된 편중이라, 반복을 피하려고 재추첨하면 그 편중 자체가 무너진다(실측: 4:1 비중에서
    /// 재추첨을 걸었더니 80%가 나와야 할 쪽이 57%로 주저앉음, `WeightDistributionMatchesRatio
    /// OverManyLines` 회귀). "장면 도입"처럼 가중치가 다 같은(대개 전부 1) 슬롯에서만 의미가
    /// 있고, 그게 바로 이 기능이 노리는 대상이다. 무한 루프 위험은 없다 — for 루프로 하드
    /// 캡을 걸어 뒀고, 다 실패해도 마지막 결과를 그냥 받아들이고 끝낸다.</summary>
    private const int MaxAvoidRepeatRetries = 5;

    /// <param name="local">슬롯 단위 로컬 필터. 있으면 <paramref name="lineKeys"/>(앞쪽 슬롯에서 확정된 키)와
    /// 새로 충돌하는 그룹을 빼고 그중에서 고른다. 전부 걸러지면 원래 그룹 전체에서 고른다(폴백 — 줄 단위
    /// 재추첨이 마무리한다). 하나도 안 걸러지면 원래 목록 그대로라 난수 소비도 필터가 없을 때와 같다.</param>
    private static AlternativeGroup PickGroup(
        AlternativeSlot alt, IRandomSource rnd, AlternativeGroup? avoid = null,
        LocalFilter? local = null, HashSet<string>? lineKeys = null)
    {
        IReadOnlyList<AlternativeGroup> groups = alt.Groups;
        if (local != null && lineKeys != null)
        {
            var ok = alt.Groups.Where(g => g.Weight > 0 && local.Allows(lineKeys, g)).ToList();
            int viableCount = alt.Groups.Count(g => g.Weight > 0);
            if (ok.Count > 0 && ok.Count < viableCount) groups = ok;
        }

        var picked = PickGroupOnce(groups, rnd);
        if (avoid is null) return picked;

        var viable = groups.Where(g => g.Weight > 0).ToList();
        if (viable.Count <= 1) return picked; // 다른 선택지가 없으면 피할 방법도 없다.
        if (viable.Any(g => g.Weight != viable[0].Weight)) return picked; // 가중치가 다르면 편중을 존중.

        for (int tries = 0; ReferenceEquals(picked, avoid) && tries < MaxAvoidRepeatRetries; tries++)
            picked = PickGroupOnce(groups, rnd);

        return picked;
    }

    /// <summary>대안 그룹 하나를 가중치에 비례해 선택한다(반복 회피 없는 원본 로직). 모든
    /// 가중치가 기본값 1이면 기존과 똑같은 균등 추첨이 된다(하위 호환). 가중치 0인 그룹은
    /// 절대 뽑히지 않는다.</summary>
    private static AlternativeGroup PickGroupOnce(IReadOnlyList<AlternativeGroup> groups, IRandomSource rnd)
    {
        long total = 0;
        foreach (var g in groups) total += g.Weight;

        // Validate가 total>0을 보장하지만, Generate를 거치지 않는 호출 경로에서도 안전하도록
        // 방어적으로 균등 폴백을 둔다.
        if (total <= 0) return groups[rnd.Next(groups.Count)];

        int roll = total <= int.MaxValue ? rnd.Next((int)total) : rnd.Next(int.MaxValue);
        long target = total <= int.MaxValue ? roll : (long)((double)roll / int.MaxValue * total);

        long acc = 0;
        foreach (var g in groups)
        {
            acc += g.Weight;
            if (target < acc) return g;
        }
        return groups[^1]; // 부동소수 경계 보호
    }

    /// <summary>슬롯 단위 로컬 필터(<see cref="GenerationOptions.LocalConflictFiltering"/>)의 상태.
    /// Generate 호출 하나 동안만 살며, 후보·그룹별 판정 키 집합을 캐시한다 — 같은 후보를 줄마다
    /// 다시 분해하지 않으려고.</summary>
    internal sealed class LocalFilter
    {
        private readonly ConflictRules _rules;
        private readonly Dictionary<string, HashSet<string>> _candidateKeys = new(StringComparer.Ordinal);
        private readonly Dictionary<AlternativeGroup, HashSet<string>> _groupKeys = new(ReferenceEqualityComparer.Instance);

        public LocalFilter(ConflictRules rules) => _rules = rules;

        public HashSet<string> KeysOf(string candidate)
        {
            if (!_candidateKeys.TryGetValue(candidate, out var keys))
                _candidateKeys[candidate] = keys = TagKey.KeySet(new[] { candidate });
            return keys;
        }

        public HashSet<string> KeysOf(AlternativeGroup group)
        {
            if (!_groupKeys.TryGetValue(group, out var keys))
                _groupKeys[group] = keys = TagKey.KeySet(group.Tags);
            return keys;
        }

        public bool Allows(HashSet<string> lineKeys, string candidate) => !_rules.Introduces(lineKeys, KeysOf(candidate));
        public bool Allows(HashSet<string> lineKeys, AlternativeGroup group) => !_rules.Introduces(lineKeys, KeysOf(group));
    }

    /// <summary>후보 목록 전체의 추첨 가중치(같은 순서).
    /// <list type="bullet">
    /// <item>실존 태그 조각이 있는 후보: <see cref="WeightOf"/> — 조각들 중 최댓값.</item>
    /// <item>실존 태그 조각이 하나도 없는 후보(순수 자연어, 사전에 없는 태그, 빈도 0): 같은 후보
    /// 목록 안에서 빈도를 아는 후보들의 가중치 **중앙값**. 아는 후보가 하나도 없으면 1(=균등).</item>
    /// </list>
    /// 예전엔 모르는 후보를 무조건 1로 둬서, 빈도 수백~수천(가중치 수십)인 태그 옆의 자연어 후보가
    /// 사실상 안 뽑혔다(명목 40~80% → 실측 0.3~6%). 중앙값은 그 풀의 "보통 태그 하나"만큼의 몫을 주므로
    /// 풀의 빈도 규모(인기 태그 풀이든 희귀 태그 풀이든)와 무관하게 자연어 후보가 평균적인 후보처럼
    /// 경쟁한다. 절대 상수를 쓰지 않은 이유: 풀마다 빈도 규모가 수십 배씩 달라서 어떤 고정값도 어느
    /// 풀에선 굶기고 어느 풀에선 독식한다.</summary>
    internal static int[] SamplingWeights(IReadOnlyList<string> candidates, ITagLookup tagInfo)
    {
        var known = new int?[candidates.Count];
        var knownValues = new List<int>(candidates.Count);
        for (int i = 0; i < candidates.Count; i++)
        {
            known[i] = WeightOf(candidates[i], tagInfo);
            if (known[i] is int w) knownValues.Add(w);
        }

        int fallback = Median(knownValues);
        var result = new int[candidates.Count];
        for (int i = 0; i < candidates.Count; i++) result[i] = known[i] ?? fallback;
        return result;
    }

    /// <summary>후보 하나의 빈도 기반 가중치. 후보를 <see cref="TagKey"/>로 분해해 사전에 있고 빈도가
    /// 양수인 조각마다 max(1, round(sqrt(postCount)))를 구하고, 그중 **최댓값**을 대표로 쓴다.
    /// 그런 조각이 없으면 null(호출자가 풀 중앙값으로 채운다).
    /// <para>제곱근으로 누르는 이유: danbooru 빈도는 편차가 수천 배라 선형 가중 시 희귀 태그가
    /// 사실상 사라진다. 제곱근은 인기 태그를 우대하되 큐레이션 태그도 살려 둔다.</para>
    /// <para>최댓값인 이유: 묶음("(문장:1.1), bite_mark")은 동반 태그를 실어 나르려고 만든 것이라, 가장
    /// 알아보기 쉬운 태그가 그 후보의 렌더링 가능성을 대표한다. 동반 태그가 하나뿐인 대부분의 묶음은
    /// 그 태그를 단독으로 쓴 후보와 정확히 같은 가중치를 받는다. 가중치 표기(<c>(vaginal:1.2)</c>)는
    /// 렌더링 강조일 뿐 추첨 빈도와 무관하므로 반영하지 않는다(= <c>vaginal</c>과 같다).</para></summary>
    internal static int? WeightOf(string tag, ITagLookup tagInfo)
    {
        int? best = null;
        foreach (var t in TagKey.KnownTags(tag, tagInfo))
        {
            if (t.PostCount <= 0) continue;
            int w = Math.Max(1, (int)Math.Round(Math.Sqrt(t.PostCount)));
            if (best is null || w > best) best = w;
        }
        return best;
    }

    /// <summary>정수 중앙값(짝수 개면 가운데 둘의 평균, 반올림). 비어 있으면 1.</summary>
    private static int Median(List<int> values)
    {
        if (values.Count == 0) return 1;
        values.Sort();
        int mid = values.Count / 2;
        return values.Count % 2 == 1
            ? values[mid]
            : (int)Math.Round((values[mid - 1] + (long)values[mid]) / 2.0, MidpointRounding.AwayFromZero);
    }
}
