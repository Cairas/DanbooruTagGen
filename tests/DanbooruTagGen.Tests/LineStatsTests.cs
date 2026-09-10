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
}
