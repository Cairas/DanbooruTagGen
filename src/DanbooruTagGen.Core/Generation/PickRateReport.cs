using DanbooruTagGen.Core.Models;

namespace DanbooruTagGen.Core.Generation;

/// <summary>최종 채택된 한 줄에서 실제로 뽑힌 것 하나. ALT면 <see cref="Group"/>, 랜덤 풀이면 <see cref="Candidate"/>
/// (<see cref="WildcardGenerator"/>의 후보 원문 — 추첨 가중치 접두 포함). <see cref="SlotIndex"/>는 생성에 쓴
/// 레시피(훅 적용 후)의 슬롯 번호.</summary>
internal readonly record struct PickRecord(int SlotIndex, AlternativeGroup? Group, string? Candidate);

/// <summary>최종 채택된 한 줄의 추첨 기록. Retries: 이 줄을 얻기까지 줄 전체를 다시 굴린 횟수.
/// HasLeftoverConflict: 재추첨 뒤에도 모순이 남았는가.</summary>
internal sealed record LineTrace(int LineIndex, IReadOnlyList<PickRecord> Picks, int Retries, bool HasLeftoverConflict);

/// <summary>실효 확률 리포트의 한 행. 확률은 전부 "한 줄에 이 항목이 나올 확률(%)".
/// <para><see cref="NominalPercent"/>: 이 슬롯만 따로 굴렸을 때의 확률 — ALT는 weight/Σweight, 랜덤 풀은
/// 발동 확률·개수 범위·가중치(빈도 가중·N:: 배율)를 그대로 탄 슬롯 단독 추첨을 많이 돌려 잰 값(같은
/// <see cref="WildcardGenerator.PickRandom"/> 코드). <see cref="ObservedPercent"/>: 실제 생성기(모순 회피·중복
/// 회피·줄 내 중복 제거·블록리스트까지 전부 켠 상태)로 뽑은 줄에서 잰 값. 둘의 차이가 "다른 슬롯 때문에 생긴
/// 왜곡"이다(n517처럼 브랜치가 줄 재추첨에 밀려 억제되는 사고를 숫자로 본다).</para>
/// <para><see cref="Weight"/>: ALT는 그룹 weight, 랜덤 풀은 실제 추첨에 쓰인 후보 가중치(기본 × N:: 배율).</para></summary>
public sealed record PickRateRow(
    int SlotIndex,
    string SlotLabel,
    string Kind,
    string Item,
    long Weight,
    double NominalPercent,
    double ObservedPercent)
{
    /// <summary>실측 − 명목(퍼센트포인트).</summary>
    public double Delta => ObservedPercent - NominalPercent;
}

/// <summary>리포트 전체. Rows는 |편차|가 큰 순.</summary>
public sealed record PickRateSummary(
    int Lines,
    double AverageRetries,
    double RerolledLinePercent,
    double LeftoverConflictLinePercent,
    IReadOnlyList<PickRateRow> Rows);

/// <summary>몬테카를로 "실효 확률" 리포트. 레시피 하나를 <b>실제</b> <see cref="WildcardGenerator"/>로 N줄 뽑아,
/// ALT 그룹 브랜치별·랜덤 풀 후보별로 명목 확률과 실측 등장률을 나란히 낸다.
/// <para>왜 생성기를 다시 구현하지 않나: 이 리포트의 목적이 "생성기가 실제로 이렇게 뽑는다"를 보는 것이라,
/// 재구현은 바로 그 차이(재추첨·중복 제거·로컬 필터 등)를 놓친다. 생성기는 채택된 줄의 추첨 기록만
/// 내보내고(<see cref="WildcardGenerator.GenerateCore"/>의 observer), 통계는 여기서 센다.</para>
/// <para>훅은 호출측이 미리 적용한 레시피를 넘긴다(<see cref="HookApplier"/> — 앱 생성 경로와 같다).</para></summary>
public static class PickRateReport
{
    /// <summary>랜덤 풀 슬롯 하나의 명목 분포를 잴 때 슬롯 단독으로 굴리는 횟수. 슬롯 하나만 굴리므로
    /// 줄 생성보다 훨씬 싸서, 명목 쪽 표본 오차를 실측보다 작게(±0.3%p 안팎) 잡을 수 있다.</summary>
    public const int NominalDraws = 40_000;

