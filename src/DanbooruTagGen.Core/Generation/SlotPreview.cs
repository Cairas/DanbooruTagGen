using System.Globalization;
using System.Text;
using DanbooruTagGen.Core.Models;

namespace DanbooruTagGen.Core.Generation;

/// <summary>슬롯 하나만 따로 굴려 본 결과(편집 화면의 "이 슬롯만 굴려 보기"). 다른 슬롯·모순·중복 회피 없이 그 슬롯의 추첨만 —
/// 빈도 가중·N:: 배율·발동 확률·개수 범위는 실제 생성과 같은 코드(<see cref="WildcardGenerator.PickRandom"/>)를 탄다.</summary>
public static class SlotPreview
{
    public const int DefaultDraws = 4000;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Describe(Slot slot, IReadOnlyDictionary<string, Pool> poolsById, GenerationOptions options,
        ITagLookup? tagInfo = null, int draws = DefaultDraws, int? seed = null)
    {
        var sb = new StringBuilder();
        var label = string.IsNullOrWhiteSpace(slot.Label) ? "(이름 없음)" : slot.Label;
        switch (slot)
        {
            case FixedSlot f:
                sb.AppendLine($"[고정] {label} — 매 줄 항상: {string.Join(", ", f.Tags)}");
                break;

            case AlternativeSlot alt:
            {
                long total = alt.Groups.Sum(g => (long)Math.Max(0, g.Weight));
                sb.AppendLine($"[대안] {label} — 매 줄 그룹 하나(weight 비율). 실제 생성에선 앞쪽 슬롯과 모순인 그룹이 빠지거나(로컬 필터) 재추첨으로 줄어들 수 있습니다.");
                foreach (var g in alt.Groups.OrderByDescending(g => g.Weight))
                {
                    var p = total > 0 ? 100.0 * Math.Max(0, g.Weight) / total : 0;
                    sb.AppendLine($"  {p,6:0.0}%  {WildcardGenerator.GroupDisplayName(alt, g)} (weight {g.Weight}): {string.Join(", ", g.Tags)}");
                }
                break;
            }

            case RandomPoolSlot r:
            {
                var candidates = WildcardGenerator.ResolveCandidates(r, poolsById);
                var rnd = new SystemRandomSource(seed ?? options.Seed);
                var hits = new Dictionary<string, int>(StringComparer.Ordinal);
                var counts = new Dictionary<int, int>();
                var samples = new List<string>();
                for (int d = 0; d < draws; d++)
                {
                    var picks = WildcardGenerator.PickRandom(candidates, r, rnd, options, tagInfo);
                    counts[picks.Count] = counts.GetValueOrDefault(picks.Count) + 1;
                    foreach (var c in picks) hits[c] = hits.GetValueOrDefault(c) + 1;
                    if (samples.Count < 12) samples.Add(picks.Count == 0 ? "(안 나옴)" : string.Join(", ", picks));
                }
                var count = r.MinCount == r.MaxCount ? $"{r.MinCount}개" : $"{r.MinCount}~{r.MaxCount}개";
                sb.AppendLine($"[랜덤] {label} — 후보 {candidates.Count}개 중 {count}, 발동 {r.ChancePercent}%, "
                              + $"{(options.Sampling == SamplingMode.Weighted && tagInfo != null ? "빈도 가중" : "균등")} 추첨 · {draws:N0}번 굴림");
                sb.AppendLine("  뽑힌 개수: " + string.Join(" · ", counts.OrderBy(kv => kv.Key)
                    .Select(kv => $"{kv.Key}개 {(100.0 * kv.Value / draws).ToString("0.#", Inv)}%")));
                sb.AppendLine();
                sb.AppendLine("  한 줄에 나올 확률(높은 순):");
                foreach (var c in candidates.OrderByDescending(c => hits.GetValueOrDefault(c)).ThenBy(c => c, StringComparer.Ordinal))
                {
                    double p = 100.0 * hits.GetValueOrDefault(c) / draws;
                    sb.AppendLine($"  {p,6:0.0}%  {c}" + (p == 0 ? "  ⛔ 한 번도 안 나옴" : ""));
                }
                sb.AppendLine();
                sb.AppendLine("  예시:");
                foreach (var s in samples) sb.AppendLine("    " + s);
                break;
            }
        }
        if (!slot.IsEnabled) sb.AppendLine("⏸ 이 슬롯은 꺼져 있어 실제 생성에선 아무것도 안 냅니다.");
        return sb.ToString().TrimEnd();
    }
}
