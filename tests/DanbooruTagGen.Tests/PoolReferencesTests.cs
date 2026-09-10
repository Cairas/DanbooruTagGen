using DanbooruTagGen.Core.Models;
using DanbooruTagGen.Core.Persistence;
using Xunit;

namespace DanbooruTagGen.Tests;

/// <summary>"이 풀을 지우면 어떤 레시피가 깨지는가"를 미리 알려 주는 역참조 조회.
/// 풀 삭제에는 되돌리기가 없고, 참조가 끊긴 레시피는 생성 자체가 검증 오류로 막힌다.</summary>
public class PoolReferencesTests
{
    private static Recipe RecipeWith(string name, params Slot[] slots)
        => new() { Name = name, Slots = slots.ToList() };

    [Fact]
    public void FindsRecipesReferencingPoolAsPrimary()
    {
        var recipes = new[]
        {
            RecipeWith("촉수", new RandomPoolSlot { PoolId = "p1" }),
            RecipeWith("무관", new RandomPoolSlot { PoolId = "other" }),
        };

        var users = PoolReferences.FindRecipesUsing("p1", recipes);

        Assert.Equal(new[] { "촉수" }, users);
    }

    [Fact]
    public void FindsRecipesReferencingPoolThroughChaining()
    {
        var slot = new RandomPoolSlot { PoolId = "base" };
        slot.ExtraPoolIds.Add("p1");
        var recipes = new[] { RecipeWith("체이닝", slot) };

        Assert.Equal(new[] { "체이닝" }, PoolReferences.FindRecipesUsing("p1", recipes));
    }

    [Fact]
    public void ReportsEachRecipeOnceEvenWithSeveralReferencingSlots()
    {
        var recipes = new[]
        {
            RecipeWith("둘 다", new RandomPoolSlot { PoolId = "p1" }, new RandomPoolSlot { PoolId = "p1" }),
        };

        Assert.Single(PoolReferences.FindRecipesUsing("p1", recipes));
    }

    [Fact]
    public void UnreferencedPoolHasNoUsers()
    {
        var recipes = new[] { RecipeWith("고정만", new FixedSlot { Tags = { "1girl" } }) };

        Assert.Empty(PoolReferences.FindRecipesUsing("p1", recipes));
    }
}
