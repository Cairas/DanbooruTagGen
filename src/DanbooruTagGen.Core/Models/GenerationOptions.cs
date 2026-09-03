namespace DanbooruTagGen.Core.Models;

/// <summary>랜덤 슬롯의 후보 추첨 방식.
/// Uniform: 후보를 동등 확률로 추첨(기존 동작).
/// Weighted: danbooru 빈도(post count)의 제곱근에 비례해 추첨 — 인기 태그가 더 자주
/// 나오되, 제곱근으로 완만하게 눌러 희귀 태그도 살아남게 한다.</summary>
public enum SamplingMode
{
    Uniform = 0,
    Weighted = 1,
}

public sealed class GenerationOptions
{
    public int LineCount { get; set; } = 100;
    public int? Seed { get; set; }
    public bool DedupeWithinLine { get; set; } = true;
    public bool AvoidDuplicateLines { get; set; } = true;
    /// <summary>모순(상호배타) 태그가 한 줄에 같이 나오면 재추첨해 피한다. 기본 켜짐.</summary>
    public bool AvoidConflicts { get; set; } = true;
    /// <summary>출력 시 '_'를 공백으로 (SD 프롬프트 관례: long_hair -> long hair). 기본 켜짐.</summary>
    public bool UnderscoreToSpace { get; set; } = true;
    /// <summary>랜덤 슬롯 추첨 방식. 기본 Uniform(기존 동작 보존). 빈도 메타데이터가
    /// 주입되지 않으면 Weighted여도 Uniform으로 폴백한다.</summary>
    public SamplingMode Sampling { get; set; } = SamplingMode.Uniform;
    /// <summary>한 줄의 태그를 표준 프롬프트 순서(품질→인원→…→배경→형식)로 재배치한다.
    /// 기본 꺼짐. 그룹/카테고리 메타데이터가 주입되지 않으면 무시된다.</summary>
    public bool AutoOrderTags { get; set; }
    public List<string> Blocklist { get; set; } = new();
}
