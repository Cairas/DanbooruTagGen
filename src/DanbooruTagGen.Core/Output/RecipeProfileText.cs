using System.Globalization;
using System.Text;
using DanbooruTagGen.Core.Generation;

namespace DanbooruTagGen.Core.Output;

/// <summary><see cref="RecipeProfile"/>을 사람이 읽을 글로: 편집 화면 한두 줄 요약, 두 레시피 비교 리포트.</summary>
public static class RecipeProfileText
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Combos(long n) => n == long.MaxValue ? "매우 많음" : n.ToString("N0", Inv);

    public static string Effective(RecipeProfile p) =>
        p.EffectiveCombinations is { } e ? e.ToString("N0", Inv) : $"{p.SampleLines:N0}+";

    /// <summary>편집 화면용 요약(두 줄). 요청 줄 수를 다 못 채울 만큼 빠듯하면 ⚠를 붙인다.
    /// <paramref name="includeTokens"/>가 false면 토큰 부분을 뺀다(점검 리포트는 모델별 길이를 따로 한 줄로 보여 준다).</summary>
    public static string Summary(RecipeProfile p, bool includeTokens = true)
    {
        var line1 = $"조합: 이론 {Combos(p.TheoreticalCombinations)} · 표본 {p.SampleLines:N0}줄 중 서로 다른 줄 {p.DistinctInSample:N0} · 유효 {Effective(p)}";
        var line2 = $"{p.RequestedLines:N0}줄 생성 시: "
            + (p.DuplicateLines > 0 ? $"⚠ 중복 {p.DuplicateLines:N0}줄 · " : "")
            + $"재추첨 줄당 {p.AverageRetries.ToString("0.0#", Inv)}회"
            + (p.IsTight && p.DuplicateLines == 0 ? " ⚠ 여유 부족" : "")
            + (p.LeftoverConflictPercent > 0 ? $" · 모순 남은 줄 {p.LeftoverConflictPercent.ToString("0.#", Inv)}%" : "")
            + (includeTokens ? TokenPart(p.Length) : "");
        return line1 + "\n" + line2;
    }

    /// <summary>토큰 부분: 실제 토크나이저로 쟀으면 모델·기준·초과 줄 수까지, 아니면 예전 어림값 한 마디.</summary>
    public static string TokenPart(LineLengthStats l)
    {
        if (l.TokenLimit <= 0)
            return $" · 토큰 평균 {l.TokensAverage.ToString("0.#", Inv)}(최대 {l.TokensMax}, 품질 태그 제외)";
        var chunks = l.Model == Tokenization.ModelKind.IL ? $", {l.ChunksMax}청크" : "";
        return $" · {l.Model} 토큰 평균 {l.TokensAverage.ToString("0.#", Inv)}(최대 {l.TokensMax}{chunks}) · 기준 {l.TokenLimit} 넘는 줄 {l.OverLimit}/{l.LineCount}"
               + (l.IsExact ? "" : " (추정)") + " · 품질 태그 제외";
    }

    /// <summary>모델별 길이 한 줄(점검 리포트): "IL 평균 48 · 최대 97(2청크) · Anima 평균 55 · 최대 110".</summary>
    public static string ModelLengthsLine(IReadOnlyList<LineLengthStats> lengths) =>
        string.Join(" · ", lengths.Select(l =>
            $"{l.Model} 평균 {l.TokensAverage.ToString("0.#", Inv)} · 최대 {l.TokensMax}"
            + (l.Model == Tokenization.ModelKind.IL ? $"({l.ChunksMax}청크)" : "")
            + (l.IsExact ? "" : "(추정)")));

    /// <summary>두 레시피(보통 수정 전 A / 수정 후 B)를 같은 시드·같은 옵션으로 잰 결과 비교.</summary>
    public static string Compare(RecipeProfile a, string nameA, RecipeProfile b, string nameB, int? seed)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"A: {nameA}");
        sb.AppendLine($"B: {nameB}");
        sb.AppendLine($"같은 시드({(seed is int s ? s.ToString(Inv) : "자동")})·같은 생성 옵션. 분포는 중복 회피를 끈 {a.SampleLines:N0}줄 표본, 재추첨·중복은 {a.RequestedLines:N0}줄 실제 생성 기준.");
        sb.AppendLine();

        void Row(string label, string va, string vb) => sb.AppendLine($"{label,-22}{va,14}{vb,14}");
        Row("", "A", "B");
        Row("이론 조합 수", Combos(a.TheoreticalCombinations), Combos(b.TheoreticalCombinations));
        Row("표본 중 서로 다른 줄", a.DistinctInSample.ToString("N0", Inv), b.DistinctInSample.ToString("N0", Inv));
        Row("유효 조합 수", Effective(a), Effective(b));
        Row($"중복 줄({a.RequestedLines}줄 중)", a.DuplicateLines.ToString("N0", Inv), b.DuplicateLines.ToString("N0", Inv));
        Row("줄당 재추첨", a.AverageRetries.ToString("0.00", Inv), b.AverageRetries.ToString("0.00", Inv));
        Row("모순 남은 줄 %", a.LeftoverConflictPercent.ToString("0.0", Inv), b.LeftoverConflictPercent.ToString("0.0", Inv));
        Row("줄당 태그 평균/최대", $"{a.Length.Average.ToString("0.#", Inv)}/{a.Length.Max}", $"{b.Length.Average.ToString("0.#", Inv)}/{b.Length.Max}");
        Row("추정 토큰 평균/최대", $"{a.Length.TokensAverage.ToString("0.#", Inv)}/{a.Length.TokensMax}", $"{b.Length.TokensAverage.ToString("0.#", Inv)}/{b.Length.TokensMax}");
        Row("75토큰 넘는 줄", a.Length.MultiChunkLines.ToString("N0", Inv), b.Length.MultiChunkLines.ToString("N0", Inv));
        sb.AppendLine();

        // 항목별 등장률: 슬롯 라벨 + 종류 + 항목으로 맞춘다(슬롯 이름을 바꾸면 빠짐/추가로 보인다).
        var keyed = new Dictionary<(string, string, string), (double? A, double? B)>();
        foreach (var r in a.Items) keyed[(r.SlotLabel, r.Kind, r.Item)] = (r.Percent, null);
        foreach (var r in b.Items)
            keyed[(r.SlotLabel, r.Kind, r.Item)] = keyed.TryGetValue((r.SlotLabel, r.Kind, r.Item), out var v) ? (v.A, r.Percent) : (null, r.Percent);

        var rows = keyed
            .Select(kv => (Slot: kv.Key.Item1, Item: kv.Key.Item3, kv.Value.A, kv.Value.B,
                Delta: (kv.Value.B ?? 0) - (kv.Value.A ?? 0)))
            .Where(r => r.A is null || r.B is null || Math.Abs(r.Delta) >= 1.0)
            .OrderByDescending(r => r.A is null || r.B is null ? double.MaxValue : Math.Abs(r.Delta))
            .ThenBy(r => r.Slot, StringComparer.CurrentCulture)
            .ToList();

        sb.AppendLine(rows.Count == 0
            ? "항목별 등장률: 1%p 넘게 달라진 항목 없음."
            : $"항목별 등장률 변화(1%p 이상·추가·빠짐, {rows.Count}개):");
        string Pct(double? p) => p is { } v ? v.ToString("0.0", Inv) + "%" : "—";
        foreach (var r in rows.Take(80))
        {
            var tag = r.A is null ? "  [B에 추가]" : r.B is null ? "  [B에서 빠짐]" : $"  ({(r.Delta >= 0 ? "+" : "")}{r.Delta.ToString("0.0", Inv)})";
            sb.AppendLine($"  {Pct(r.A),7} → {Pct(r.B),-7} {r.Slot} / {Shorten(r.Item, 70)}{tag}");
        }
        if (rows.Count > 80) sb.AppendLine($"  … 외 {rows.Count - 80}개");
        return sb.ToString().TrimEnd();
    }

    private static string Shorten(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