    public static PickRateSummary Run(
        Recipe recipe,
        IReadOnlyDictionary<string, Pool> poolsById,
        GenerationOptions options,
        ConflictRules? conflicts = null,
        ITagLookup? tagInfo = null,
        int nominalDraws = NominalDraws)
    {
        // 슬롯·후보별 실측 카운트: 줄당 한 번만 센다(같은 줄에 같은 후보가 두 번 나올 수 없지만, 방어적으로).
        var altCounts = new Dictionary<(int, AlternativeGroup), int>();
        var candCounts = new Dictionary<(int, string), int>();
        long retriesTotal = 0;
        int rerolled = 0, leftover = 0, lines = 0;

        void Observe(LineTrace t)
        {
            lines++;
            retriesTotal += t.Retries;
            if (t.Retries > 0) rerolled++;
            if (t.HasLeftoverConflict) leftover++;
            var seenAlt = new HashSet<(int, AlternativeGroup)>();
            var seenCand = new HashSet<(int, string)>();
            foreach (var p in t.Picks)
            {
                if (p.Group is { } g)
                {
                    if (seenAlt.Add((p.SlotIndex, g))) altCounts[(p.SlotIndex, g)] = altCounts.GetValueOrDefault((p.SlotIndex, g)) + 1;
                }
                else if (p.Candidate is { } c && seenCand.Add((p.SlotIndex, c)))
                    candCounts[(p.SlotIndex, c)] = candCounts.GetValueOrDefault((p.SlotIndex, c)) + 1;
            }
        }

        new WildcardGenerator().GenerateCore(recipe, poolsById, options, conflicts, tagInfo,
            progress: null, CancellationToken.None, Observe);

        var rows = new List<PickRateRow>();
        double Pct(int n) => lines == 0 ? 0 : 100.0 * n / lines;

        // 명목 분포용 난수는 생성 시드와 분리한다(같은 시드를 쓰면 리포트가 생성 결과를 흉내 낼 뿐이다).
        var nominalRnd = new SystemRandomSource(options.Seed is int s ? unchecked(s * 31 + 7) : null);

        for (int si = 0; si < recipe.Slots.Count; si++)
        {
            var slot = recipe.Slots[si];
            if (!slot.IsEnabled) continue;
            switch (slot)
            {
                case AlternativeSlot alt:
                {
                    long total = alt.Groups.Sum(g => (long)Math.Max(0, g.Weight));
                    for (int gi = 0; gi < alt.Groups.Count; gi++)
                    {
                        var g = alt.Groups[gi];
                        double nominal = total > 0 ? 100.0 * Math.Max(0, g.Weight) / total : 0;
                        var name = string.IsNullOrWhiteSpace(g.Label) ? $"(그룹 {gi + 1})" : g.Label;
                        rows.Add(new PickRateRow(si, slot.Label, "ALT", name, g.Weight, nominal,
                            Pct(altCounts.GetValueOrDefault((si, g)))));
                    }
                    break;
                }
                case RandomPoolSlot r:
                {
                    var candidates = WildcardGenerator.ResolveCandidates(r, poolsById);
                    var weights = EffectiveWeights(candidates, options, tagInfo);
                    var nominalHits = new Dictionary<string, int>(StringComparer.Ordinal);
                    // 슬롯 단독 추첨: 로컬 필터·줄 맥락 없이(=명목) 같은 PickRandom을 그대로 돌린다.
                    for (int d = 0; d < nominalDraws; d++)
                        foreach (var c in WildcardGenerator.PickRandom(candidates, r, nominalRnd, options, tagInfo))
                            nominalHits[c] = nominalHits.GetValueOrDefault(c) + 1;
                    for (int ci = 0; ci < candidates.Count; ci++)
                    {
                        var c = candidates[ci];
                        double nominal = nominalDraws == 0 ? 0 : 100.0 * nominalHits.GetValueOrDefault(c) / nominalDraws;
                        rows.Add(new PickRateRow(si, slot.Label, "POOL", c, weights[ci], nominal,
                            Pct(candCounts.GetValueOrDefault((si, c)))));
                    }
                    break;
                }
            }
        }

        rows.Sort((a, b) =>
        {
            int byDelta = Math.Abs(b.Delta).CompareTo(Math.Abs(a.Delta));
            return byDelta != 0 ? byDelta : a.SlotIndex.CompareTo(b.SlotIndex);
        });

        return new PickRateSummary(
            lines,
            lines == 0 ? 0 : (double)retriesTotal / lines,
            Pct(rerolled),
            Pct(leftover),
            rows);
    }

    /// <summary>후보별 실제 추첨 가중치(빈도 가중이면 <see cref="WildcardGenerator.SamplingWeights"/>, 아니면 1) × N:: 배율.
    /// 표시용 — 추첨 자체는 <see cref="WildcardGenerator.PickRandom"/>이 한다.</summary>
    private static long[] EffectiveWeights(IReadOnlyList<string> candidates, GenerationOptions options, ITagLookup? tagInfo)
    {
        var baseWeights = options.Sampling == SamplingMode.Weighted && tagInfo is not null
            ? WildcardGenerator.SamplingWeights(candidates, tagInfo)
            : Enumerable.Repeat(1, candidates.Count).ToArray();
        var pick = WildcardGenerator.PickWeightsOf(candidates);
        return baseWeights.Select((w, i) => (long)w * (pick?[i] ?? 1)).ToArray();
    }
}
