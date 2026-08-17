using DanbooruTagGen.Core.Generation;
using DanbooruTagGen.Core.Models;
using Xunit;

namespace DanbooruTagGen.Tests;

public class WildcardGeneratorTests
{
    private static Dictionary<string, Pool> Pools(params Pool[] p) => p.ToDictionary(x => x.Id);

    [Fact]
    public void FixedSlotEmitsAllTagsInOrder()
    {
        var recipe = new Recipe { Slots = { new FixedSlot { Tags = { "1girl", "solo" } } } };
        var gen = new WildcardGenerator();
        var line = gen.GenerateLine(recipe, Pools(), new GenerationOptions(), new FakeRandomSource());
        Assert.Equal("1girl, solo", line);
    }

    [Fact]
    public void RandomPoolPicksWithoutReplacement()
    {
        var pool = new Pool { Id = "p", Candidates = { "a", "b", "c" } };
        var recipe = new Recipe { Slots = { new RandomPoolSlot { PoolId = "p", MinCount = 2, MaxCount = 2 } } };
        var gen = new WildcardGenerator();
        // count=2 고정. 첫 pick index 0 -> "a"(제거), 다음 index 0 -> "b"
        var line = gen.GenerateLine(recipe, Pools(pool), new GenerationOptions(), new FakeRandomSource(0, 0, 0));
        Assert.Equal("a, b", line);
    }

    [Fact]
    public void RandomCountUsesRangeFromRandomSource()
    {
        var pool = new Pool { Id = "p", Candidates = { "a", "b", "c" } };
        var recipe = new Recipe { Slots = { new RandomPoolSlot { PoolId = "p", MinCount = 1, MaxCount = 3 } } };
        var gen = new WildcardGenerator();
        // 범위 폭 = 3 (1..3). FakeRandom 첫 Next(3)=2 -> count = 1+2 = 3
        var line = gen.GenerateLine(recipe, Pools(pool), new GenerationOptions(), new FakeRandomSource(2, 0, 0, 0));
        Assert.Equal(3, line.Split(", ").Length);
    }

    [Fact]
    public void DedupeWithinLineRemovesRepeats()
    {
        var recipe = new Recipe { Slots =
        {
            new FixedSlot { Tags = { "1girl", "smile" } },
            new FixedSlot { Tags = { "smile", "solo" } },
        } };
        var gen = new WildcardGenerator();
        var line = gen.GenerateLine(recipe, Pools(), new GenerationOptions { DedupeWithinLine = true }, new FakeRandomSource());
        Assert.Equal("1girl, smile, solo", line);
    }

    [Fact]
    public void BlocklistRemovesTags()
    {
        var recipe = new Recipe { Slots = { new FixedSlot { Tags = { "1girl", "lowres", "solo" } } } };
        var opts = new GenerationOptions { Blocklist = { "lowres" } };
        var gen = new WildcardGenerator();
        var line = gen.GenerateLine(recipe, Pools(), opts, new FakeRandomSource());
        Assert.Equal("1girl, solo", line);
    }

    [Fact]
    public void SeedProducesDeterministicOutput()
    {
        var pool = new Pool { Id = "p", Candidates = { "a", "b", "c", "d", "e" } };
        var recipe = new Recipe { Slots = { new RandomPoolSlot { PoolId = "p", MinCount = 1, MaxCount = 2 } } };
        var gen = new WildcardGenerator();
        var pools = Pools(pool);
        var r1 = gen.Generate(recipe, pools, new GenerationOptions { LineCount = 10, Seed = 42 });
        var r2 = gen.Generate(recipe, pools, new GenerationOptions { LineCount = 10, Seed = 42 });
        Assert.Equal(r1.Lines, r2.Lines);
    }

    [Fact]
    public void AvoidDuplicateLinesWarnsWhenSpaceExhausted()
    {
        // 후보 1개, 1개 추출 -> 가능한 고유 줄은 1개뿐인데 5줄 요청
        var pool = new Pool { Id = "p", Candidates = { "only" } };
        var recipe = new Recipe { Slots = { new RandomPoolSlot { PoolId = "p", MinCount = 1, MaxCount = 1 } } };
        var gen = new WildcardGenerator();
        var result = gen.Generate(recipe, Pools(pool), new GenerationOptions { LineCount = 5, AvoidDuplicateLines = true, Seed = 1 });
        Assert.Equal(5, result.Lines.Count);
        Assert.Single(result.Warnings); // 경고는 정확히 1회만
    }

    [Fact]
    public void UnderscoreToSpaceConvertsOutput()
    {
        var recipe = new Recipe { Slots = { new FixedSlot { Tags = { "long_hair", "blue_eyes" } } } };
        var gen = new WildcardGenerator();
        var on = gen.GenerateLine(recipe, Pools(), new GenerationOptions { UnderscoreToSpace = true }, new FakeRandomSource());
        Assert.Equal("long hair, blue eyes", on);
        var off = gen.GenerateLine(recipe, Pools(), new GenerationOptions { UnderscoreToSpace = false }, new FakeRandomSource());
        Assert.Equal("long_hair, blue_eyes", off);
    }

