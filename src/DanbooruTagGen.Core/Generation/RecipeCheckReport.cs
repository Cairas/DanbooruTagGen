using System.Globalization;
using System.Text;
using DanbooruTagGen.Core.Models;
using DanbooruTagGen.Core.Output;

namespace DanbooruTagGen.Core.Generation;

/// <summary>레시피 점검 결과 숫자 요약(일괄 점검 표·판정용).</summary>
public sealed record RecipeCheckSummary(
    int ValidationIssues,
    int Advisories,
    int DefiniteConflicts,
    int PossibleConflicts,
    long MajorCombinations,
    RecipeProfile Profile,
    int SuppressedItems,
    int NeverAppearingItems)
{
    /// <summary>손봐야 할 것: 검증 지적(없는 태그·상대 없는 행위·문법)·항상 모순·요청 줄 수 빠듯함·한 번도 안 나오는 항목.
    /// "닻 없는 자연어"(<see cref="Advisories"/>)·"가끔 모순"·억제는 의도일 수 있어 참고로만 센다(단계 게이팅 팩은 가끔 모순이 수천 건이 정상).</summary>
    public int MustFix => ValidationIssues + DefiniteConflicts + (Profile.IsTight ? 1 : 0) + NeverAppearingItems;
}

/// <summary>레시피 하나를 한 번에 점검하는 리포트: 검증(없는 태그·상대 없는 행위·닻 없는 자연어·문법) → 모순 가능성(항상/가끔) →
/// 조합·길이(서로 다른 줄·유효 조합·중복·재추첨·토큰) → 명목 대비 실측이 크게 어긋나는 항목(억제·한 번도 안 나옴).
/// <para>왜: 지금까지는 tools/conflict_scan.py·visual_variety_scan.py·태그 실존 스캔·RecipeStats를 따로 돌려야 했고, 파이썬 스캐너가
/// 엔진과 판정이 갈라져(면제 규칙 누락) 오탐을 내기도 했다. 전부 엔진 코드(RecipeValidator·ConflictRules·VarietyAnalyzer·
/// RecipeProfiler·PickRateReport)를 그대로 부른다.</para></summary>
public static class RecipeCheckReport
{
    /// <summary>명목 대비 실측이 이만큼(%p) 넘게 어긋나면 "억제·왜곡"으로 보고한다.</summary>
    public const double SuppressionThreshold = 3.0;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <param name="deep">false면 등장률 분석(가장 무거운 단계)을 건너뛴다 — 전체 일괄 점검용.</param>
    public static (RecipeCheckSummary Summary, string Text) Build(
        Recipe recipe,
        IReadOnlyDictionary<string, Pool> poolsById,
        GenerationOptions options,
        ConflictRules conflicts,
        ITagLookup? tagInfo,
        RequiresRules? requires = null,
        IEnumerable<GenerationHook>? hooks = null,
        Func<string, IReadOnlyList<string>>? suggest = null,
        bool deep = true,
        int sampleLines = RecipeProfiler.DefaultSampleLines,
        Tokenization.ITokenCounter? il = null,
        Tokenization.ITokenCounter? anima = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"레시피: {recipe.Name}  (id={recipe.Id})");
        sb.AppendLine($"옵션: {options.LineCount}줄 · 시드 {(options.Seed is int s ? s.ToString(Inv) : "자동")} · 모순 회피 {OnOff(options.AvoidConflicts)}(로컬 필터 {OnOff(options.LocalConflictFiltering)})"
                      + $" · {(options.Sampling == SamplingMode.Weighted ? "빈도 가중" : "균등")} 추첨 · 규칙 {conflicts.Count:N0}개 · 조합·분포는 훅 제외");
        sb.AppendLine();

        // [1] 검증
        var all = RecipeValidator.Validate(recipe, poolsById, tagInfo, suggest, hooks, requires, conflicts);
        static bool Advisory(ValidationIssue i) => i.Rule is ValidationRule.AnchorlessPhrase or ValidationRule.UnusedMarker;
        var issues = all.Where(i => !Advisory(i)).ToList();
        var advisories = all.Where(Advisory).ToList();
        sb.AppendLine($"[1] 검증 — {(issues.Count == 0 ? "문제 없음" : $"{issues.Count}건")}"
                      + (advisories.Count > 0 ? $" · 참고 {advisories.Count}건(닻 없는 자연어·쓰이지 않는 implies 표식)" : ""));
        foreach (var i in issues) sb.AppendLine("  ⚠ " + i.Message);
        foreach (var i in advisories.Take(10)) sb.AppendLine("  · " + i.Message);
        if (advisories.Count > 10) sb.AppendLine($"  · … 외 {advisories.Count - 10}건");
        sb.AppendLine();

