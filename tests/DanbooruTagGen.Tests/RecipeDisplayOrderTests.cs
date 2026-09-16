using DanbooruTagGen.Core.Models;
using Xunit;

namespace DanbooruTagGen.Tests;

/// <summary>"테스트" 라벨이 붙은 팩을 목록 맨 아래로 내리는 규칙. 레시피 라이브러리 창과
/// 일괄 생성 체크리스트가 이 규칙을 공유하므로, 여기서 깨지면 두 화면이 동시에 어긋난다.</summary>
public class RecipeDisplayOrderTests
{
    private static Recipe R(string name, params string[] labels) =>
        new() { Name = name, Labels = labels.ToList() };

    [Fact]
    public void RecipeWithTestLabelIsPinned()
        => Assert.True(RecipeDisplayOrder.IsPinnedToBottom(R("테스트 팩", "테스트")));

    [Fact]
    public void RecipeWithoutTestLabelIsNotPinned()
        => Assert.False(RecipeDisplayOrder.IsPinnedToBottom(R("촉수", "촉수", "구속")));

    [Fact]
    public void RecipeWithNoLabelsIsNotPinned()
        => Assert.False(RecipeDisplayOrder.IsPinnedToBottom(R("촉수")));

    /// <summary>라벨은 사용자가 직접 치는 자유 문자열이라 공백이 섞여 들어오기 쉽다.</summary>
    [Fact]
    public void SurroundingWhitespaceInTheLabelStillCounts()
        => Assert.True(RecipeDisplayOrder.IsPinnedToBottom(R("테스트 팩", "  테스트  ")));

    [Fact]
    public void PinnedRecipeGoesLast()
    {
        var recipes = new[] { R("a"), R("테스트", "테스트"), R("b"), R("c") };
        var ordered = RecipeDisplayOrder.PinnedLast(recipes).Select(r => r.Name);
        Assert.Equal(new[] { "a", "b", "c", "테스트" }, ordered);
    }

    /// <summary>핀 대상이 아닌 레시피들끼리의 순서는 recipes.json 배열 순서 그대로여야 한다 —
    /// 여기서 이름순 정렬 같은 걸 끼얹으면 사용자가 배열을 손으로 맞춰 둔 의미가 없어진다.</summary>
    [Fact]
    public void OtherRecipesKeepTheirOriginalOrder()
    {
        var recipes = new[] { R("z"), R("테스트", "테스트"), R("a"), R("m") };
        var ordered = RecipeDisplayOrder.PinnedLast(recipes).Select(r => r.Name);
        Assert.Equal(new[] { "z", "a", "m", "테스트" }, ordered);
    }

    [Fact]
    public void SeveralPinnedRecipesAllGoLastKeepingTheirOrder()
    {
        var recipes = new[] { R("t1", "테스트"), R("a"), R("t2", "테스트"), R("b") };
        var ordered = RecipeDisplayOrder.PinnedLast(recipes).Select(r => r.Name);
        Assert.Equal(new[] { "a", "b", "t1", "t2" }, ordered);
    }

    [Fact]
    public void NothingPinnedLeavesTheListUntouched()
    {
        var recipes = new[] { R("a"), R("b"), R("c") };
        var ordered = RecipeDisplayOrder.PinnedLast(recipes).Select(r => r.Name);
        Assert.Equal(new[] { "a", "b", "c" }, ordered);
    }
}
