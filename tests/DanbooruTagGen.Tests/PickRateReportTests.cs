using DanbooruTagGen.Core.Generation;
using DanbooruTagGen.Core.Models;
using Xunit;

namespace DanbooruTagGen.Tests;

/// <summary>1-6: 몬테카를로 실효 확률 리포트. 실제 WildcardGenerator의 채택된 줄 기록으로 실측을 세고,
/// 명목은 ALT weight 비율 / 랜덤 풀 슬롯 단독 추첨으로 잰다.</summary>
public class PickRateReportTests
{
    private static Dictionary<string, Pool> NoPools() => new();

    private static Recipe Recipe() => new()
    {
        Slots =
        {
            new AlternativeSlot
            {
                Label = "포즈",
                Groups =
                {
                    new AlternativeGroup { Label = "풀넬슨", Tags = { "full_nelson" }, Weight = 1 },
                    new AlternativeGroup { Label = "서서", Tags = { "standing_sex" }, Weight = 2 },
                },
            },
            new RandomPoolSlot { Label = "손짓", Tags = { "grabbing_another's_breast", "hand_on_another's_hip", "2::smile" } },
        },
    };

    private static readonly ConflictRules Rules = new(new[]
    {
        new ConflictGroup("양손", new[] { "full_nelson", "grabbing_another's_breast", "hand_on_another's_hip" }),
    });

    private static PickRateRow Row(PickRateSummary s, string item) => s.Rows.Single(r => r.Item == item);

    [Fact]
    public void WithoutConflictsObservedMatchesNominal()
    {
        var s = PickRateReport.Run(Recipe(), NoPools(),
            new GenerationOptions { LineCount = 5000, Seed = 1, AvoidDuplicateLines = false }, conflicts: null);
        Assert.Equal(5000, s.Lines);
        Assert.Equal(33.33, Row(s, "풀넬슨").NominalPercent, 1);
        Assert.InRange(Row(s, "2::smile").NominalPercent, 48.5, 51.5);    // 2::smile = 2/4 (슬롯 단독 추첨)
        Assert.Equal(2, Row(s, "2::smile").Weight);
        Assert.All(s.Rows, r => Assert.InRange(Math.Abs(r.Delta), 0, 3.0));
        Assert.Equal(0, s.AverageRetries);
    }

    [Fact]
    public void RerollSuppressionShowsUpAsTheLargestDeviation()
    {
        var s = PickRateReport.Run(Recipe(), NoPools(),
            new GenerationOptions { LineCount = 5000, Seed = 2, AvoidDuplicateLines = false }, Rules);
        // 풀넬슨: 명목 33.3% → 실측 ≈ (⅓·½)/(⅓·½+⅔) = 20%. 손짓 smile은 풀넬슨 줄 때문에 오른다.
        var fn = Row(s, "풀넬슨");
        Assert.InRange(fn.ObservedPercent, 17, 23);
        Assert.True(Math.Abs(s.Rows[0].Delta) >= Math.Abs(s.Rows[^1].Delta));   // |편차| 큰 순 정렬
        Assert.True(s.RerolledLinePercent > 0);
        Assert.Equal(0, s.LeftoverConflictLinePercent);
    }

    [Fact]
    public void LocalFilteringRestoresTheNominalRate()
    {
        var s = PickRateReport.Run(Recipe(), NoPools(),
            new GenerationOptions { LineCount = 5000, Seed = 3, AvoidDuplicateLines = false, LocalConflictFiltering = true }, Rules);
        Assert.InRange(Row(s, "풀넬슨").Delta, -3, 3);
        Assert.Equal(0, s.AverageRetries);
    }

    [Fact]
    public void ChanceAndCountRangeAreInTheNominal()
    {
        var recipe = new Recipe
        {
            Slots = { new RandomPoolSlot { Label = "가끔", Tags = { "a", "b", "c", "d" }, MinCount = 1, MaxCount = 2, ChancePercent = 50 } },
        };
        var s = PickRateReport.Run(recipe, NoPools(), new GenerationOptions { LineCount = 3000, Seed = 4, AvoidDuplicateLines = false });
        // 발동 50% × 개수 평균 1.5 / 후보 4 = 18.75%
        Assert.All(s.Rows, r => Assert.InRange(r.NominalPercent, 17.5, 20));
    }
}