        // [2] 모순 가능성(슬롯 단위, MaxCount 반영 — 빌더·라이브러리 배지와 같은 기준)
        var hits = conflicts.Count == 0
            ? Array.Empty<RecipeConflict>()
            : conflicts.AnalyzeRecipe(SlotSpec.FromSlots(recipe.Slots, poolsById));
        int definite = hits.Count(h => h.Severity == ConflictSeverity.Definite);
        sb.AppendLine($"[2] 모순 가능성 — 항상 {definite} · 가끔 {hits.Count - definite}"
                      + (hits.Count > 0 ? "  (항상 = 고정 태그끼리라 매 줄 충돌, 가끔 = 풀 후보 조합에 따라)" : ""));
        foreach (var h in hits.OrderBy(h => h.Severity).Take(30))
            sb.AppendLine($"  [{(h.Severity == ConflictSeverity.Definite ? "항상" : "가끔")}] {h.Label}: {string.Join(" + ", h.Tags)}");
        if (hits.Count > 30) sb.AppendLine($"  … 외 {hits.Count - 30}건");
        sb.AppendLine();

        // [3] 조합·길이
        var models = new[] { il, anima }.Where(c => c != null).Select(c => c!).ToList();
        var profile = RecipeProfiler.Measure(recipe, poolsById, options, conflicts, tagInfo, sampleLines, alsoMeasure: models);
        long major = VarietyAnalyzer.ComputeMajorCombinations(recipe, poolsById);
        sb.AppendLine("[3] 조합·길이" + (profile.IsTight ? " — ⚠ 요청 줄 수를 서로 다른 줄로 채우기 빠듯함" : ""));
        foreach (var line in RecipeProfileText.Summary(profile, includeTokens: profile.ModelLengths.Count == 0).Split('\n')) sb.AppendLine("  " + line);
        if (profile.ModelLengths.Count > 0)
            sb.AppendLine("  토큰 — " + RecipeProfileText.ModelLengthsLine(profile.ModelLengths) + " (품질 태그 제외)");
        sb.AppendLine($"  MAJOR 축 조합 {RecipeProfileText.Combos(major)}" + (major < VarietyAnalyzer.Threshold ? $" ⚠ 기준 {VarietyAnalyzer.Threshold} 미만(매 줄이 비슷해 보일 수 있음)" : ""));
        sb.AppendLine();

        // [4] 명목 대비 실측
        int suppressed = 0, never = 0;
        if (deep)
        {
            var rate = PickRateReport.Run(recipe, poolsById,
                options with { LineCount = Math.Max(sampleLines, 1000), AvoidDuplicateLines = false, StopWhenExhausted = false, CaptureOrigins = false, ReservedLineKeys = null },
                conflicts, tagInfo, nominalDraws: 20_000);
            var bad = rate.Rows.Where(r => Math.Abs(r.Delta) >= SuppressionThreshold || (r.NominalPercent >= 1 && r.ObservedPercent == 0)).ToList();
            never = rate.Rows.Count(r => r.NominalPercent >= 1 && r.ObservedPercent == 0);
            suppressed = bad.Count(r => r.Delta <= -SuppressionThreshold);
            sb.AppendLine($"[4] 명목 대비 실측 — {(bad.Count == 0 ? $"{SuppressionThreshold:0}%p 넘게 어긋나는 항목 없음" : $"{bad.Count}개 (억제 {suppressed} · 한 번도 안 나옴 {never})")}"
                          + $"  · 줄 재추첨 평균 {rate.AverageRetries.ToString("0.00", Inv)}회, 모순 남은 줄 {rate.LeftoverConflictLinePercent.ToString("0.0", Inv)}% ({rate.Lines:N0}줄, 중복 회피 끔)");
            if (bad.Count > 0) sb.AppendLine($"  {"편차",7} {"명목%",7} {"실측%",7}  슬롯 / 항목");
            foreach (var r in bad.Take(25))
                sb.AppendLine($"  {r.Delta,7:+0.0;-0.0;0.0} {r.NominalPercent,7:0.0} {r.ObservedPercent,7:0.0}  {r.SlotLabel} / {Shorten(r.Item, 80)}"
                              + (r.NominalPercent >= 1 && r.ObservedPercent == 0 ? "  ⛔ 한 번도 안 나옴" : ""));
            if (bad.Count > 25) sb.AppendLine($"  … 외 {bad.Count - 25}개");
            sb.AppendLine();
        }

        var summary = new RecipeCheckSummary(issues.Count, advisories.Count, definite, hits.Count - definite, major, profile, suppressed, never);
        sb.Append(summary.MustFix == 0
            ? "판정: 손볼 것 없음" + (hits.Count - definite > 0 || suppressed > 0 ? " (가끔 모순·억제는 의도인지 확인)" : "")
            : $"판정: 손볼 것 {summary.MustFix}건 — 검증 {issues.Count} · 항상 모순 {definite} · 조합 빠듯 {(profile.IsTight ? 1 : 0)} · 안 나오는 항목 {never}");
        return (summary, sb.ToString());
    }

    private static string OnOff(bool b) => b ? "켬" : "끔";
    private static string Shorten(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
