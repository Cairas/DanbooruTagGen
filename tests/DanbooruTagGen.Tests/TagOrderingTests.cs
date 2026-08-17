using DanbooruTagGen.Core.Generation;
using DanbooruTagGen.Core.Models;
using Xunit;

namespace DanbooruTagGen.Tests;

public class TagOrderingTests
{
    // 별칭 마지막 토큰이 그룹 키워드(큐레이션 규칙). 빈도는 정렬에 무관해 0으로 둔다.
    private static FakeTagLookup Lookup() => new FakeTagLookup()
        .Add("1girl", 0, "인원")
        .Add("long_hair", 0, "긴머리", "머리")
        .Add("blue_eyes", 0, "파란눈", "눈")
        .Add("smile", 0, "미소", "표정")
        .Add("shirt", 0, "셔츠", "의상")
        .Add("forest", 0, "숲", "배경")
        .Add("hatsune_miku", TagCategory.Character, 0); // 캐릭터: 그룹 키워드 없음

    [Fact]
    public void ReordersIntoCanonicalOrder()
    {
        var order = new TagOrdering(Lookup());
        var result = order.Reorder(new[] { "forest", "shirt", "1girl", "long_hair", "masterpiece" });
        // 품질 → 인원 → 머리 → 의상 → 배경
        Assert.Equal(new[] { "masterpiece", "1girl", "long_hair", "shirt", "forest" }, result);
    }

    [Fact]
    public void QualityTagGoesFirstByName()
    {
        var order = new TagOrdering(Lookup());
        var result = order.Reorder(new[] { "smile", "best_quality", "1girl" });
        Assert.Equal("best_quality", result[0]);
    }

    [Fact]
    public void CharacterCategoryRanksAfterCount()
    {
        var order = new TagOrdering(Lookup());
        var result = order.Reorder(new[] { "long_hair", "hatsune_miku", "1girl" });
        // 인원(1) → 캐릭터(2) → 머리(4)
        Assert.Equal(new[] { "1girl", "hatsune_miku", "long_hair" }, result);
    }

    [Fact]
    public void UnclassifiedTagStaysAtOriginalPosition()
    {
        var order = new TagOrdering(Lookup());
        // mystery_tag는 조회 실패(미분류) → 인덱스 1에 고정. 분류된 것만 그 사이에 정렬.
        var result = order.Reorder(new[] { "long_hair", "mystery_tag", "1girl" });
        Assert.Equal(new[] { "1girl", "mystery_tag", "long_hair" }, result);
    }

    [Fact]
    public void StableWithinSameGroup()
    {
        var look = new FakeTagLookup()
            .Add("hair_a", 0, "머리").Add("hair_b", 0, "머리").Add("hair_c", 0, "머리");
        var order = new TagOrdering(look);
        var result = order.Reorder(new[] { "hair_c", "hair_a", "hair_b" });
        // 동일 그룹은 입력 순서 보존
        Assert.Equal(new[] { "hair_c", "hair_a", "hair_b" }, result);
    }

    [Fact]
    public void ReorderNeverDropsOrDuplicatesTags()
    {
        // 정렬 대상(분류됨)과 고정 대상(미분류)이 섞여도 개수·구성이 절대 바뀌면 안 된다.
        var input = new[] { "forest", "mystery_tag", "shirt", "1girl", "long_hair", "masterpiece", "another_unknown" };
        var order = new TagOrdering(Lookup());
        var result = order.Reorder(input);
        Assert.Equal(input.Length, result.Count);
        Assert.Equal(input.OrderBy(x => x, StringComparer.Ordinal), result.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void ReorderPreservesDuplicateTags()
    {
        // 같은 태그가 줄 안에 두 번 있어도(DedupeWithinLine 꺼짐 등) 둘 다 살아남아야 한다.
        var order = new TagOrdering(Lookup());
        var result = order.Reorder(new[] { "shirt", "1girl", "shirt" });
        Assert.Equal(3, result.Count);
        Assert.Equal(2, result.Count(t => t == "shirt"));
    }

    [Fact]
    public void EmptyInputReturnsEmpty()
    {
        var order = new TagOrdering(Lookup());
        Assert.Empty(order.Reorder(Array.Empty<string>()));
    }

    [Fact]
    public void AllUnclassifiedPreservesOriginalOrder()
    {
        var order = new TagOrdering(Lookup());
        var input = new[] { "unknown_c", "unknown_a", "unknown_b" };
        Assert.Equal(input, order.Reorder(input));
    }

    [Fact]
    public void ArtistCategoryRanksWithCharacterAndCopyright()
    {
        // Animagine/NoobAI 가이드 공통 요구사항: 작가 태그는 캐릭터/시리즈 바로 옆.
        // 예전엔 TagCategory.Artist가 미분류로 빠져 원래 위치에 방치됐었다(회귀 테스트).
        var look = new FakeTagLookup()
            .Add("1girl", 0, "인원")
            .Add("long_hair", 0, "머리")
            .Add("hatsune_miku", TagCategory.Character, 0)
            .Add("some_artist", TagCategory.Artist, 0);
        var order = new TagOrdering(look);
        var result = order.Reorder(new[] { "long_hair", "some_artist", "hatsune_miku", "1girl" });
        // 인원(1) → 캐릭터/작가(2, 입력 순서 보존) → 머리(4)
        Assert.Equal(new[] { "1girl", "some_artist", "hatsune_miku", "long_hair" }, result);
    }
}
