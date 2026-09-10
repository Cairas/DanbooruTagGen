using DanbooruTagGen.Core.Models;
using Xunit;

namespace DanbooruTagGen.Tests;

/// <summary>레시피 빌더의 파괴적 편집(태그·슬롯·그룹 삭제, 구성 교체)을 되돌리는 스택.
/// 이 동작들은 확인창 없이 한 번에 사라지는데 지금까지 되돌릴 방법이 전혀 없었다.</summary>
public class RecipeEditHistoryTests
{
    [Fact]
    public void UndoesTheMostRecentEditFirst()
    {
        var order = new List<string>();
        var history = new RecipeEditHistory();
        history.Push("첫째", () => order.Add("첫째"));
        history.Push("둘째", () => order.Add("둘째"));

        history.Undo();
        history.Undo();

        Assert.Equal(new[] { "둘째", "첫째" }, order);
    }

    [Fact]
    public void UndoReturnsTheLabelOfWhatItUndid()
    {
        var history = new RecipeEditHistory();
        history.Push("슬롯 '표정' 삭제", () => { });

        Assert.Equal("슬롯 '표정' 삭제", history.Undo());
    }

    [Fact]
    public void EmptyHistoryHasNothingToUndo()
    {
        var history = new RecipeEditHistory();

        Assert.False(history.CanUndo);
        Assert.Null(history.Undo());
    }

    [Fact]
    public void NextLabelNamesWhatWillBeUndone()
    {
        var history = new RecipeEditHistory();
        history.Push("태그 'blush' 삭제", () => { });
        history.Push("구성 교체", () => { });

        Assert.Equal("구성 교체", history.NextLabel);
    }

    [Fact]
    public void ForgetsTheOldestEditOnceTheDepthIsExceeded()
    {
        var ran = new List<int>();
        var history = new RecipeEditHistory();
        for (int i = 0; i <= RecipeEditHistory.MaxDepth; i++)   // 한 개 초과
        {
            int captured = i;
            history.Push($"편집 {i}", () => ran.Add(captured));
        }

        while (history.CanUndo) history.Undo();

        Assert.Equal(RecipeEditHistory.MaxDepth, ran.Count);
        Assert.DoesNotContain(0, ran);   // 가장 오래된 것이 밀려났다
    }

    [Fact]
    public void EditsMadeWhileUndoingAreNotRecorded()
    {
        // 되돌리는 동작 자체가 컬렉션을 건드리면 뷰모델의 삭제 훅이 다시 불린다.
        // 그걸 그대로 쌓으면 되돌리기와 다시하기가 서로를 먹여 스택이 영영 안 비워진다.
        var history = new RecipeEditHistory();
        history.Push("삭제", () => history.Push("되돌리는 중에 생긴 편집", () => { }));

        history.Undo();

        Assert.False(history.CanUndo);
    }

    [Fact]
    public void ClearDropsEverything()
    {
        var history = new RecipeEditHistory();
        history.Push("편집", () => { });

        history.Clear();

        Assert.False(history.CanUndo);
    }
}
