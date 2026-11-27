using DanbooruTagGen.Core.Models;

namespace DanbooruTagGen.Core.Persistence;

/// <summary>레시피 편집 탭(빌더)이 "지금 어느 라이브러리 레시피를 편집 중인가"를 잃지 않게 하는 규칙.
/// <para>왜: 예전 자동 저장(last-recipe.json)은 슬롯만 남기고 매번 새 무작위 id를 써서, 재시작하면 화면엔 그 레시피가
/// 그대로 떠 있어도 앱은 어느 레시피인지 몰랐다. 그래서 퀵 갱신이 라이브러리·일괄 목록만 최신으로 바꾸고 편집 탭은
/// 옛 구성으로 남아, 편집 탭 미리보기만 옛 내용으로 나왔다. 실행용 사본도 매번 새 id·"Untitled"라 같은 문제가
/// 누를 때마다 로그에 다시 남았다.</para></summary>
public static class BuilderSession
{
    /// <summary>라이브러리 레시피와 이어지지 않은 편집 탭 구성의 실행용 id(매번 같아야 로그 중복 거르기가 된다).</summary>
    public const string UnlinkedRunId = "builder";

    /// <summary>자동 저장본. 이어진 레시피가 있으면 그 id를 담는다(없으면 기본 무작위 id — 아무 레시피와도 안 이어진다).</summary>
    public static Recipe Autosave(IEnumerable<Slot> slots, string? linkedRecipeId)
    {
        var recipe = new Recipe { Name = "Untitled", Slots = slots.ToList() };
        if (!string.IsNullOrEmpty(linkedRecipeId)) recipe.Id = linkedRecipeId;
        return recipe;
    }

    /// <summary>되살린 자동 저장본이 라이브러리의 어느 레시피와 이어지는가(그 레시피가 지금도 있을 때만).</summary>
    public static string? LinkedId(Recipe restored, IEnumerable<Recipe> library) =>
        !string.IsNullOrEmpty(restored.Id) && library.Any(r => r.Id == restored.Id) ? restored.Id : null;

    /// <summary>생성·미리보기에 넘길 사본: 이어진 레시피의 id·이름("… (편집 탭)"), 없으면 <see cref="UnlinkedRunId"/>·"레시피 빌더".</summary>
    public static Recipe ForRun(IEnumerable<Slot> slots, string? linkedRecipeId, IEnumerable<Recipe> library)
    {
        var linked = string.IsNullOrEmpty(linkedRecipeId) ? null : library.FirstOrDefault(r => r.Id == linkedRecipeId);
        return new Recipe
        {
            Id = linked?.Id ?? UnlinkedRunId,
            Name = linked != null ? linked.Name + " (편집 탭)" : "레시피 빌더",
            Slots = slots.ToList(),
        };
    }
}
