namespace DanbooruTagGen.Core.Models;

public sealed class Recipe
{
    /// <summary>레시피 식별자. 번들 프리셋(data/presets)은 고정 id를 갖고 있어,
    /// 시딩(PresetSeeder)이 "이미 넣었는지/사용자가 지웠는지"를 이 값으로 판단한다.
    /// 사용자가 만든 레시피는 임의 GUID — id가 없던 옛 저장 파일도 로드 시 자동 발급된다.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Untitled";
    /// <summary>레시피 라이브러리의 카테고리 필터용 분류(예: "촉수", "구속·BDSM").
    /// 빈 문자열이면 미분류 — 옛 저장 파일에 필드가 없어도 기본값이라 그대로 로드된다.</summary>
    public string Category { get; set; } = "";
    public List<Slot> Slots { get; set; } = new();
    public int DefaultLineCount { get; set; } = 100;

    /// <summary>레시피 라이브러리 목록에 보여줄 모순 배지("‼"=고정 태그끼리 항상 충돌,
    /// "⚠"=풀 후보까지 포함하면 충돌 가능, ""=없음). 저장하지 않는 계산값 — 라이브러리
    /// 창이 목록을 채울 때마다 RecipeLibraryViewModel이 다시 계산해 넣는다.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string ConflictBadge { get; set; } = "";
}
