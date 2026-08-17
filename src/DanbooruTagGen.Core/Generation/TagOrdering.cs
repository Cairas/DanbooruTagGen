using DanbooruTagGen.Core.Models;

namespace DanbooruTagGen.Core.Generation;

/// <summary>한 줄의 태그를 표준 프롬프트 순서로 재배치한다.
///
/// 설계 의도: Danbooru 계열 모델은 태그 "순서"에 민감하다(앞쪽 태그가 더 강하게 반영).
/// 그래서 품질→인원→캐릭터→외형→의상→행위→소품→배경→형식 순으로 정렬하면 사용자
/// 의도대로 그려질 확률이 높아진다. 분류 기준은 큐레이션한 17개 그룹 키워드다.
///
/// 미분류(큐레이션 안 된) 태그는 임의로 옮기면 의도를 해칠 수 있으므로 "원래 위치"에
/// 고정하고, 분류된 태그만 그 사이 빈칸에 표준 순서로 끼워 넣는다(안정적·예측 가능).</summary>
public sealed class TagOrdering
{
    // 표준 순서(앞→뒤). 값이 작을수록 앞. 캐릭터/작품은 그룹 키워드가 아니라
    // danbooru 카테고리로 식별하므로 별도 상수(CharacterRank).
    private static readonly IReadOnlyDictionary<string, int> GroupRank =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["품질"] = 0,
            ["인원"] = 1,
            // 캐릭터/작품 = 2
            ["신체"] = 3,
            ["머리"] = 4,
            ["눈"] = 5,
            ["표정"] = 6,
            ["의상"] = 7,
            ["노출"] = 8,
            ["포즈"] = 9,
            ["행위"] = 10,
            ["체위"] = 11,
            ["분비물"] = 12,
            ["도구"] = 13,
            ["소품"] = 14,
            ["시점"] = 15,
            ["배경"] = 16,
            ["형식"] = 17,
        };

    private const int QualityRank = 0;
    private const int CharacterRank = 2;
    /// <summary>미분류 표식. 정렬 대상에서 제외하고 원래 위치에 고정한다.</summary>
    private const int Unranked = int.MaxValue;

    // 품질 태그는 별칭에 그룹 키워드 '품질'이 없을 수 있어(예: extra-quality-tags.csv) 이름으로도 식별.
    private static readonly IReadOnlySet<string> QualityNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "masterpiece", "best_quality", "high_quality", "normal_quality", "low_quality", "worst_quality",
        "amazing_quality", "great_quality", "absurdres", "highres", "lowres",
    };

    private readonly ITagLookup _lookup;

    public TagOrdering(ITagLookup lookup) => _lookup = lookup;

    /// <summary>분류된 태그만 표준 순서로 재배치하고, 미분류 태그는 원래 인덱스에 고정한다.
    /// 같은 등급(rank)의 태그끼리는 입력 등장 순서를 보존한다(안정 정렬).</summary>
    public List<string> Reorder(IReadOnlyList<string> tags)
    {
        int n = tags.Count;
        var ranks = new int[n];
        var classified = new List<int>(n); // 분류된 입력 인덱스(증가 순서 → 안정성 보장)
        for (int i = 0; i < n; i++)
        {
            ranks[i] = RankOf(tags[i]);
            if (ranks[i] != Unranked) classified.Add(i);
        }

        // 분류된 태그를 rank 오름차순으로 안정 정렬(동률은 원래 순서 유지).
        var sorted = classified
            .Select((idx, ord) => (idx, ord))
            .OrderBy(x => ranks[x.idx])
            .ThenBy(x => x.ord)
            .Select(x => tags[x.idx])
            .ToList();

        // 미분류 위치는 원본 그대로, 분류 위치(빈칸)에는 정렬 결과를 순서대로 채운다.
        var result = new List<string>(n);
        int s = 0;
        for (int i = 0; i < n; i++)
            result.Add(ranks[i] == Unranked ? tags[i] : sorted[s++]);
        return result;
    }

    /// <summary>태그의 표준 순서 등급. 품질 이름 → 0, 그룹 키워드 → 표, 캐릭터/작품/작가 카테고리 → 2,
    /// 그 외(미분류) → Unranked.</summary>
    internal int RankOf(string tag)
    {
        if (QualityNames.Contains(tag)) return QualityRank;

        var info = _lookup.Lookup(tag);
        if (info is null) return Unranked;

        var group = TagGroupKeywords.FromAliases(info.Aliases);
        if (group is not null && GroupRank.TryGetValue(group, out var rank)) return rank;

        // 작가(Artist) 태그는 캐릭터/저작권과 같은 자리에 둔다 — Animagine/NoobAI 가이드가
        // 공통으로 "작가 태그는 캐릭터/시리즈 바로 뒤"라고 요구하는데, 예전엔 이 카테고리를
        // 안 잡아서 미분류로 원래 위치에 방치됐었다.
        if (info.Category is TagCategory.Character or TagCategory.Copyright or TagCategory.Artist) return CharacterRank;

        return Unranked;
    }
}
