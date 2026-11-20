using DanbooruTagGen.Core.Generation;
using DanbooruTagGen.Core.Models;
using DanbooruTagGen.Core.Output;
using Xunit;

namespace DanbooruTagGen.Tests;

/// <summary>편집 화면 실시간 지표·수정 전/후 비교가 쓰는 레시피 실측(<see cref="RecipeProfiler"/>).</summary>
public class RecipeProfilerTests
{
    private static Dictionary<string, Pool> NoPools() => new();

    /// <summary>서로 다른 줄이 정확히 6개(포즈 2 × 표정 3), 전부 균등.</summary>
    private static Recipe Tiny() => new()
    {
        Slots =
        {
            new FixedSlot { Label = "기본", Tags = { "1girl" } },
            new AlternativeSlot { Label = "포즈", Groups = { new AlternativeGroup { Label = "서서", Tags = { "standing" } }, new AlternativeGroup { Label = "앉아", Tags = { "sitting" } } } },
            new RandomPoolSlot { Label = "표정", Tags = { "smile", "blush", "crying" } },
        },
    };

    private static Recipe Wide() => new()
    {
        Slots =
        {
            new RandomPoolSlot { Label = "a", Tags = Enumerable.Range(0, 30).Select(i => $"a{i}").ToObservable() },
            new RandomPoolSlot { Label = "b", Tags = Enumerable.Range(0, 30).Select(i => $"b{i}").ToObservable() },
            new RandomPoolSlot { Label = "c", Tags = Enumerable.Range(0, 30).Select(i => $"c{i}").ToObservable() },
        },
    };

    private static GenerationOptions Opts(int lines = 200) => new() { LineCount = lines, Seed = 5 };

    [Fact]
    public void TinyRecipeIsReportedAsExhausted()
    {
        var p = RecipeProfiler.Measure(Tiny(), NoPools(), Opts());
        Assert.Equal(6, p.TheoreticalCombinations);
        Assert.Equal(6, p.DistinctInSample);
        Assert.InRange(p.EffectiveCombinations!.Value, 5.5, 6.5);   // 균등이면 유효 = 실제
        Assert.Equal(200, p.RequestedLines);
        Assert.Equal(194, p.DuplicateLines);
        Assert.True(p.IsTight);
        Assert.Contains("⚠ 중복 194줄", RecipeProfileText.Summary(p));
    }

    [Fact]
    public void WideRecipeHasRoomToSpare()
    {
        var p = RecipeProfiler.Measure(Wide(), NoPools(), Opts());
        Assert.Equal(27_000, p.TheoreticalCombinations);
        Assert.Equal(0, p.DuplicateLines);
        Assert.False(p.IsTight);
        Assert.DoesNotContain("⚠", RecipeProfileText.Summary(p));
    }

    [Fact]
    public void ItemRatesCoverEveryBranchAndCandidate()
    {
        var p = RecipeProfiler.Measure(Tiny(), NoPools(), Opts());
        var pose = p.Items.Where(i => i.SlotLabel == "포즈").ToList();
        Assert.Equal(new[] { "서서", "앉아" }, pose.Select(i => i.Item));
        Assert.InRange(pose.Sum(i => i.Percent), 99.9, 100.1);
        Assert.Equal(3, p.Items.Count(i => i.Kind == "POOL"));
        Assert.All(pose, i => Assert.InRange(i.Percent, 45, 55));
    }

    [Fact]
    public void WeightSkewLowersEffectiveCombinations()
    {
        var skewed = Tiny();
        ((AlternativeSlot)skewed.Slots[1]).Groups[0].Weight = 6;   // 6:1
        var p = RecipeProfiler.Measure(skewed, NoPools(), Opts());
        Assert.Equal(6, p.DistinctInSample);
        Assert.True(p.EffectiveCombinations < 5);   // 6:1이면 포즈 축이 사실상 1.3가지
    }

    [Fact]
    public void CompareListsAddedRemovedAndChangedItems()
    {
        var before = Tiny();
        var after = Tiny();
        var pool = (RandomPoolSlot)after.Slots[2];
        pool.Tags.Remove("crying");
        pool.Tags.Add("tears");
        pool.Tags.Add("open_mouth");
        var a = RecipeProfiler.Measure(before, NoPools(), Opts());
        var b = RecipeProfiler.Measure(after, NoPools(), Opts());
        var text = RecipeProfileText.Compare(a, "수정 전", b, "수정 후", seed: 5);
        Assert.Contains("A: 수정 전", text);
        Assert.Contains("이론 조합 수", text);
        Assert.Contains("표정 / crying  [B에서 빠짐]", text);
        Assert.Contains("표정 / tears  [B에 추가]", text);
        Assert.Contains("표정 / open_mouth  [B에 추가]", text);
    }

    [Fact]
    public void AllCombinationsMultipliesEverySlotAndSaturates()
    {
        Assert.Equal(6, VarietyAnalyzer.ComputeAllCombinations(Tiny().Slots, NoPools()));
        var huge = new Recipe();
        for (int i = 0; i < 30; i++)
            huge.Slots.Add(new RandomPoolSlot { Tags = Enumerable.Range(0, 40).Select(k => $"t{i}_{k}").ToObservable(), MinCount = 3, MaxCount = 3 });
        Assert.Equal(long.MaxValue, VarietyAnalyzer.ComputeAllCombinations(huge.Slots, NoPools()));
    }
}

internal static class ObservableExtensions
{
    public static System.Collections.ObjectModel.ObservableCollection<T> ToObservable<T>(this IEnumerable<T> items) => new(items);
}
