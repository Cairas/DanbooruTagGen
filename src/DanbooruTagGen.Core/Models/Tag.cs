namespace DanbooruTagGen.Core.Models;

/// <summary>danbooru.csv 카테고리 코드. 색상·필터링에 사용.</summary>
public enum TagCategory
{
    General = 0,
    Artist = 1,
    Copyright = 3,
    Character = 4,
    Meta = 5,
    Unknown = -1,
}

/// <summary>자동완성 사전의 한 항목. CSV 원형을 그대로 보존한다(변환 없음).
/// Description은 선택적 한국어 설명(큐레이션 파일에서 채움). 기본 빈 문자열이라
/// 4-인자 생성도 그대로 동작한다.
/// IsNsfw는 성인 태그 여부(ko-nsfw.csv 출처면 true) — 일반/성인 필터에 사용.</summary>
public sealed record Tag(
    string Name,
    TagCategory Category,
    int PostCount,
    IReadOnlyList<string> Aliases,
    string Description = "",
    bool IsNsfw = false);
