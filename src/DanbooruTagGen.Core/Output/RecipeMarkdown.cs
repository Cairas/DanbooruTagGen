using System.Text;
using DanbooruTagGen.Core.Generation;
using DanbooruTagGen.Core.Models;

namespace DanbooruTagGen.Core.Output;

/// <summary>레시피 구조를 사람이 읽을 마크다운으로(다른 작업 세션에 넘길 때 그대로 붙여 넣는 용도). 슬롯 순서대로
/// 고정 태그 · ALT 브랜치(가중치·태그) · 풀 후보(뽑는 개수·발동 확률)와, 있으면 실측 지표 한 단락.</summary>
public static class RecipeMarkdown
{
    /// <summary>풀 후보·브랜치 태그를 이보다 많이 나열하지 않는다(긴 풀은 "외 N개").</summary>
    public const int MaxListed = 40;

    public static string Export(Recipe recipe, IReadOnlyDictionary<string, Pool> poolsById, RecipeProfile? profile = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## {recipe.Name}");
        sb.AppendLine();
        sb.AppendLine($"- id: `{recipe.Id}`");
        if (recipe.Category.Length > 0) sb.AppendLine($"- 분류: {recipe.Category}");
        if (recipe.Labels.Count > 0) sb.AppendLine($"- 라벨: {string.Join(", ", recipe.Labels)}");
        int enabled = recipe.Slots.Count(s => s.IsEnabled);
        sb.AppendLine($"- 슬롯 {recipe.Slots.Count}개(켜짐 {enabled}) · 이론 조합 {RecipeProfileText.Combos(VarietyAnalyzer.ComputeAllCombinations(recipe.Slots, poolsById))}"
                      + $" · MAJOR 축 조합 {RecipeProfileText.Combos(VarietyAnalyzer.ComputeMajorCombinations(recipe, poolsById))}");
        if (profile is not null)
        {
            sb.AppendLine();
            sb.AppendLine("**실측**");
            foreach (var line in RecipeProfileText.Summary(profile).Split('\n')) sb.AppendLine($"- {line}");
        }
        sb.AppendLine();

        for (int i = 0; i < recipe.Slots.Count; i++)
        {
            var slot = recipe.Slots[i];
            var label = string.IsNullOrWhiteSpace(slot.Label) ? "(이름 없음)" : slot.Label;
            var off = slot.IsEnabled ? "" : " — ⏸ 꺼짐";
            switch (slot)
            {
                case FixedSlot f:
                    sb.AppendLine($"### {i + 1}. [고정] {label}{off}");
                    sb.AppendLine(Code(f.Tags));
                    break;
                case AlternativeSlot alt:
                {
                    long total = alt.Groups.Sum(g => (long)Math.Max(0, g.Weight));
                    sb.AppendLine($"### {i + 1}. [대안] {label} — 그룹 {alt.Groups.Count}개, 매 줄 하나{off}");
                    foreach (var g in alt.Groups)
                    {
                        var share = total > 0 ? $"{100.0 * Math.Max(0, g.Weight) / total:0.#}%" : "—";
                        sb.AppendLine($"- **{WildcardGenerator.GroupDisplayName(alt, g)}** (weight {g.Weight}, {share}, 태그 {g.Tags.Count}개): {Code(g.Tags)}");
                    }
                    break;
                }
                case RandomPoolSlot r:
                {
                    var candidates = WildcardGenerator.ResolveCandidates(r, poolsById);
                    var pools = new List<string>();
                    if (!string.IsNullOrEmpty(r.PoolId)) pools.Add(poolsById.TryGetValue(r.PoolId, out var p) ? p.Name : r.PoolId + "(없음)");
                    foreach (var extra in r.ExtraPoolIds) pools.Add(poolsById.TryGetValue(extra, out var ep) ? ep.Name : extra + "(없음)");
                    var count = r.MinCount == r.MaxCount ? $"{r.MinCount}개" : $"{r.MinCount}~{r.MaxCount}개";
                    sb.AppendLine($"### {i + 1}. [랜덤] {label} — 후보 {candidates.Count}개 중 {count}, 발동 {r.ChancePercent}%"
                                  + (pools.Count > 0 ? $", 풀: {string.Join(" + ", pools)}" : "") + off);
                    sb.AppendLine(Code(candidates));
                    break;
                }
            }
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string Code(IEnumerable<string> tags)
    {
        var list = tags.ToList();
        var shown = list.Take(MaxListed).Select(t => $"`{t.Replace("`", "'")}`");
        return string.Join(", ", shown) + (list.Count > MaxListed ? $" … 외 {list.Count - MaxListed}개" : "");
    }
}
