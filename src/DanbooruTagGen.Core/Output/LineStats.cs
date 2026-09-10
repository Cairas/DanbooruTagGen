namespace DanbooruTagGen.Core.Output;

/// <summary>생성된 줄들의 길이 요약. <see cref="OverBudget"/>는 예산을 넘은 줄 수.</summary>
public sealed record LineLengthStats(int LineCount, int Min, int Max, double Average, int OverBudget, int Budget);

/// <summary>줄 길이(태그 개수) 점검.
///
/// 왜 필요한가: guide.md의 "줄 길이 예산"은 한 줄 24~27태그를 목표로 한다. 예산을 넘으면
/// 뒤쪽 태그가 밀려 반영이 약해지는데, 지금까지는 뽑은 줄을 눈으로 세어 보는 수밖에 없었다.</summary>
public static class LineStats
{
    /// <summary>guide.md의 목표 상한(24~27태그). 이보다 길면 뒤쪽 태그가 묻히기 시작한다.</summary>
    public const int DefaultBudget = 27;

    public static LineLengthStats Measure(IReadOnlyList<string> lines, int budget = DefaultBudget)
    {
        int count = 0, min = int.MaxValue, max = 0, over = 0;
        long total = 0;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;   // 파일 구분용 빈 줄은 세지 않는다
            int tags = CountTags(line);
            count++;
            total += tags;
            if (tags < min) min = tags;
            if (tags > max) max = tags;
            if (tags > budget) over++;
        }

        if (count == 0) return new LineLengthStats(0, 0, 0, 0, 0, budget);
        return new LineLengthStats(count, min, max, Math.Round((double)total / count, 1), over, budget);
    }

    /// <summary>한 줄의 태그 수. 괄호 안의 쉼표는 세지 않는다 — 자연어 문구가 쉼표를 품고
    /// 있어서("(she is dragged in, still in shock:1.2)") 그냥 쪼개면 개수가 부풀어 오른다.</summary>
    private static int CountTags(string line)
    {
        int depth = 0, tags = 0;
        bool sawContent = false;

        foreach (var c in line)
        {
            switch (c)
            {
                case '(': depth++; sawContent = true; break;
                case ')': if (depth > 0) depth--; break;
                case ',' when depth == 0:
                    if (sawContent) tags++;
                    sawContent = false;
                    break;
                default:
                    if (!char.IsWhiteSpace(c)) sawContent = true;
                    break;
            }
        }

        if (sawContent) tags++;
        return tags;
    }
}
