using DanbooruTagGen.Core.Generation;
using DanbooruTagGen.Core.Models;
using Xunit;

namespace DanbooruTagGen.Tests;

public class WildcardGeneratorValidationTests
{
    private static Dictionary<string, Pool> Pools(params Pool[] p) => p.ToDictionary(x => x.Id);

    [Fact]
    public void ValidRecipePasses()
    {
        var pool = new Pool { Id = "p1", Name = "표정", Candidates = { "smile", "grin" } };
        var recipe = new Recipe { Slots = { new RandomPoolSlot { PoolId = "p1", MinCount = 1, MaxCount = 2 } } };
        WildcardGenerator.Validate(recipe, Pools(pool)); // no throw
    }

    /// <summary>태그가 없는 대안 그룹은 "이 분기에서는 아무것도 안 나온다"를 뜻하는 의도된
    /// 패턴이다. 가중치로 무발동 확률을 정밀하게 잡을 때 randomPool의 min/max보다 쓰기 편해서
    /// n534·n535·ztest가 이미 이 방식을 쓰고 있다 — 검증이 막으면 그 팩들이 생성을 못 한다.</summary>
    [Fact]
    public void AlternativeGroupWithNoTagsIsAllowed()
    {
        var recipe = new Recipe
        {
            Slots =
            {
                new AlternativeSlot
                {
                    Label = "임신",
                    Groups =
                    {
                        new AlternativeGroup { Label = "있음", Tags = { "pregnant" }, Weight = 1 },
                        new AlternativeGroup { Label = "없음", Weight = 3 },
                    },
                },
            },
        };
        WildcardGenerator.Validate(recipe, Pools()); // no throw
    }

    /// <summary>검증만 통과하고 생성에서 터지면 의미가 없다. 빈 그룹이 뽑히면 그 줄에 태그를
    /// 하나도 더하지 않고 지나가야 한다.</summary>
    [Fact]
    public void EmptyAlternativeGroupContributesNoTags()
    {
        var recipe = new Recipe
        {
            Slots =
            {
                new FixedSlot { Label = "기본", Tags = { "1girl" } },
                new AlternativeSlot
                {
                    Label = "임신",
                    // 가중치 0인 "있음"은 절대 안 뽑히므로 매 줄 빈 그룹이 선택된다.
                    Groups =
                    {
                        new AlternativeGroup { Label = "있음", Tags = { "pregnant" }, Weight = 0 },
                        new AlternativeGroup { Label = "없음", Weight = 1 },
                    },
                },
            },
        };

        var result = new WildcardGenerator().Generate(
            recipe, Pools(), new GenerationOptions { LineCount = 5, Seed = 1 });

        Assert.Equal(5, result.Lines.Count);
        Assert.All(result.Lines, line => Assert.Equal("1girl", line));
    }

    [Fact]
    public void EmptyRecipeThrows()
    {
        var ex = Assert.Throws<GenerationValidationException>(
            () => WildcardGenerator.Validate(new Recipe(), Pools()));
        Assert.Contains("슬롯", ex.Message);
    }

    [Fact]
    public void EmptyFixedSlotThrows()
    {
        var recipe = new Recipe { Slots = { new FixedSlot() } };
        Assert.Throws<GenerationValidationException>(() => WildcardGenerator.Validate(recipe, Pools()));
    }

    [Fact]
    public void MinGreaterThanMaxThrows()
    {
        var pool = new Pool { Id = "p1", Candidates = { "a", "b" } };
        var recipe = new Recipe { Slots = { new RandomPoolSlot { PoolId = "p1", MinCount = 3, MaxCount = 1 } } };
        Assert.Throws<GenerationValidationException>(() => WildcardGenerator.Validate(recipe, Pools(pool)));
    }

    [Fact]
    public void MissingPoolReferenceThrows()
    {
        var recipe = new Recipe { Slots = { new RandomPoolSlot { PoolId = "nope", MinCount = 1, MaxCount = 1 } } };
        var ex = Assert.Throws<GenerationValidationException>(() => WildcardGenerator.Validate(recipe, Pools()));
        Assert.Contains("nope", ex.Message);
    }

    [Fact]
    public void MinExceedsCandidateCountThrows()
    {
        var pool = new Pool { Id = "p1", Candidates = { "a" } };
        var recipe = new Recipe { Slots = { new RandomPoolSlot { PoolId = "p1", MinCount = 2, MaxCount = 2 } } };
        Assert.Throws<GenerationValidationException>(() => WildcardGenerator.Validate(recipe, Pools(pool)));
    }

    [Fact]
    public void NegativeMinCountThrows()
    {
        var pool = new Pool { Id = "p1", Candidates = { "a", "b" } };
        var recipe = new Recipe { Slots = { new RandomPoolSlot { PoolId = "p1", MinCount = -1, MaxCount = 1 } } };
        Assert.Throws<GenerationValidationException>(() => WildcardGenerator.Validate(recipe, Pools(pool)));
    }

    [Fact]
    public void InlineTagsWithStalePoolReferenceDoesNotThrow()
    {
        // Tags가 이미 있으면 PoolId가 가리키는 풀이 없어도(삭제된 풀 등) 막지 않는다 —
        // Tags만으로 생성 가능하므로 여기서 에러를 내는 건 지나치게 엄격하다.
        var recipe = new Recipe { Slots = { new RandomPoolSlot { PoolId = "deleted", Tags = { "a" }, MinCount = 1, MaxCount = 1 } } };
        WildcardGenerator.Validate(recipe, Pools()); // no throw
    }

    [Fact]
    public void AlternativeSlotWithNoGroupsThrows()
    {
        var recipe = new Recipe { Slots = { new AlternativeSlot() } };
        Assert.Throws<GenerationValidationException>(() => WildcardGenerator.Validate(recipe, Pools()));
    }

    [Fact]
    public void AlternativeSlotWithFilledGroupsPasses()
    {
        var recipe = new Recipe { Slots =
        {
            new AlternativeSlot { Groups = { new AlternativeGroup { Tags = { "a" } }, new AlternativeGroup { Tags = { "b" } } } },
        } };
        WildcardGenerator.Validate(recipe, Pools()); // no throw
    }

    [Fact]
    public void AllSlotsDisabledThrows()
    {
        var recipe = new Recipe { Slots = { new FixedSlot { Tags = { "1girl" }, IsEnabled = false } } };
        var ex = Assert.Throws<GenerationValidationException>(() => WildcardGenerator.Validate(recipe, Pools()));
        Assert.Contains("꺼져", ex.Message);
    }
}
