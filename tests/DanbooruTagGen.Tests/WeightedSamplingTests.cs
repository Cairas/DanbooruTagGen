using DanbooruTagGen.Core.Generation;
using DanbooruTagGen.Core.Models;
using Xunit;

namespace DanbooruTagGen.Tests;

public class WeightedSamplingTests
{
    private static Dictionary<string, Pool> Pools(params Pool[] p) => p.ToDictionary(x => x.Id);

    // common: postCount 10000 → 가중치 round(sqrt)=100, rare: 1 → 1. 누적 총합 101.
    private static (WildcardGenerator gen, Recipe recipe, Dictionary<string, Pool> pools, FakeTagLookup look) Setup()
    {
        var pool = new Pool { Id = "p", Candidates = { "common", "rare" } };
        var recipe = new Recipe { Slots = { new RandomPoolSlot { PoolId = "p", MinCount = 1, MaxCount = 1 } } };
        var look = new FakeTagLookup().Add("common", 10000).Add("rare", 1);
        return (new WildcardGenerator(), recipe, Pools(pool), look);
    }

    [Fact]
    public void WeightedPicksHighFrequencyOnLowRoll()
    {
        var (gen, recipe, pools, look) = Setup();
        var opts = new GenerationOptions { Sampling = SamplingMode.Weighted };
        // 룰렛 눈금 0 → 첫 버킷(common, 누적 0~99)
        var tags = gen.GenerateLineTags(recipe, pools, opts, new FakeRandomSource(0), look);
        Assert.Equal(new[] { "common" }, tags);
    }

    [Fact]
    public void WeightedPicksLowFrequencyAtBoundaryRoll()
    {
        var (gen, recipe, pools, look) = Setup();
        var opts = new GenerationOptions { Sampling = SamplingMode.Weighted };
        // 눈금 100 → common 누적(100) 경계 밖, rare(누적 101) 차지
        var tags = gen.GenerateLineTags(recipe, pools, opts, new FakeRandomSource(100), look);
        Assert.Equal(new[] { "rare" }, tags);
    }

    [Fact]
    public void WeightedFallsBackToUniformWithoutLookup()
    {
        var (gen, recipe, pools, _) = Setup();
        var opts = new GenerationOptions { Sampling = SamplingMode.Weighted };
        // 메타데이터 없음 → 균등. Next(2)=1 → 인덱스 1(rare)
        var tags = gen.GenerateLineTags(recipe, pools, opts, new FakeRandomSource(1), null);
        Assert.Equal(new[] { "rare" }, tags);
    }

    [Fact]
    public void GenerateAppliesAutoOrdering()
    {
        var recipe = new Recipe { Slots = { new FixedSlot { Tags = { "forest", "1girl", "long_hair" } } } };
        var look = new FakeTagLookup().Add("1girl", 0, "인원").Add("long_hair", 0, "머리").Add("forest", 0, "배경");
        var opts = new GenerationOptions { LineCount = 1, AutoOrderTags = true, UnderscoreToSpace = false };
        var result = new WildcardGenerator().Generate(recipe, new Dictionary<string, Pool>(), opts, null, look);
        Assert.Equal("1girl, long_hair, forest", result.Lines[0]);
    }

    [Fact]
    public void GeneratePreservesOrderWhenAutoOrderOff()
    {
        var recipe = new Recipe { Slots = { new FixedSlot { Tags = { "forest", "1girl", "long_hair" } } } };
        var look = new FakeTagLookup().Add("1girl", 0, "인원").Add("long_hair", 0, "머리").Add("forest", 0, "배경");
        var opts = new GenerationOptions { LineCount = 1, AutoOrderTags = false, UnderscoreToSpace = false };
        var result = new WildcardGenerator().Generate(recipe, new Dictionary<string, Pool>(), opts, null, look);
        Assert.Equal("forest, 1girl, long_hair", result.Lines[0]);
    }
}
