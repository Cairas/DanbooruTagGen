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

/// <summary>출력 형식. 모델마다 원하는 프롬프트 모양이 달라서 나눈다.
/// <para>
/// Tags(기본): Illustrious/NoobAI 계열용. 태그를 콤마로 나열한 한 줄. 태그가 많을수록 좋다.
/// </para><para>
/// Anima: Anima 계열용. 이 모델은 CLIP이 아니라 Qwen LLM을 텍스트 인코더로 써서 자연어를
/// 잘 읽는 대신 **태그 과밀에 민감**하다. 그래서 COSMETIC 축(구도·조명·유두 디테일·체액)을
/// 버려 태그 수를 줄이고, 뽑힌 태그에서 만든 영어 서술문을 뒤에 덧붙인다.
/// 와일드카드 파일은 "한 줄 = 한 프롬프트"라서 두 줄로 쪼개지 않고 한 줄에 이어 쓴다.
/// </para></summary>
public enum PromptFormat
{
    Tags = 0,
    Anima = 1,
}

public sealed class GenerationOptions
{
    public int LineCount { get; set; } = 100;
    /// <summary>출력 형식. 기본 Tags(기존 동작 그대로).</summary>
    public PromptFormat Format { get; set; } = PromptFormat.Tags;
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
