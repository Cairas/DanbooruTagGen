using DanbooruTagGen.Core.Generation;
using DanbooruTagGen.Core.Models;
using Xunit;

namespace DanbooruTagGen.Tests;

/// <summary>tools/visual_variety_scan.py의 slot_cardinality/MAJOR 조합 계산을 포팅한
/// VarietyAnalyzer. 파이썬 쪽과 같은 결과가 나오는지 대표 케이스로 고정한다.</summary>
public class VarietyAnalyzerTests
{
    private static IReadOnlyDictionary<string, Pool> NoPools() => new Dictionary<string, Pool>();

    [Fact]
    public void FixedSlotCardinalityIsAlwaysOne()
    {
        var slot = new FixedSlot { Label = "기본", Tags = { "1girl", "tentacles" } };
        Assert.Equal(1, VarietyAnalyzer.SlotCardinality(slot, NoPools()));
    }

    [Fact]
    public void AlternativeSlotCardinalityCountsOnlyLiveGroups()
    {
        // weight:0인 그룹은 절대 안 뽑히므로 가짓수에서 빠져야 한다.
        var slot = new AlternativeSlot { Label = "체위" };
        slot.Groups.Add(new AlternativeGroup { Tags = { "a" }, Weight = 1 });
        slot.Groups.Add(new AlternativeGroup { Tags = { "b" }, Weight = 2 });
        slot.Groups.Add(new AlternativeGroup { Tags = { "c" }, Weight = 0 });

        Assert.Equal(2, VarietyAnalyzer.SlotCardinality(slot, NoPools()));
    }

    [Fact]
    public void RandomPoolCardinalitySumsCombinationsAcrossMinMaxRange()
    {
        // 후보 4개, min1max2 → C(4,1)+C(4,2) = 4+6 = 10.
        var slot = new RandomPoolSlot { Label = "체위", MinCount = 1, MaxCount = 2 };
        slot.Tags.Add("a"); slot.Tags.Add("b"); slot.Tags.Add("c"); slot.Tags.Add("d");

        Assert.Equal(10, VarietyAnalyzer.SlotCardinality(slot, NoPools()));
    }

    [Fact]
    public void RandomPoolCardinalityMergesInlineTagsAndChainedPools()
    {
        var pools = new Dictionary<string, Pool>
        {
            ["p1"] = new Pool { Id = "p1", Candidates = { "x", "y" } },
        };
        var slot = new RandomPoolSlot { Label = "체위", MinCount = 1, MaxCount = 1, PoolId = "p1" };
        slot.Tags.Add("z");

        // 인라인 1개 + 풀 2개 = 후보 3개, min1max1 → C(3,1) = 3.
        Assert.Equal(3, VarietyAnalyzer.SlotCardinality(slot, pools));
    }

    [Fact]
    public void RandomPoolCardinalityClampsMaxCountToCandidateCount()
    {
        // 후보 2개인데 maxCount=5로 설정돼도 실제로는 2개까지만 뽑을 수 있다.
        var slot = new RandomPoolSlot { Label = "체위", MinCount = 0, MaxCount = 5 };
        slot.Tags.Add("a"); slot.Tags.Add("b");

        // k=0,1,2 → C(2,0)+C(2,1)+C(2,2) = 1+2+1 = 4.
        Assert.Equal(4, VarietyAnalyzer.SlotCardinality(slot, NoPools()));
    }

    [Fact]
    public void ComputeMajorCombinationsMultipliesOnlyMajorAxesAboveOne()
    {
        var slots = new List<Slot>
        {
            new FixedSlot { Label = "기본", Tags = { "1girl" } },                       // MAJOR지만 1이라 곱에 안 들어감
            new RandomPoolSlot { Label = "체위", MinCount = 1, MaxCount = 1,             // MAJOR, 4
                Tags = { "a", "b", "c", "d" } },
            new RandomPoolSlot { Label = "표정", MinCount = 1, MaxCount = 1,             // MINOR라 무시
                Tags = { "x", "y", "z" } },
            new RandomPoolSlot { Label = "구도", MinCount = 1, MaxCount = 1,             // COSMETIC이라 무시
                Tags = { "p", "q" } },
        };

        Assert.Equal(4, VarietyAnalyzer.ComputeMajorCombinations(slots, NoPools()));
    }

    [Fact]
    public void ComputeMajorCombinationsSkipsDisabledSlots()
    {
        var slots = new List<Slot>
        {
            new RandomPoolSlot { Label = "체위", MinCount = 1, MaxCount = 1, IsEnabled = false,
                Tags = { "a", "b", "c", "d" } },
        };
        Assert.Equal(1, VarietyAnalyzer.ComputeMajorCombinations(slots, NoPools()));
    }

    [Fact]
    public void ComputeMajorCombinationsFromRecipeMatchesSlotOverload()
    {
        var recipe = new Recipe
        {
            Slots =
            {
                new RandomPoolSlot { Label = "배경", MinCount = 1, MaxCount = 1, Tags = { "a", "b", "c" } },
            },
        };
        Assert.Equal(3, VarietyAnalyzer.ComputeMajorCombinations(recipe, NoPools()));
    }
}
