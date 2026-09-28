using System.Globalization;
using System.Text.RegularExpressions;
using DanbooruTagGen.Core.Generation;
using DanbooruTagGen.Core.Models;
using Xunit;

namespace DanbooruTagGen.Tests;

/// <summary>가중치 범위 표기 (tag:LOW~HIGH) — 매 줄 범위 안에서 새로 뽑힌다.</summary>
public class WeightRangeTests
{
    private static Dictionary<string, Pool> Pools(params Pool[] p) => p.ToDictionary(x => x.Id);

    private static Recipe FixedRecipe(params string[] tags)
    {
        var slot = new FixedSlot { Label = "기본" };
        foreach (var t in tags) slot.Tags.Add(t);
        return new Recipe { Slots = { slot } };
    }

    private static readonly Regex WeightOf = new(@"\(wide_hips:(?<w>[0-9.]+)\)");

    private static double ExtractWeight(string line)
    {
        var m = WeightOf.Match(line);
        Assert.True(m.Success, $"범위가 치환되지 않음: {line}");
        return double.Parse(m.Groups["w"].Value, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void SingleWeightIsUntouched()
    {
        var gen = new WildcardGenerator();
        var line = gen.GenerateLine(FixedRecipe("(wide_hips:1.3)", "(a large dragon tattoo:1.4), dragon_tattoo"),
            Pools(), new GenerationOptions { UnderscoreToSpace = false }, new FakeRandomSource());
        Assert.Equal("(wide_hips:1.3), (a large dragon tattoo:1.4), dragon_tattoo", line);
    }

    /// <summary>범위가 없는 레시피는 난수를 하나도 더 소비하지 않아야 한다 — 그래야 기존
    /// 레시피가 같은 시드에서 예전과 완전히 같은 줄을 낸다.</summary>
    [Fact]
    public void SingleWeightConsumesNoRandomness()
    {
        var rnd = new CountingRandom();
        Assert.Equal("(wide_hips:1.3)", WeightRange.Resolve("(wide_hips:1.3)", rnd));
        Assert.Equal("top-down_bottom-up", WeightRange.Resolve("top-down_bottom-up", rnd));
        Assert.Equal(0, rnd.Calls);
    }

    [Theory]
    [InlineData(0, "(wide_hips:1.1)")]
    [InlineData(10, "(wide_hips:1.2)")]   // 끝자리 0 제거
    [InlineData(13, "(wide_hips:1.23)")]
    [InlineData(20, "(wide_hips:1.3)")]
    public void RangeMapsIntegerRollToTwoDecimals(int roll, string expected)
    {
        Assert.Equal(expected, WeightRange.Resolve("(wide_hips:1.1~1.3)", new FakeRandomSource(roll)));
    }

    [Fact]
    public void RangeAlwaysStaysWithinBounds()
    {
        var gen = new WildcardGenerator();
        var result = gen.Generate(FixedRecipe("(wide_hips:1.1~1.3)"), Pools(),
            new GenerationOptions { LineCount = 200, Seed = 7, UnderscoreToSpace = false, AvoidDuplicateLines = false });
        foreach (var line in result.Lines)
        {
            var w = ExtractWeight(line);
            Assert.InRange(w, 1.1, 1.3);
        }
    }

    [Fact]
    public void EachParenGroupInOneStringIsResolvedIndependently()
    {
        var s = WeightRange.Resolve("(a large dragon tattoo:1.2~1.4), (piledriver_(sex):0.8~1.0), dragon_tattoo",
            new FakeRandomSource(5, 20));
        Assert.Equal("(a large dragon tattoo:1.25), (piledriver_(sex):1), dragon_tattoo", s);
    }

    [Fact]
    public void SameSeedReproducesIdenticalLines()
    {
        var pool = new Pool { Id = "p", Candidates = { "smile", "grin", "blush", "(open_mouth:1.0~1.5)" } };
        var recipe = new Recipe
        {
            Slots =
            {
                new FixedSlot { Label = "기본", Tags = { "1girl", "(wide_hips:1.1~1.3)" } },
                new RandomPoolSlot { Label = "표정", PoolId = "p", MinCount = 1, MaxCount = 2 },
                new AlternativeSlot
                {
                    Label = "자세",
                    Groups =
                    {
                        new AlternativeGroup { Label = "A", Tags = { "(standing:0.9~1.2)" } },
                        new AlternativeGroup { Label = "B", Tags = { "sitting" } },
                    },
                },
            },
        };
        var gen = new WildcardGenerator();
        var opts = new GenerationOptions { LineCount = 50, Seed = 42 };
        var r1 = gen.Generate(recipe, Pools(pool), opts);
        var r2 = gen.Generate(recipe, Pools(pool), opts);
        Assert.Equal(r1.Lines, r2.Lines);
    }

    [Fact]
    public void LinesInOneBatchDrawDifferentValues()
    {
        var gen = new WildcardGenerator();
        var result = gen.Generate(FixedRecipe("(wide_hips:1.1~1.3)"), Pools(),
            new GenerationOptions { LineCount = 50, Seed = 3, UnderscoreToSpace = false, AvoidDuplicateLines = false });
        var distinct = result.Lines.Select(ExtractWeight).Distinct().Count();
        Assert.True(distinct > 5, $"50줄에서 서로 다른 값이 {distinct}개뿐 — 한 번 뽑고 고정된 듯");
    }

    [Theory]
    [InlineData("(wide_hips:1.3~1.1)")]
    [InlineData("(wide_hips:a~1.3)")]
    [InlineData("(wide_hips:~1.3)")]
    [InlineData("(a large dragon tattoo:1.4), (wide_hips:1.5~1.2)")]
    public void InvalidRangeFailsValidation(string tag)
    {
        var ex = Assert.Throws<GenerationValidationException>(() => WildcardGenerator.Validate(FixedRecipe(tag), Pools()));
        Assert.Contains("기본", ex.Message);
    }

    [Fact]
    public void InvalidRangeInPoolOrAlternativeFailsValidation()
    {
        var pool = new Pool { Id = "p", Candidates = { "smile", "(grin:1.3~1.1)" } };
        var viaPool = new Recipe { Slots = { new RandomPoolSlot { Label = "표정", PoolId = "p", MinCount = 1, MaxCount = 1 } } };
        Assert.Throws<GenerationValidationException>(() => WildcardGenerator.Validate(viaPool, Pools(pool)));

        var viaAlt = new Recipe
        {
            Slots =
            {
                new AlternativeSlot
                {
                    Label = "자세",
                    Groups = { new AlternativeGroup { Label = "A", Tags = { "(standing:2~1)" } } },
                },
            },
        };
        Assert.Throws<GenerationValidationException>(() => WildcardGenerator.Validate(viaAlt, Pools()));
    }

    [Fact]
    public void EqualBoundsAreValid()
    {
        WildcardGenerator.Validate(FixedRecipe("(wide_hips:1.2~1.2)"), Pools()); // no throw
        Assert.Equal("(wide_hips:1.2)", WeightRange.Resolve("(wide_hips:1.2~1.2)", new FakeRandomSource()));
    }

    /// <summary>범위 태그도 태그 존재 점검에서는 태그명만 보여야 한다(오탐 방지).</summary>
    [Fact]
    public void RecipeValidatorUnwrapsRangeSyntax()
    {
        var lookup = new FakeTagLookup().Add("wide_hips", 1000);
        var issues = RecipeValidator.Validate(FixedRecipe("(wide_hips:1.1~1.3)"), Pools(), lookup);
        Assert.DoesNotContain(issues, i => i.Rule == ValidationRule.UnknownTag);
    }

    private sealed class CountingRandom : IRandomSource
    {
        public int Calls { get; private set; }
        public int Next(int maxExclusive) { Calls++; return 0; }
    }
}
