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

    /// <summary>매 줄 맨 앞에 품질/안전 등급 태그를 붙일지. 기본 꺼짐 — 이미 자기만의
    /// 품질 태그를 ComfyUI 쪽에서 따로 쓰는 사용자를 위한 옵트인. 이 기능이 있는 이유는
    /// 그런 워크플로를 안 가진 사용자도 이 프로그램만으로 바로 완성된 프롬프트를 뽑을 수
    /// 있게 하기 위함이다.</summary>
    public bool QualityTagsEnabled { get; set; }
    /// <summary>QualityTagsEnabled가 켜졌을 때 매 줄 앞에 붙는 텍스트. 사용자가 자유롭게 수정 가능.</summary>
    public string QualityTagsText { get; set; } =
        "masterpiece, score_9, score_8, score_7, score_6, best quality, amazing quality, very aesthetic, extremely detailed, very detailed, absurdres, newest, highres, uncensored";

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
