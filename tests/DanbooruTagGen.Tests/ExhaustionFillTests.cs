using DanbooruTagGen.Core.Generation;
using DanbooruTagGen.Core.Models;
using Xunit;

namespace DanbooruTagGen.Tests;

/// <summary>서로 다른 줄이 바닥난 뒤 남은 조합을 전부 펼쳐 정확히 채우기(CombinationEnumerator).</summary>
public class ExhaustionFillTests
{
    private static Dictionary<string, Pool> NoPools() => new();

    /// <summary>진행 10 × 샷 3(6:1:1 쏠림) × 반응 3개 중 2개(3) = 90종 — 무작위 재추첨만으로는 마지막 쏠린 조합을 못 뽑던 모양.</summary>
    private static Recipe Ninety() => new()
    {
        Slots =
        {
            new FixedSlot { Tags = { "1girl" } },
            new AlternativeSlot
            {
                Label = "진행",
                Groups = { new() { Label = "s0", Tags = { "s0" } }, new() { Label = "s1", Tags = { "s1" } }, new() { Label = "s2", Tags = { "s2" } },
                           new() { Label = "s3", Tags = { "s3" } }, new() { Label = "s4", Tags = { "s4" } }, new() { Label = "s5", Tags = { "s5" } },
                           new() { Label = "s6", Tags = { "s6" } }, new() { Label = "s7", Tags = { "s7" } }, new() { Label = "s8", Tags = { "s8" } },
                           new() { Label = "s9", Tags = { "s9" } } },
            },
            new AlternativeSlot
            {
                Label = "샷",
                Groups = { new() { Tags = { "cowboy_shot" }, Weight = 6 }, new() { Tags = { "close-up" }, Weight = 1 }, new() { Tags = { "full_body" }, Weight = 1 } },
            },
            new RandomPoolSlot { Label = "반응", Tags = { "trembling", "sweat", "blush" }, MinCount = 2, MaxCount = 2 },
        },
    };

    [Fact]
    public void StopFillsEveryRemainingCombinationExactly()
    {
        var result = new WildcardGenerator().Generate(Ninety(), NoPools(),
            new GenerationOptions { LineCount = 200, Seed = 3, StopWhenExhausted = true });
        Assert.Equal(90, result.Lines.Count);
        Assert.Equal(90, result.LineKeys.Distinct().Count());
        Assert.True(result.Exhausted);
        Assert.Contains(result.Warnings, w => w.Contains("가능한 서로 다른 줄을 모두 썼고"));
    }

    [Fact]
    public void WithoutStopTheFillComesFirstThenDuplicates()
    {
        var result = new WildcardGenerator().Generate(Ninety(), NoPools(),
            new GenerationOptions { LineCount = 120, Seed = 3, StopWhenExhausted = false });
        Assert.Equal(120, result.Lines.Count);
        Assert.Equal(90, result.LineKeys.Take(90).Distinct().Count());   // 앞 90줄이 서로 다른 줄 전부
        Assert.Contains(result.Warnings, w => w.Contains("중복될 수 있습니다"));
    }

    [Fact]
    public void FillSkipsCombinationsThatConflict()
    {
        var rules = new ConflictRules(new[] { new ConflictGroup("샷", new[] { "close-up", "s0" }) });
        var result = new WildcardGenerator().Generate(Ninety(), NoPools(),
            new GenerationOptions { LineCount = 200, Seed = 3, StopWhenExhausted = true, UnderscoreToSpace = false }, rules);
        Assert.Equal(87, result.Lines.Count);   // s0 + close-up 3종이 빠진다
        Assert.Empty(result.Conflicts);
    }

    [Fact]
    public void FillRecordsOriginsForFilledLines()
    {
        var result = new WildcardGenerator().Generate(Ninety(), NoPools(),
            new GenerationOptions { LineCount = 200, Seed = 3, StopWhenExhausted = true, CaptureOrigins = true, UnderscoreToSpace = false });
        Assert.All(Enumerable.Range(0, result.Lines.Count), i =>
            Assert.Equal(result.Lines[i], string.Join(", ", result.Origins![i].Slots.SelectMany(s => s.Emitted))));
    }

    [Fact]
    public void ChanceAndOptionalCountsAreEnumerated()
    {
        // 풀: 발동 50%, 0~1개 → "안 나옴" 1 + 후보 2 = 3가지, ALT 2 → 6종.
        var recipe = new Recipe
        {
            Slots =
            {
                new AlternativeSlot { Groups = { new() { Tags = { "a" } }, new() { Tags = { "b" } } } },
                new RandomPoolSlot { Tags = { "x", "y" }, MinCount = 0, MaxCount = 1, ChancePercent = 50 },
            },
        };
        var result = new WildcardGenerator().Generate(recipe, NoPools(),
            new GenerationOptions { LineCount = 50, Seed = 1, StopWhenExhausted = true });
        Assert.Equal(6, result.Lines.Count);
    }
}
