using DanbooruTagGen.Core.Generation;

namespace DanbooruTagGen.Core.Output;

/// <summary>줄 출처(<see cref="LineOrigin"/>)를 사람이 읽을 여러 줄 글로. 앱의 "줄 출처 보기" 패널이 쓴다.</summary>
public static class LineOriginText
{
    public static string Format(LineOrigin origin, IReadOnlyList<ConflictHit>? leftoverConflicts = null)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var s in origin.Slots)
        {
            var kind = s.Kind switch
            {
                SlotOriginKind.Fixed => "고정",
                SlotOriginKind.Pool => "랜덤",
                _ => "대안",
            };
            var label = string.IsNullOrWhiteSpace(s.SlotLabel) ? $"슬롯 {s.SlotIndex + 1}" : s.SlotLabel;
            if (s.GroupLabel is { } g) label += $" → {g}";
            var body = s.Emitted.Count > 0
                ? string.Join(", ", s.Emitted)
                : s.Kind == SlotOriginKind.Pool && s.Dropped.Count == 0 ? "(이번 줄엔 안 나옴)" : "(줄에 들어간 태그 없음)";
            sb.Append('[').Append(kind).Append("] ").Append(label).Append(" : ").AppendLine(body);
            if (s.FilteredOut.Count > 0)
                sb.Append("      ⊘ 앞쪽 태그와 모순이라 후보에서 뺌: ").AppendLine(string.Join(", ", s.FilteredOut));
            if (s.Dropped.Count > 0)
                sb.Append("      ⊘ 뽑혔지만 줄 내 중복·블록리스트로 빠짐: ").AppendLine(string.Join(", ", s.Dropped));
        }
        if (leftoverConflicts is { Count: > 0 })
            sb.Append("⚠ 남은 모순: ").AppendLine(string.Join("; ", leftoverConflicts.Select(h => $"{h.Label}({string.Join(" + ", h.Tags)})")));
        return sb.ToString().TrimEnd();
    }
}
