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
    public void AlternativeSlotWithEmptyGroupThrows()
    {
        var recipe = new Recipe { Slots =
        {
            new AlternativeSlot { Groups = { new AlternativeGroup { Tags = { "a" } }, new AlternativeGroup() } },
        } };
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
