namespace DanbooruTagGen.Core.Generation;

/// <summary>큐레이션 파일(ko-categories.csv / ko-nsfw.csv)의 4번째 칸에 쓰는 17개 그룹
/// 키워드. 파싱 시 별칭(Aliases)에 한국어 동의어와 함께 섞여 저장되므로, 태그의 그룹은
/// "별칭들 중 이 집합에 속하는 토큰"으로 역추출한다. 순서 자동 배치의 분류 기준이다.</summary>
public static class TagGroupKeywords
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        "표정", "눈", "머리", "포즈", "시점", "의상", "소품", "신체",
        "배경", "형식", "인원", "노출", "행위", "체위", "분비물", "도구", "품질",
    };

    /// <summary>별칭들 중 그룹 키워드를 찾아 반환(없으면 null). 큐레이션 규칙상 그룹
    /// 키워드는 보통 마지막 토큰이지만, 위치에 의존하지 않고 집합 일치로 찾는다.</summary>
    public static string? FromAliases(IEnumerable<string> aliases)
    {
        foreach (var a in aliases)
            if (All.Contains(a)) return a;
        return null;
    }
}
