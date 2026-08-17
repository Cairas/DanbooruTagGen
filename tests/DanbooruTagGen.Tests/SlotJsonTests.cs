using System.Text.Json;
using DanbooruTagGen.Core.Models;
using DanbooruTagGen.Core.Persistence;
using Xunit;

namespace DanbooruTagGen.Tests;

public class SlotJsonTests
{
    [Fact]
    public void RecipeRoundTripsPolymorphicSlots()
    {
        var recipe = new Recipe
        {
            Name = "test",
            Slots =
            {
                new FixedSlot { Label = "base", Tags = { "1girl", "solo" } },
                new RandomPoolSlot { Label = "표정", PoolId = "p1", ExtraPoolIds = { "p2", "p3" }, MinCount = 1, MaxCount = 2 },
            },
        };

        var json = JsonSerializer.Serialize(recipe, JsonStore.Options);
        var back = JsonSerializer.Deserialize<Recipe>(json, JsonStore.Options)!;

        Assert.Equal(2, back.Slots.Count);
        var fixedSlot = Assert.IsType<FixedSlot>(back.Slots[0]);
        Assert.Equal(new[] { "1girl", "solo" }, fixedSlot.Tags);
        var poolSlot = Assert.IsType<RandomPoolSlot>(back.Slots[1]);
        Assert.Equal("p1", poolSlot.PoolId);
        Assert.Equal(2, poolSlot.MaxCount);
        Assert.Equal(new[] { "p2", "p3" }, poolSlot.ExtraPoolIds);
    }

    [Fact]
    public void OldRecipeJsonWithoutExtraPoolIdsLoadsWithEmptyChain()
    {
        // 기존 390개 레시피 파일엔 extraPoolIds 필드가 아예 없다 — 필드 추가 전 저장분도
        // 그대로 로드돼야 하고(누락 필드는 기본값 빈 컬렉션), poolId 하나짜리 참조는 그대로 유지돼야 한다.
        var json = """
        {"name":"old","slots":[{"type":"randomPool","label":"체위","poolId":"p1","tags":[],"minCount":1,"maxCount":1}]}
        """;
        var recipe = JsonSerializer.Deserialize<Recipe>(json, JsonStore.Options)!;
        var poolSlot = Assert.IsType<RandomPoolSlot>(recipe.Slots[0]);
        Assert.Equal("p1", poolSlot.PoolId);
        Assert.Empty(poolSlot.ExtraPoolIds);
    }

    [Fact]
    public void AlternativeSlotRoundTrips()
    {
        var recipe = new Recipe
        {
            Name = "test",
            Slots =
            {
                new AlternativeSlot
                {
                    Label = "대안",
                    Groups =
                    {
                        new AlternativeGroup { Label = "방법 A", Tags = { "collar", "leash" } },
                        new AlternativeGroup { Label = "방법 B", Tags = { "prison_uniform" } },
                    },
                },
            },
        };

        var json = JsonSerializer.Serialize(recipe, JsonStore.Options);
        var back = JsonSerializer.Deserialize<Recipe>(json, JsonStore.Options)!;

        var alt = Assert.IsType<AlternativeSlot>(back.Slots[0]);
        Assert.Equal(2, alt.Groups.Count);
        Assert.Equal("방법 A", alt.Groups[0].Label);
        Assert.Equal(new[] { "collar", "leash" }, alt.Groups[0].Tags);
        Assert.Equal(new[] { "prison_uniform" }, alt.Groups[1].Tags);
    }
}