    [Fact]
    public void RandomPoolSlotCombinesInlineTagsWithPoolCandidates()
    {
        // 회귀 테스트: 풀을 연결해 둔 랜덤 슬롯에 인라인 태그를 하나 더 추가해도
        // 풀 후보가 사라지면 안 된다(예전엔 Tags가 있으면 PoolId를 통째로 무시했음 —
        // "슬롯에 풀을 불러온 뒤 태그를 하나 더 넣었더니 나머지 풀 태그가 안 나온다" 버그).
        var pool = new Pool { Id = "p", Candidates = { "a", "b" } };
        var recipe = new Recipe { Slots = { new RandomPoolSlot { PoolId = "p", Tags = { "extra" }, MinCount = 3, MaxCount = 3 } } };
        var gen = new WildcardGenerator();
        // 후보 3개("extra","a","b") 전부 뽑아야 하므로 순서는 FakeRandomSource가 주는 index 그대로.
        var line = gen.GenerateLine(recipe, Pools(pool), new GenerationOptions(), new FakeRandomSource(0, 0, 0));
        Assert.Equal("extra, a, b", line);
    }

    [Fact]
    public void RandomPoolSlotDedupesInlineTagAlreadyInPool()
    {
        // 인라인 태그가 풀에도 이미 있으면 합칠 때 중복으로 두 번 후보에 들어가면 안 된다.
        var pool = new Pool { Id = "p", Candidates = { "a", "b" } };
        var recipe = new Recipe { Slots = { new RandomPoolSlot { PoolId = "p", Tags = { "a" }, MinCount = 2, MaxCount = 2 } } };
        var gen = new WildcardGenerator();
        var line = gen.GenerateLine(recipe, Pools(pool), new GenerationOptions(), new FakeRandomSource(0, 0, 0));
        Assert.Equal("a, b", line);
    }

    [Fact]
    public void RandomPoolSlotChainsExtraPoolIds()
    {
        // 풀 체이닝: PoolId 하나 + ExtraPoolIds 여러 개가 모두 하나의 후보군으로 합쳐져야 한다
        // (슬롯을 따로 두면 한 줄에 서로 다른 체위 태그가 동시에 뽑히는 모순이 생길 수 있어서
        // 반드시 이렇게 합치는 방식이어야 함).
        var p1 = new Pool { Id = "p1", Candidates = { "a", "b" } };
        var p2 = new Pool { Id = "p2", Candidates = { "c" } };
        var recipe = new Recipe { Slots =
        {
            new RandomPoolSlot { PoolId = "p1", ExtraPoolIds = { "p2" }, MinCount = 3, MaxCount = 3 },
        } };
        var gen = new WildcardGenerator();
        var line = gen.GenerateLine(recipe, Pools(p1, p2), new GenerationOptions(), new FakeRandomSource(0, 0, 0));
        Assert.Equal("a, b, c", line);
    }

    [Fact]
    public void RandomPoolSlotDedupesAcrossChainedPools()
    {
        // 체이닝된 풀끼리 같은 태그가 있으면 후보에 한 번만 들어가야 한다.
        var p1 = new Pool { Id = "p1", Candidates = { "a", "b" } };
        var p2 = new Pool { Id = "p2", Candidates = { "b", "c" } };
        var recipe = new Recipe { Slots =
        {
            new RandomPoolSlot { PoolId = "p1", ExtraPoolIds = { "p2" }, MinCount = 3, MaxCount = 3 },
        } };
        var gen = new WildcardGenerator();
        var line = gen.GenerateLine(recipe, Pools(p1, p2), new GenerationOptions(), new FakeRandomSource(0, 0, 0));
        Assert.Equal("a, b, c", line);
    }

    [Fact]
    public void AlternativeSlotPicksExactlyOneGroupWhole()
    {
        // "방법 A vs 방법 B": 매 줄 그룹 하나만 통째로 뽑히고 다른 그룹 태그와는 안 섞여야 한다.
        var recipe = new Recipe { Slots =
        {
            new AlternativeSlot { Groups =
            {
                new AlternativeGroup { Label = "A", Tags = { "collar", "leash" } },
                new AlternativeGroup { Label = "B", Tags = { "prison_uniform" } },
            } },
        } };
        var gen = new WildcardGenerator();
        var opts = new GenerationOptions { UnderscoreToSpace = false };
        var lineA = gen.GenerateLine(recipe, Pools(), opts, new FakeRandomSource(0));
        Assert.Equal("collar, leash", lineA);
        var lineB = gen.GenerateLine(recipe, Pools(), opts, new FakeRandomSource(1));
        Assert.Equal("prison_uniform", lineB);
    }

    [Fact]
    public void AlternativeSlotCoexistsWithOtherSlots()
    {
        var pool = new Pool { Id = "p", Candidates = { "smile" } };
        var recipe = new Recipe { Slots =
        {
            new FixedSlot { Tags = { "1girl" } },
            new AlternativeSlot { Groups = { new AlternativeGroup { Tags = { "a" } } } },
            new RandomPoolSlot { PoolId = "p", MinCount = 1, MaxCount = 1 },
        } };
        var gen = new WildcardGenerator();
        var line = gen.GenerateLine(recipe, Pools(pool), new GenerationOptions(), new FakeRandomSource(0, 0));
        Assert.Equal("1girl, a, smile", line);
    }

    [Fact]
    public void DisabledSlotIsSkippedEntirely()
    {
        var recipe = new Recipe { Slots =
        {
            new FixedSlot { Tags = { "1girl" } },
            new FixedSlot { Tags = { "solo" }, IsEnabled = false },
        } };
        var gen = new WildcardGenerator();
        var line = gen.GenerateLine(recipe, Pools(), new GenerationOptions(), new FakeRandomSource());
        Assert.Equal("1girl", line);
    }
}
