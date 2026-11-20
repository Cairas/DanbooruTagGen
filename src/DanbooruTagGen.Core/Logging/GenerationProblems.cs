using System.Globalization;
using System.Text;
using DanbooruTagGen.Core.Generation;
using DanbooruTagGen.Core.Models;
using DanbooruTagGen.Core.Output;
using DanbooruTagGen.Core.Tokenization;

namespace DanbooruTagGen.Core.Logging;

/// <summary>레시피 하나의 생성 결과와 그걸 판정할 재료. <see cref="OutputLines"/>는 실제로 쓰인 줄(품질 태그를 붙였으면 붙인 것) —
/// 길이는 이걸로 잰다. <see cref="Result"/>는 붙이기 전 원문(모순·출처·중복 판정용).</summary>
public sealed record GenerationProblemInput(
    Recipe Recipe,
    IReadOnlyDictionary<string, Pool> Pools,
    GenerationResult Result,
    int Requested,
    bool IsPreview,
    int Seed,
    ITokenCounter Counter,
    int TokenLimit,
    IReadOnlyList<string> OutputLines,
    DateTime At);

/// <summary>생성 결과에서 **문제가 있는 부분만** 잘라 로그 항목으로 만든다(컨셉 제작 문제 파악용). 문제가 없으면 빈 목록.
/// 레시피당 최대 네 항목: 조합 바닥(계산식) · 중복 줄 · 모순 남은 줄(원문·규칙·출처, 최대 <see cref="MaxConflictLines"/>줄) · 토큰 기준 초과(한 줄).</summary>
public static class GenerationProblems
{
    public const int MaxConflictLines = 10;
    /// <summary>항목 제목에 들어가는 문제 종류 이름(앱이 종류별로 세는 데 쓴다).</summary>
    public const string Exhausted = "조합 바닥";
    public const string Duplicates = "중복 줄";
    public const string Conflicts = "모순 남은 줄";
    public const string TooLong = "토큰 기준 초과";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static IReadOnlyList<LogEntry> From(GenerationProblemInput input)
    {
        var r = input.Result;
        var kind = input.IsPreview ? "미리보기" : "생성";
        var seed = $"시드 {input.Seed.ToString(Inv)}";
        var entries = new List<LogEntry>();
        LogEntry Entry(string title, string body) => new(input.At, LogKind.Concept, $"{kind} · {title} · {seed}", input.Recipe.Name, body);

        if (r.Exhausted)
        {
            bool all = r.Warnings.Any(w => w.Contains("모두 썼고", StringComparison.Ordinal));
            entries.Add(Entry(Exhausted,
                $"요청 {input.Requested}줄 → {r.Lines.Count}줄" + (all ? " (가능한 서로 다른 줄을 모두 씀)" : " (남은 조합을 더 찾지 못함)") + "\n"
                + Formula(input.Recipe, input.Pools)));
        }

        int distinct = r.LineKeys.Count > 0 ? r.LineKeys.Distinct(StringComparer.Ordinal).Count() : r.Lines.Count;
        int duplicates = r.Lines.Count - distinct;
        if (duplicates > 0)
            entries.Add(Entry(Duplicates, $"중복 줄 {duplicates}개가 들어감 — 서로 다른 줄 {distinct}개뿐(\"조합이 바닥나면 멈춤\"이 꺼져 있음)\n"
                                          + Formula(input.Recipe, input.Pools)));

        if (r.Conflicts.Count > 0)
        {
            var sb = new StringBuilder();
            foreach (var c in r.Conflicts.Take(MaxConflictLines))
            {
                var text = c.LineIndex < input.OutputLines.Count ? input.OutputLines[c.LineIndex] : r.Lines[c.LineIndex];
                sb.Append('#').Append(c.LineIndex + 1).Append(' ').Append(text).Append('\n');
                sb.Append("  걸린 규칙: ").Append(string.Join("; ", c.Hits.Select(h => $"{h.Label}({string.Join(" + ", h.Tags)})"))).Append('\n');
                if (r.Origins is { } origins && c.LineIndex < origins.Count)
                    foreach (var line in LineOriginText.Format(origins[c.LineIndex]).Split('\n'))
                        sb.Append("  ").Append(line).Append('\n');
            }
            if (r.Conflicts.Count > MaxConflictLines) sb.Append($"… 외 {r.Conflicts.Count - MaxConflictLines}줄\n");
            entries.Add(Entry($"{Conflicts} {r.Conflicts.Count}", sb.ToString().TrimEnd()));
        }

        if (input.TokenLimit > 0 && input.OutputLines.Count > 0)
        {
            var len = LineStats.Measure(input.OutputLines, input.Counter, input.TokenLimit);
            if (len.OverLimit > 0)
                entries.Add(Entry(TooLong,
                    $"{len.Model} 최대 {len.TokensMax}토큰 · 기준 {len.TokenLimit} 넘는 줄 {len.OverLimit}/{len.LineCount} · 가장 긴 줄 #{len.LongestLineIndex + 1}"
                    + (len.IsExact ? "" : " (추정)")));
        }
        return entries;
    }

    private static readonly string[] Kinds = { Exhausted, Duplicates, Conflicts, TooLong };

    /// <summary>항목의 문제 종류(<see cref="Exhausted"/> 등). 이 클래스가 만든 항목이 아니면 null.</summary>
    public static string? KindOf(LogEntry entry) =>
        Kinds.FirstOrDefault(k => entry.Title.Contains(k, StringComparison.Ordinal));

    /// <summary>"같은 문제"를 알아보는 열쇠: 문제 종류 · 레시피 id · 레시피 내용 지문 · 요청 줄 수 · 토큰 기준. 시드는 뺀다 —
    /// 같은 레시피를 다시 돌릴 때마다 같은 문제가 또 쌓이지 않게(<see cref="RepeatFilter"/>). 레시피를 고치면 다른 열쇠가 된다.</summary>
    public static string RepeatKey(GenerationProblemInput input, LogEntry entry)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(input.Recipe, Persistence.JsonStore.Options);
        var print = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(json)), 0, 8);
        return string.Join('|', KindOf(entry) ?? entry.Title, input.Recipe.Id, print,
            input.Requested.ToString(Inv), input.Counter.Model, input.TokenLimit.ToString(Inv));
    }

    /// <summary>"이론 조합 54 = 진행 단계 6 × 샷 크기 3 × 몸의 반응 3(3개 중 2개)" — 가짓수가 1보다 큰 켜진 슬롯만.</summary>
    public static string Formula(Recipe recipe, IReadOnlyDictionary<string, Pool> pools)
    {
        var parts = new List<string>();
        foreach (var slot in recipe.Slots)
        {
            if (!slot.IsEnabled) continue;
            long card = VarietyAnalyzer.SlotCardinality(slot, pools);
            if (card <= 1) continue;
            var label = string.IsNullOrWhiteSpace(slot.Label) ? "(이름 없음)" : slot.Label;
            var detail = slot is RandomPoolSlot p
                ? $"({WildcardGenerator.ResolveCandidates(p, pools).Count}개 중 {(p.MinCount == p.MaxCount ? $"{p.MinCount}개" : $"{p.MinCount}~{p.MaxCount}개")})"
                : "";
            parts.Add($"{label} {card.ToString(Inv)}{detail}");
        }
        var total = VarietyAnalyzer.ComputeAllCombinations(recipe.Slots, pools);
        return $"이론 조합 {RecipeProfileText.Combos(total)} = " + (parts.Count == 0 ? "1" : string.Join(" × ", parts));
    }
}
