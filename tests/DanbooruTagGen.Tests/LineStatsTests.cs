using DanbooruTagGen.Core.Output;
using Xunit;

namespace DanbooruTagGen.Tests;

/// <summary>줄 길이 예산 점검(guide.md: 한 줄 24~27태그 목표). 예산을 넘으면 뒤쪽 태그가
/// 밀려 반영이 약해지는데, 지금까지는 눈으로 세어 보는 수밖에 없었다.</summary>
public class LineStatsTests
{
    [Fact]
    public void CountsCommaSeparatedTagsPerLine()
    {
        var stats = LineStats.Measure(new[] { "a, b, c", "a, b" });

        Assert.Equal(2, stats.Min);
        Assert.Equal(3, stats.Max);
        Assert.Equal(2.5, stats.Average);
    }

    [Fact]
    public void CountsLinesOverTheBudget()
    {
        var stats = LineStats.Measure(new[] { "a, b, c", "a, b, c, d", "a" }, budget: 3);

        Assert.Equal(1, stats.OverBudget);
    }

    [Fact]
    public void IgnoresBlankSeparatorLines()
    {
        var stats = LineStats.Measure(new[] { "a, b", "", "   " });

        Assert.Equal(1, stats.LineCount);
        Assert.Equal(2, stats.Max);
    }

    [Fact]
    public void EmptyInputMeasuresNothing()
    {
        var stats = LineStats.Measure(Array.Empty<string>());

        Assert.Equal(0, stats.LineCount);
        Assert.Equal(0, stats.Max);
        Assert.Equal(0, stats.Average);
    }

    [Fact]
    public void ToleratesTagsThatContainCommasInsideParentheses()
    {
        // 자연어 문구는 쉼표를 품는다("she is dragged in, still in shock").
        // 그런 줄에서 태그 수가 부풀지 않도록 괄호 안의 쉼표는 세지 않는다.
        var stats = LineStats.Measure(new[] { "1girl, (she is dragged in, still in shock:1.2), rape" });

        Assert.Equal(3, stats.Max);
    }

    // ── 1-8: 토큰 예산 ─────────────────────────────────────────

    [Fact]
    public void EstimatesClipTokensFromWordsAndPunctuation()
    {
        // 단어 13개(1girl은 한 덩어리로 셈) → round(1.3×13)=17, 구두점: 항목 구분 쉼표 3 + 문장 속 쉼표 1 = 4 → 21.
        // 강조 문법의 괄호와 ":1.1"은 세지 않는다.
        Assert.Equal(21, LineStats.EstimateClipTokens("1girl, long hair, looking at viewer, (she turns, glancing back over her shoulder:1.1)"));
        Assert.Equal(0, LineStats.EstimateClipTokens("   "));
    }

    [Fact]
    public void CountsChunksOf75Tokens()
    {
        Assert.Equal(0, LineStats.ChunksOf(0));
        Assert.Equal(1, LineStats.ChunksOf(75));
        Assert.Equal(2, LineStats.ChunksOf(76));
    }

    [Fact]
    public void ReportsPhraseWordsAndTokenBudget()
    {
        var longSentence = "(" + string.Join(" ", Enumerable.Repeat("word", 70)) + ":1.1)";
        var stats = LineStats.Measure(new[] { "1girl, smile", "1girl, " + longSentence });

        Assert.Equal(35, stats.PhraseWordsAverage);   // (0 + 70) / 2
        Assert.Equal(70, stats.PhraseWordsMax);
        Assert.Equal(2, stats.ChunksMax);             // 1.3×71 + 1 ≈ 93토큰 → 2청크
        Assert.Equal(1, stats.MultiChunkLines);
    }

    [Fact]
    public void LookupSeparatesMultiWordTagsFromPhrases()
    {
        // 출력 줄은 '_'→공백 변환을 거친다. 사전이 있으면 "hand on another's head"(4단어 태그)는 태그로 본다.
        var lookup = new FakeTagLookup().Add("hand_on_another's_head", 1);
        var line = new[] { "hand on another's head, pink smoke" };

        var withLookup = LineStats.Measure(line, lookup: lookup);
        Assert.Equal(2, withLookup.PhraseWordsMax);     // "pink smoke"만 자연어

        var without = LineStats.Measure(line);
        Assert.Equal(0, without.PhraseWordsMax);        // 사전 없으면 5단어 미만은 태그로 근사
    }
}
