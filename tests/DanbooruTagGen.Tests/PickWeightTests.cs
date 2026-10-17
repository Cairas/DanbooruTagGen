using DanbooruTagGen.Core.Generation;
using DanbooruTagGen.Core.Models;
using Xunit;

namespace DanbooruTagGen.Tests;

/// <summary>1-9: 랜덤 풀 후보별 추첨 가중치 접두 <c>N::</c>. 접두가 없는 기존 후보는 가중치 1이라
/// 예전과 같은 경로(같은 시드 → 같은 줄)를 타야 한다.</summary>
public class PickWeightTests
{
    private static Dictionary<string, Pool> NoPools() => new();

    [Theory]
    [InlineData("3::vaginal", 3, "vaginal", true)]
    [InlineData("0::vaginal", 0, "vaginal", true)]
    [InlineData(" 12 :: (she turns, glancing back:1.1), looking_back", 12, "(she turns, glancing back:1.1), looking_back", true)]
    [InlineData("vaginal", 1, "vaginal", false)]
    [InlineData("a::b", 1, "a::b", false)]   // 숫자 접두가 아니면 문법이 아니다
    [InlineData("::b", 1, "::b", false)]
    public void ParsesPrefix(string raw, int weight, string body, bool isExplicit)
    {
        var spec = TagKey.ParsePickWeight(raw);
        Assert.Equal(weight, spec.Weight);
        Assert.Equal(body, spec.Body);
        Assert.Equal(isExplicit, spec.Explicit);
        Assert.Null(spec.Error);
    }

    [Theory]
    [InlineData("0.5::vaginal")]
    [InlineData("-1::vaginal")]
    public void NonIntegerPrefixIsAnError(string raw) => Assert.NotNull(TagKey.ParsePickWeight(raw).Error);

    [Fact]
    public void PrefixIsInvisibleToJudgmentKeys()
    {
        Assert.Equal(new[] { "vaginal" }, TagKey.Keys("3::(vaginal:1.2)"));
        Assert.Equal(new[] { "she_turns", "looking_back" }, TagKey.Keys("2::(she turns:1.1), looking_back"));
    }

    [Fact]
    public void PrefixNeverReachesOutput()
    {
        var recipe = new Recipe { Slots = { new RandomPoolSlot { Tags = { "5::smile" } } } };
        var line = new WildcardGenerator().GenerateLine(recipe, NoPools(), new GenerationOptions(), new FakeRandomSource(0));
        Assert.Equal("smile", line);
    }

    [Fact]
    public void WeightSkewsUniformPick()
    {
        // 3::a, b → 누적 가중치 [3, 4]. 눈금 0~2 = a, 3 = b.
        var slot = new RandomPoolSlot { Tags = { "3::a", "b" } };
        var recipe = new Recipe { Slots = { slot } };
        var gen = new WildcardGenerator();
        Assert.Equal("a", gen.GenerateLine(recipe, NoPools(), new GenerationOptions(), new FakeRandomSource(2)));
        Assert.Equal("b", gen.GenerateLine(recipe, NoPools(), new GenerationOptions(), new FakeRandomSource(3)));
    }

    [Fact]
    public void WeightZeroIsNeverPicked()
    {
        var recipe = new Recipe { Slots = { new RandomPoolSlot { Tags = { "0::a", "b" }, MinCount = 1, MaxCount = 2 } } };
        var result = new WildcardGenerator().Generate(recipe, NoPools(),
            new GenerationOptions { LineCount = 200, Seed = 1, AvoidDuplicateLines = false });
        Assert.All(result.Lines, l => Assert.Equal("b", l));
    }

    [Fact]
    public void WeightMultipliesFrequencyWeightToo()
    {
        // 빈도 가중: a=100(√=10), b=100(√=10). 접두 3::a → 30 vs 10 → a 75%.
        var info = new FakeTagLookup().Add("a", 100).Add("b", 100);
        var recipe = new Recipe { Slots = { new RandomPoolSlot { Tags = { "3::a", "b" } } } };
        var result = new WildcardGenerator().Generate(recipe, NoPools(),
            new GenerationOptions { LineCount = 4000, Seed = 7, AvoidDuplicateLines = false, Sampling = SamplingMode.Weighted },
            tagInfo: info);
        double share = result.Lines.Count(l => l == "a") / (double)result.Lines.Count;
        Assert.InRange(share, 0.72, 0.78);
    }

    [Fact]
    public void LegacyCandidatesKeepTheSameLinesForTheSameSeed()
    {
        // 접두가 없는 풀은 예전 PickUniform 경로를 그대로 탄다 — 같은 시드에서 접두 없는 레시피와
        // "모든 후보에 1::"을 붙인 레시피는 다른 경로(룰렛)를 타지만, 눈금이 같아 같은 줄이 나와야 한다.
        var plain = new Recipe { Slots = { new RandomPoolSlot { Tags = { "a", "b", "c", "d" }, MinCount = 1, MaxCount = 3 } } };
        var ones = new Recipe { Slots = { new RandomPoolSlot { Tags = { "1::a", "1::b", "1::c", "1::d" }, MinCount = 1, MaxCount = 3 } } };
        var opts = new GenerationOptions { LineCount = 50, Seed = 42, AvoidDuplicateLines = false };
        var gen = new WildcardGenerator();
        Assert.Equal(gen.Generate(plain, NoPools(), opts).Lines, gen.Generate(ones, NoPools(), opts).Lines);
    }

    [Fact]
    public void ValidateRejectsDecimalPrefix()
    {
        var recipe = new Recipe { Slots = { new RandomPoolSlot { Tags = { "0.5::a", "b" } } } };
        var ex = Assert.Throws<GenerationValidationException>(() => WildcardGenerator.Validate(recipe, NoPools()));
        Assert.Contains("정수", ex.Message);
    }

    [Fact]
    public void ValidateRejectsAllZeroWeightsAndCountsOnlyPickable()
    {
        var allZero = new Recipe { Slots = { new RandomPoolSlot { Tags = { "0::a", "0::b" } } } };
        Assert.Throws<GenerationValidationException>(() => WildcardGenerator.Validate(allZero, NoPools()));

        var tooFew = new Recipe { Slots = { new RandomPoolSlot { Tags = { "0::a", "b" }, MinCount = 2, MaxCount = 2 } } };
        Assert.Throws<GenerationValidationException>(() => WildcardGenerator.Validate(tooFew, NoPools()));
    }

    [Fact]
    public void ZeroWeightCandidateIsIgnoredBySlotAnalysis()
    {
        var rules = new ConflictRules(new[] { new ConflictGroup("체위", new[] { "missionary", "doggystyle" }) });
        var hits = rules.AnalyzeRecipe(new[]
        {
            new SlotSpec(new[] { "missionary" }, 1, IsFixed: true),
            new SlotSpec(new[] { "0::doggystyle", "smile" }, 1, IsFixed: false),
        });
        Assert.Empty(hits);
    }
}
