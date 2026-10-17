using System.Text.RegularExpressions;
using DanbooruTagGen.Core.Generation;

namespace DanbooruTagGen.Core.Output;

/// <summary>생성된 줄들의 길이 요약. <see cref="OverBudget"/>는 예산을 넘은 줄 수.
/// 위치 인자(항목 수)는 예전 그대로고, 토큰 예산 쪽은 init 속성으로 덧붙였다(기존 생성·비교 코드 호환).</summary>
public sealed record LineLengthStats(int LineCount, int Min, int Max, double Average, int OverBudget, int Budget)
{
    /// <summary>줄당 자연어(서술 문장) 단어 수 평균·최대. 태그가 아닌 항목만 센다(<see cref="LineStats.IsPhraseItem"/>).</summary>
    public double PhraseWordsAverage { get; init; }
    public int PhraseWordsMax { get; init; }

    /// <summary>줄당 추정 CLIP 토큰 수 평균·최대(<see cref="LineStats.EstimateClipTokens"/> — 거친 근사).</summary>
    public double TokensAverage { get; init; }
    public int TokensMax { get; init; }

    /// <summary>75토큰 청크 수의 최대, 그리고 청크가 2개 이상 필요한 줄 수.</summary>
    public int ChunksMax { get; init; }
    public int MultiChunkLines { get; init; }
}

/// <summary>줄 길이(태그 개수) + 토큰 예산 점검.
///
/// 왜 필요한가: guide.md의 "줄 길이 예산"은 한 줄 24~27태그를 목표로 한다. 예산을 넘으면
/// 뒤쪽 태그가 밀려 반영이 약해지는데, 지금까지는 뽑은 줄을 눈으로 세어 보는 수밖에 없었다.
/// 항목 수만으로는 자연어 문장 하나가 태그 열 개만큼 토큰을 먹는 걸 못 본다 — CLIP은 75토큰 단위
/// 청크로 끊어 읽으므로(청크가 넘어가면 앞뒤 청크가 따로 인코딩돼 결합이 약해진다), 토큰 추정과
/// 청크 수를 같이 보여 준다.</summary>
public static class LineStats
{
    /// <summary>guide.md의 목표 상한(24~27태그). 이보다 길면 뒤쪽 태그가 묻히기 시작한다.</summary>
    public const int DefaultBudget = 27;

    /// <summary>CLIP 텍스트 인코더 한 청크의 토큰 수(시작·끝 토큰 2개를 뺀 77-2). A1111/ComfyUI가 이 단위로 끊는다.</summary>
    public const int ClipChunkTokens = 75;

    /// <summary>단어 하나당 평균 BPE 토큰 수 근사. 흔한 영어 낱말은 1토큰이지만 danbooru 고유어(ahegao,
    /// torogao)·합성어는 2~3토큰으로 쪼개져, 실측 감으로 1.3배를 쓴다.</summary>
    private const double TokensPerWord = 1.3;

    /// <summary>사전 없이 자연어를 가를 때의 최소 단어 수. 출력 줄은 '_'→공백 변환을 거쳐 "long hair"와 문장을
    /// 글자만으로 구별할 수 없는데, danbooru 태그는 4단어(hand on another's head)까지는 흔하고 5단어부터
    /// 드물다.</summary>
    private const int PhraseMinWordsWithoutLookup = 5;

    private static readonly Regex WordRun = new(@"[\p{L}\p{N}]+", RegexOptions.Compiled);

    public static LineLengthStats Measure(IReadOnlyList<string> lines, int budget = DefaultBudget, ITagLookup? lookup = null)
    {
        int count = 0, min = int.MaxValue, max = 0, over = 0;
        long total = 0, phraseTotal = 0, tokenTotal = 0;
        int phraseMax = 0, tokenMax = 0, chunkMax = 0, multiChunk = 0;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;   // 파일 구분용 빈 줄은 세지 않는다
            var items = Items(line);
            int tags = items.Count;
            count++;
            total += tags;
            if (tags < min) min = tags;
            if (tags > max) max = tags;
            if (tags > budget) over++;

            int phraseWords = 0;
            foreach (var item in items)
                if (IsPhraseItem(item, lookup)) phraseWords += WordCount(TagKey.StripWeight(item, out _));
            phraseTotal += phraseWords;
            if (phraseWords > phraseMax) phraseMax = phraseWords;

            int tokens = EstimateClipTokens(line);
            tokenTotal += tokens;
            if (tokens > tokenMax) tokenMax = tokens;
            int chunks = ChunksOf(tokens);
            if (chunks > chunkMax) chunkMax = chunks;
            if (chunks > 1) multiChunk++;
        }

