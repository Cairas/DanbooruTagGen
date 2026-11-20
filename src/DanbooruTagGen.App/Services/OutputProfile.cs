namespace DanbooruTagGen.App.Services;

/// <summary>출력 프로필: 모델마다 다른 출력 설정 묶음(품질 태그 · '_'→공백 · 빈 줄 · 자동 정렬). 생성 탭에서 골라 한 번에 바꾼다.</summary>
public sealed class OutputProfile
{
    public string Name { get; set; } = "";
    public bool QualityTagsEnabled { get; set; }
    public string QualityTagsText { get; set; } = "";
    public bool UnderscoreToSpace { get; set; } = true;
    public bool InsertBlankLine { get; set; } = true;
    public bool AutoOrderTags { get; set; }
    /// <summary>이 프로필의 출력을 넣을 모델(토큰을 세는 토크나이저가 달라진다). 예전 프로필(값 없음)은 IL.</summary>
    public Core.Tokenization.ModelKind Model { get; set; } = Core.Tokenization.ModelKind.IL;
    /// <summary>길이 기준(토큰). 0이면 모델 기본값(IL 225 · Anima 512).</summary>
    public int TokenLimit { get; set; }

    public override string ToString() => Name;
}
