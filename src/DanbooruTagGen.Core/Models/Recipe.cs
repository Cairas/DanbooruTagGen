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
    /// <summary>사용자 정의 자유 라벨(예: "전쟁", "불륜", "네토라레"). danbooru 태그가 아니라
    /// 컨셉 분류·검색용이며 한 레시피에 여러 개 붙일 수 있다. 이름을 Tags로 짓지 않은 이유는
    /// RandomPoolSlot.Tags(danbooru 태그)와 헷갈리지 않기 위해서다. 빈 리스트면 라벨 없음 —
    /// 옛 저장 파일에 필드가 없어도 기본값이라 그대로 로드된다.</summary>
    public List<string> Labels { get; set; } = new();
    public List<Slot> Slots { get; set; } = new();
    public int DefaultLineCount { get; set; } = 100;

    /// <summary>레시피 라이브러리 목록에 보여줄 모순 배지("‼"=고정 태그끼리 항상 충돌,
    /// "⚠"=풀 후보까지 포함하면 충돌 가능, ""=없음). 저장하지 않는 계산값 — 라이브러리
    /// 창이 목록을 채울 때마다 RecipeLibraryViewModel이 다시 계산해 넣는다.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string ConflictBadge { get; set; } = "";

    /// <summary>MAJOR 축 조합 수가 VarietyAnalyzer.Threshold(200) 미만일 때 표시하는 배지
    /// ("🔸조합수", ""=충분함). ConflictBadge와 같은 이유로 저장하지 않는 계산값.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string VarietyBadge { get; set; } = "";
}