        if (count == 0) return new LineLengthStats(0, 0, 0, 0, 0, budget);
        return new LineLengthStats(count, min, max, Math.Round((double)total / count, 1), over, budget)
        {
            PhraseWordsAverage = Math.Round((double)phraseTotal / count, 1),
            PhraseWordsMax = phraseMax,
            TokensAverage = Math.Round((double)tokenTotal / count, 1),
            TokensMax = tokenMax,
            ChunksMax = chunkMax,
            MultiChunkLines = multiChunk,
        };
    }

    /// <summary>한 줄의 항목(깊이 0 쉼표로 자른 것). 괄호 안의 쉼표는 자르지 않는다 — 자연어 문구가 쉼표를 품고
    /// 있어서("(she is dragged in, still in shock:1.2)") 그냥 쪼개면 개수가 부풀어 오른다. 판정 키 분해와 같은
    /// <see cref="TagKey.SplitList"/>를 쓴다(괄호 짝이 안 맞으면 단순 쉼표 분할 — <c>:(</c> 이모티콘 태그 뒤 항목도 센다).</summary>
    private static IReadOnlyList<string> Items(string line) => TagKey.SplitList(line);

    /// <summary>항목 하나가 자연어(서술 문장)인가. 가중치 표기를 벗긴 본문 기준.
    /// 사전이 있으면: 사전에 있는 태그(공백↔'_' 동일시)면 태그, 아니고 공백이 있으면 자연어.
    /// 사전이 없으면: <see cref="PhraseMinWordsWithoutLookup"/>단어 이상이면 자연어(근사).</summary>
    internal static bool IsPhraseItem(string item, ITagLookup? lookup)
    {
        var core = TagKey.StripWeight(item, out _);
        if (!core.Any(char.IsWhiteSpace)) return false;
        if (lookup != null) return lookup.Lookup(TagKey.Normalize(core)) is null;
        return WordCount(core) >= PhraseMinWordsWithoutLookup;
    }

    /// <summary>자연어 "단어 수" = 공백으로 나뉜 낱말 수(another's는 한 단어). 토큰 추정의 글자 덩어리 수와 다르다.</summary>
    private static int WordCount(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>한 줄의 CLIP 토큰 수 거친 추정(정밀 토크나이저 아님):
    /// <c>round(1.3 × 단어 수) + 구두점 수</c>.
    /// <list type="bullet">
    /// <item>단어 = 글자·숫자 연속(공백·'_'·하이픈·아포스트로피에서 끊긴다).</item>
    /// <item>구두점 = <c>, . ; ! ? ' - _ / &amp;</c> 하나당 1토큰(CLIP BPE가 따로 떼어 낸다). 쉼표는 항목 구분자든
    /// 문장 속 쉼표든 모두 센다.</item>
    /// <item>강조 문법 <c>(본문:1.2)</c>의 괄호와 가중치 숫자는 빼고 센다 — A1111/ComfyUI는 이걸 토큰으로 넣지 않고
    /// 가중치로 처리한다. 다른 괄호(<c>piledriver_(sex)</c>)는 남는 글자대로 센다.</item>
    /// </list>
    /// 오차: 흔한 영어 문장은 ±15% 안팎, 희귀 고유명사가 많으면 과소 추정된다. 청크 경계 판단용 눈대중으로만 쓸 것.</summary>
    public static int EstimateClipTokens(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return 0;
        int words = 0, punct = 0, separators = 0;
        var items = Items(line);
        foreach (var item in items)
        {
            var core = TagKey.StripWeight(item, out _);
            words += WordRun.Matches(core).Count;
            foreach (var c in core)
                if (c is ',' or '.' or ';' or '!' or '?' or '\'' or '-' or '_' or '/' or '&') punct++;
        }
        if (items.Count > 1) separators = items.Count - 1;
        return (int)Math.Round(words * TokensPerWord, MidpointRounding.AwayFromZero) + punct + separators;
    }

    /// <summary>토큰 수 → 75토큰 청크 수(비어 있지 않으면 최소 1).</summary>
    public static int ChunksOf(int tokens) => tokens <= 0 ? 0 : (tokens + ClipChunkTokens - 1) / ClipChunkTokens;
}
