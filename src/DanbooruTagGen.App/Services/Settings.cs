using DanbooruTagGen.Core.Output;

namespace DanbooruTagGen.App.Services;

public sealed class Settings
{
    /// <summary>사용자 지정 csv 경로. 비어 있으면 동봉본 사용.</summary>
    public string CustomCsvPath { get; set; } = "";
    public string LastOutputDir { get; set; } = "";
    /// <summary>DefaultLineCount는 실제로 생성 탭의 줄 수(GenerationViewModel.LineCount)를
    /// 재시작 후에도 복원하는 데 쓰인다.</summary>
    public int DefaultLineCount { get; set; } = 100;

    // 아래는 생성 탭의 나머지 설정값들. 재시작해도 이전 값이 남아 있도록
    // GenerationViewModel이 생성 성공 시점과 앱 종료 시점에 저장한다.
    public string LastOutputPath { get; set; } = "";
    public WriteMode LastMode { get; set; } = WriteMode.Append;
    public bool DedupeWithinLine { get; set; } = true;
    public bool AvoidDuplicateLines { get; set; } = true;
    public bool AvoidConflicts { get; set; } = true;
    public bool InsertBlankLine { get; set; } = true;
    public bool UnderscoreToSpace { get; set; } = true;
    public bool WeightedSampling { get; set; }
    public bool AutoOrderTags { get; set; }
    /// <summary>Anima 출력 모드(태그 나열 + 영어 서술문). 기본 꺼짐 —
    /// 지금까지의 컨셉은 Illustrious 계열 기준으로 만든 태그라 기존 동작이 기본이다.</summary>
    public bool AnimaFormat { get; set; }

    /// <summary>한 번이라도 사용자 데이터에 주입한 번들 프리셋 id 목록(PresetSeeder).
    /// 여기 있는 프리셋은 사용자가 지워도 다시 살아나지 않는다 — 삭제도 커스텀이므로.</summary>
    public List<string> SeededPresetIds { get; set; } = new();

    /// <summary>즐겨찾기한 태그명(⭐). 태그 검색의 "⭐ 즐겨찾기" 카테고리가 이 목록을 보여준다.</summary>
    public List<string> FavoriteTags { get; set; } = new();

    /// <summary>즐겨찾기한 레시피 Id(⭐). 레시피 라이브러리의 "⭐ 즐겨찾기만" 필터가 이 목록을 쓴다.</summary>
    public List<string> FavoriteRecipeIds { get; set; } = new();

    // 라이브러리 창 크기(사용자가 조절한 값을 기억). 0 이하면 XAML 기본값 사용.
    public double RecipeLibraryWidth { get; set; }
    public double RecipeLibraryHeight { get; set; }
    public double PoolLibraryWidth { get; set; }
    public double PoolLibraryHeight { get; set; }

    /// <summary>레시피 라이브러리 창 왼쪽 목록 패널의 너비(GridSplitter로 조절). 0 이하면
    /// XAML 기본값(220) 사용. 사용자별 화면 설정이라 git엔 안 올라가고 %APPDATA%에만 남는다.</summary>
    public double RecipeLibraryLeftPanelWidth { get; set; }
}
