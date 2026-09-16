namespace DanbooruTagGen.Core.Models;

/// <summary>프리셋에 담긴 레시피 하나. id와 이름을 함께 남기는 이유는 두 가지다 —
/// (1) 번들 팩이 지워졌다 다른 id로 다시 유입되면 id 매칭이 깨지므로 이름으로 한 번 더 찾고,
/// (2) 사라진 항목을 사용자에게 "id 3a7f…"가 아니라 이름으로 알려 줄 수 있다.</summary>
public sealed class BatchSelectionEntry
{
    public string RecipeId { get; set; } = "";
    public string RecipeName { get; set; } = "";
}

/// <summary>"여러 레시피 일괄 생성"에서 고른 조합을 이름 붙여 저장한 것. 자주 쓰는 묶음
/// (예: 촉수 계열 12개)을 매번 다시 체크하지 않아도 되게 한다.
/// <para>생성 훅(<see cref="GenerationHook"/>)과는 별개로 저장·불러오기한다 — 선택 조합과
/// 훅은 서로 독립적으로 조합해 쓰는 것이 자연스럽기 때문이다.</para></summary>
public sealed class BatchSelectionPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public DateTime SavedAt { get; set; } = DateTime.Now;
    public List<BatchSelectionEntry> Entries { get; set; } = new();
}
