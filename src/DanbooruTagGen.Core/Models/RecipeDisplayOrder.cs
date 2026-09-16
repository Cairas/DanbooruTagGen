namespace DanbooruTagGen.Core.Models;

/// <summary>목록에 레시피를 보여 줄 때 항상 맨 아래로 내릴 것을 가려낸다.
/// <para>
/// 레시피 목록의 기본 순서는 recipes.json의 배열 순서인데, PresetSeeder가 새 레시피를 배열
/// 끝에 append하기 때문에 "작업 중인 브랜치 확인용" 같은 테스트 팩이 시딩할 때마다 위로
/// 밀려 올라간다. 전에는 배열을 손으로 다시 정렬하고 앱을 재시작해야 했다 — 표시할 때
/// 규칙으로 내려 주면 recipes.json을 건드릴 필요가 없다.
/// </para>
/// <para>
/// 레시피 라이브러리 창과 일괄 생성 체크리스트가 <b>같은</b> 규칙을 써야 한다. 한쪽만
/// 정렬하면 두 화면의 순서가 달라져 같은 팩을 찾는 데 헷갈린다.
/// </para></summary>
public static class RecipeDisplayOrder
{
    /// <summary>이 라벨이 붙은 레시피는 목록 맨 아래로 내린다. id를 코드에 박지 않은 이유는,
    /// 테스트 팩을 새로 만들 때마다 코드를 고쳐야 하기 때문이다 — 라벨만 붙이면 된다.</summary>
    public const string PinToBottomLabel = "테스트";

    /// <summary>라벨은 사용자가 직접 타이핑하는 자유 문자열이라 앞뒤 공백과 대소문자를 봐준다.</summary>
    public static bool IsPinnedToBottom(Recipe recipe) =>
        recipe.Labels.Any(l => string.Equals(l.Trim(), PinToBottomLabel, StringComparison.OrdinalIgnoreCase));

    /// <summary>핀 대상만 맨 뒤로 보낸다. OrderBy는 안정 정렬이라 나머지 레시피끼리의 기존
    /// 순서(= recipes.json 배열 순서)는 그대로 유지된다.</summary>
    public static IEnumerable<Recipe> PinnedLast(IEnumerable<Recipe> recipes) =>
        recipes.OrderBy(IsPinnedToBottom);
}
