using DanbooruTagGen.Core.Models;
using DanbooruTagGen.Core.Output;

namespace DanbooruTagGen.Core.Generation;

/// <summary>한 항목(ALT 브랜치 하나 또는 풀 후보 하나)이 한 줄에 나오는 실측 비율(%).</summary>
public sealed record ItemRate(int SlotIndex, string SlotLabel, string Kind, string Item, double Percent);

/// <summary>레시피 하나의 "조합 여유·길이·분포" 실측. 편집 화면의 실시간 지표와 수정 전/후 비교가 같이 쓴다.
/// <para><see cref="TheoreticalCombinations"/>: 슬롯 가짓수의 곱(상한 — 모순·줄 내 중복으로 겹치는 조합을 빼지 않음).
/// <see cref="DistinctInSample"/>: 중복 회피를 끈 채 <see cref="SampleLines"/>줄을 뽑았을 때 나온 서로 다른 줄 수.
/// <see cref="EffectiveCombinations"/>: 가중치 쏠림을 반영한 "사실상 몇 가지처럼 도는가"(1/Σp², 표본의 같은 줄 쌍 비율로 추정 —
/// 같은 줄 쌍이 하나도 없으면 null = 표본보다 훨씬 많음).</para>
/// <para><see cref="RequestedLines"/>줄(생성 탭 설정 그대로) 실행에서: 줄당 평균 재추첨, 중복으로 들어간 줄 수, 모순이 남은 줄 비율.</para></summary>
public sealed record RecipeProfile(
    long TheoreticalCombinations,
    int SampleLines,
    int DistinctInSample,
    double? EffectiveCombinations,
    int RequestedLines,
    double AverageRetries,
    int DuplicateLines,
    double LeftoverConflictPercent,
    LineLengthStats Length,
    IReadOnlyList<ItemRate> Items)
{
    /// <summary>요청 줄 수를 서로 다른 줄로 다 채우기 어려운가: 중복이 실제로 들어갔거나, 재추첨이 줄당 2회를 넘는다
    /// (실측: 정상 레시피는 0~0.5회, 조합이 바닥난 레시피는 30회 이상 — 2026-11-20 전 레시피 측정).</summary>
    public bool IsTight => DuplicateLines > 0 || AverageRetries > 2;

    /// <summary>추가로 잰 모델별 길이(점검 리포트가 IL·Anima를 같이 보여 줄 때). 기준은 모델 기본값.</summary>
    public IReadOnlyList<LineLengthStats> ModelLengths { get; init; } = Array.Empty<LineLengthStats>();
}

public static class RecipeProfiler
{
    public const int DefaultSampleLines = 2000;

    /// <param name="options">생성 탭 설정. <see cref="GenerationOptions.LineCount"/>가 "요청 줄 수"다.</param>
    public static RecipeProfile Measure(
        Recipe recipe,
        IReadOnlyDictionary<string, Pool> poolsById,
        GenerationOptions options,
        ConflictRules? conflicts = null,
        ITagLookup? tagInfo = null,
        int sampleLines = DefaultSampleLines,
        CancellationToken cancellationToken = default,
        Tokenization.ITokenCounter? counter = null,
        int tokenLimit = 0,
        IReadOnlyList<Tokenization.ITokenCounter>? alsoMeasure = null)
    {
        var gen = new WildcardGenerator();
        // 멈춤 옵션은 끈다 — "요청 줄 수를 채우려면 몇 줄이 중복되는가"를 옵션과 무관하게 같은 뜻으로 재려고.
        var clean = options with { CaptureOrigins = false, ReservedLineKeys = null, StopWhenExhausted = false };

        // 1) 분포·조합 여유: 중복 회피를 끈 표본(중복 회피가 켜져 있으면 작은 레시피는 표본이 조합을 다 쓴 뒤 분포가 뒤틀린다).
        var altCounts = new Dictionary<(int, AlternativeGroup), int>();
        var candCounts = new Dictionary<(int, string), int>();
        void Observe(LineTrace t)
        {
            var seenAlt = new HashSet<(int, AlternativeGroup)>();
            var seenCand = new HashSet<(int, string)>();
            foreach (var p in t.Picks)
            {
                if (p.Group is { } g) { if (seenAlt.Add((p.SlotIndex, g))) altCounts[(p.SlotIndex, g)] = altCounts.GetValueOrDefault((p.SlotIndex, g)) + 1; }
                else if (p.Candidate is { } c && seenCand.Add((p.SlotIndex, c))) candCounts[(p.SlotIndex, c)] = candCounts.GetValueOrDefault((p.SlotIndex, c)) + 1;
            }
        }
        var sample = gen.GenerateCore(recipe, poolsById, clean with { LineCount = sampleLines, AvoidDuplicateLines = false },
            conflicts, tagInfo, progress: null, cancellationToken, Observe);

        var keyCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var k in sample.LineKeys) keyCounts[k] = keyCounts.GetValueOrDefault(k) + 1;
        double pairs = keyCounts.Values.Sum(c => c * (c - 1) / 2.0);
        double n = sample.Lines.Count;
        double? effective = pairs == 0 ? null : n * (n - 1) / 2.0 / pairs;

        var items = new List<ItemRate>();
        double Pct(int c) => n == 0 ? 0 : 100.0 * c / n;
        for (int si = 0; si < recipe.Slots.Count; si++)
        {
            var slot = recipe.Slots[si];
            if (!slot.IsEnabled) continue;
            switch (slot)
            {
                case AlternativeSlot alt:
                    foreach (var g in alt.Groups)
                        items.Add(new ItemRate(si, slot.Label, "ALT", WildcardGenerator.GroupDisplayName(alt, g), Pct(altCounts.GetValueOrDefault((si, g)))));
                    break;
                case RandomPoolSlot r:
                    foreach (var c in WildcardGenerator.ResolveCandidates(r, poolsById))
                        items.Add(new ItemRate(si, slot.Label, "POOL", c, Pct(candCounts.GetValueOrDefault((si, c)))));
                    break;
            }
        }

        // 2) 요청 줄 수 그대로(중복 회피 설정 포함) 한 번: 재추첨·중복·남은 모순.
        long retries = 0;
        int leftover = 0;
        var requested = gen.GenerateCore(recipe, poolsById, clean, conflicts, tagInfo, progress: null, cancellationToken, t =>
        {
            retries += t.Retries;
            if (t.HasLeftoverConflict) leftover++;
        });
        int reqLines = requested.Lines.Count;
        int duplicates = reqLines - requested.LineKeys.Distinct(StringComparer.Ordinal).Count();

        return new RecipeProfile(
            VarietyAnalyzer.ComputeAllCombinations(recipe.Slots, poolsById),
            sample.Lines.Count,
            keyCounts.Count,
            effective,
            reqLines,
            reqLines == 0 ? 0 : (double)retries / reqLines,
            duplicates,
            reqLines == 0 ? 0 : 100.0 * leftover / reqLines,
            counter is null
                ? LineStats.Measure(sample.Lines, lookup: tagInfo)
                : LineStats.Measure(sample.Lines, counter, tokenLimit > 0 ? tokenLimit : Tokenization.TokenCounters.DefaultLimit(counter.Model), tagInfo),
            items)
        {
            ModelLengths = alsoMeasure?.Select(c => LineStats.Measure(sample.Lines, c, Tokenization.TokenCounters.DefaultLimit(c.Model), tagInfo)).ToList()
                           ?? (IReadOnlyList<LineLengthStats>)Array.Empty<LineLengthStats>(),
        };
    }
}
