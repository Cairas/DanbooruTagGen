using System.Text;
using DanbooruTagGen.Core.Models;

namespace DanbooruTagGen.Core.Generation;

/// <summary>태그 하나를 영어 서술 조각으로 바꾸는 사전. <c>data/anima-phrases.csv</c>에서 로드.
///
/// <para>왜 사전이고 랜덤 풀이 아닌가: 서술문이 랜덤이면 태그 줄엔 <c>standing_sex</c>가 나왔는데
/// 문장엔 "lying on her back"이 붙는 교차 모순이 산문으로 재발한다. 조각을 **그 줄에서 실제로
/// 뽑힌 태그**에 묶으면 두 줄이 구조적으로 어긋날 수 없다.</para>
///
/// <para>영어로 쓰는 이유: Anima의 텍스트 인코더(Qwen)가 다국어를 어느 정도 읽더라도, 학습
/// 분포상 영어가 가장 확실하다. 기존 자연어 문구 50개도 전부 영어다.</para>
///
/// <para>조각이 없는 태그는 **조용히 건너뛴다**. 문장이 조금 짧아질 뿐 깨지지 않는다.
/// 어떤 태그에 조각이 없는지는 <c>tools/anima_phrase_scan.py</c>가 빈도순으로 보고한다.</para>
/// </summary>
public sealed class AnimaPhraseBook
{
    private readonly Dictionary<string, string> _phrases;

    public AnimaPhraseBook(IReadOnlyDictionary<string, string> phrases)
        => _phrases = new Dictionary<string, string>(phrases, StringComparer.Ordinal);

    public static AnimaPhraseBook Empty { get; } = new(new Dictionary<string, string>());
    public int Count => _phrases.Count;

    /// <summary>이 접두어로 시작하는 "태그"는 Anima 서술문에만 쓰라고 표시된 것이다 — Tags 모드
    /// 줄에서는 완전히 빠진다. 그림에 대응하는 danbooru 태그가 없는 배경 설정(예: 과거 사건)을
    /// 굳이 태그 목록에 문자 그대로 끼워 넣고 싶지 않을 때 쓴다. 일반 자연어 문구(공백 포함,
    /// 접두어 없음)는 기존 그대로 Tags 모드에도 나간다 — SD 텍스트 인코더가 읽을 수 있어서
    /// 의도적으로 그렇게 설계됐다(guide.md). 이 접두어는 그 경로를 건드리지 않는 별도 옵트인이다.</summary>
    public const string AnimaOnlyPrefix = "anima-only:";

    public static bool IsAnimaOnly(string tag) => tag.StartsWith(AnimaOnlyPrefix, StringComparison.Ordinal);

    /// <summary>CSV 로드. 형식: <c>tag,phrase</c> (헤더 1줄, '#' 주석/빈 줄 무시).
    /// 쉼표가 든 문장을 위해 큰따옴표 감싸기를 지원한다.</summary>
    public static AnimaPhraseBook LoadFromFile(string path)
    {
        if (!File.Exists(path)) return Empty;
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in File.ReadLines(path, Encoding.UTF8))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            int comma = line.IndexOf(',');
            if (comma <= 0) continue;
            var tag = line[..comma].Trim();
            var phrase = line[(comma + 1)..].Trim();
            if (phrase.Length >= 2 && phrase[0] == '"' && phrase[^1] == '"')
                phrase = phrase[1..^1].Replace("\"\"", "\"");
            if (tag.Length == 0 || phrase.Length == 0) continue;
            if (tag.Equals("tag", StringComparison.OrdinalIgnoreCase)) continue; // 헤더
            map[tag] = phrase;
        }
        return new AnimaPhraseBook(map);
    }

    /// <summary>조각을 찾는다. 값이 <c>-</c>인 항목은 "서술로 옮길 게 없어 일부러 비워 둔"
    /// 메타 태그(hetero 등)라 조각이 없는 것으로 취급한다 — 다만 사전에 적혀 있으므로
    /// 누락 스캐너가 매번 다시 보고하지는 않는다.</summary>
    public bool TryGet(string tag, out string phrase)
    {
        phrase = "";
        if (!_phrases.TryGetValue(tag, out var p) || p == "-") return false;
        phrase = p;
        return true;
    }

    /// <summary>Anima용 한 줄을 만든다.
    /// <list type="bullet">
    /// <item>앞부분: COSMETIC 축을 버린 태그 나열 — 이 모델은 태그 과밀에 민감하다.</item>
    /// <item>뒷부분: 뽑힌 태그의 조각을 역할 순(정체성 → MAJOR → MINOR)으로 이어 붙인 영어 서술.</item>
    /// </list>
    /// 와일드카드 파일은 "한 줄 = 한 프롬프트"라 두 줄로 쪼개지 않고 한 줄에 이어 쓴다.</summary>
    public string Render(IReadOnlyList<(string Tag, SlotRole Role)> parts, bool underscoreToSpace)
    {
        var kept = parts.Where(p => p.Role != SlotRole.Cosmetic).ToList();

        // 자연어 문구는 태그 줄에서 뺀다 — 어차피 뒤 서술문에 그대로 들어가므로 두 번 나가면
        // 중복이고, 태그 과밀에 민감한 모델에 쓸데없이 길이만 먹인다. anima-only 표시 태그는
        // Tags 모드에서도 이미 완전히 빠져 있지만(WildcardGenerator.JoinLine), 여기서도
        // 방어적으로 걸러 접두어 문자열 자체가 태그 줄에 새지 않게 한다.
        var tagText = string.Join(", ", kept
            .Where(p => !p.Tag.Contains(' ') && !IsAnimaOnly(p.Tag))
            .Select(p => underscoreToSpace ? ToDisplay(p.Tag) : p.Tag));

        // 조각은 역할 순으로 모은다 — 누가/무엇을(정체성) → 어떤 상태·어디서(MAJOR) → 표정(MINOR).
        var fragments = new List<string>();
        foreach (var role in new[] { SlotRole.Identity, SlotRole.Major, SlotRole.Minor, SlotRole.Unknown })
        {
            foreach (var (tag, r) in kept)
            {
                if (r != role) continue;
                // anima-only 태그는 사전에서 실제 문구를 찾는다(접두어 자체는 절대 안 나간다).
                if (IsAnimaOnly(tag)) { if (TryGet(tag, out var animaPhrase)) AddFragment(fragments, animaPhrase); continue; }
                // 이미 문장인 것(자연어 문구)은 그대로 쓰고, 태그면 사전을 본다.
                if (tag.Contains(' ')) { AddFragment(fragments, tag); continue; }
                if (TryGet(tag, out var phrase)) AddFragment(fragments, phrase);
            }
        }

        if (fragments.Count == 0) return tagText;

        var sentence = string.Join(", ", fragments);
        if (sentence.Length > 0) sentence = char.ToUpperInvariant(sentence[0]) + sentence[1..];
        return tagText + ". " + sentence + ".";
    }

    private static void AddFragment(List<string> acc, string phrase)
    {
        // 같은 조각이 두 번 나오면(다른 태그가 같은 표현으로 매핑) 문장이 어색해진다.
        if (!acc.Contains(phrase, StringComparer.OrdinalIgnoreCase)) acc.Add(phrase);
    }

    /// <summary><c>tools/anima_phrase_scan.py</c>의 판정 규칙을 그대로 포팅: 비활성 슬롯과
    /// COSMETIC 축은 건너뛰고(Anima 모드에서 애초에 버려지는 태그라 조각이 필요 없음),
    /// 공백이 든 태그(자연어 문구 — <c>anima-only:</c> 접두어는 공백이 없으므로 포함됨)는
    /// 이미 그대로 문장에 들어가므로 사전 조회 대상에서 뺀다. 남은 태그 중 <see cref="TryGet"/>이
    /// 실패하는 것만 모아 돌려준다(중복 제거).</summary>
    public IReadOnlyList<string> FindMissingPhrases(IEnumerable<Slot> slots, IReadOnlyDictionary<string, Pool> poolsById)
    {
        var missing = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slot in slots)
        {
            if (!slot.IsEnabled) continue;
            var role = slot is FixedSlot ? SlotRole.Identity : SlotRoleClassifier.Classify(slot);
            if (role == SlotRole.Cosmetic) continue;

            IEnumerable<string> tags = slot switch
            {
                FixedSlot f => f.Tags,
                RandomPoolSlot r => ResolveRandomPoolTags(r, poolsById),
                AlternativeSlot alt => alt.Groups.SelectMany(g => g.Tags),
                _ => Array.Empty<string>(),
            };

            foreach (var tag in tags)
            {
                if (tag.Contains(' ')) continue;   // 자연어 문구는 그대로 문장에 들어간다
                if (!seen.Add(tag)) continue;
                if (!TryGet(tag, out _)) missing.Add(tag);
            }
        }
        return missing;
    }

    public IReadOnlyList<string> FindMissingPhrases(Recipe recipe, IReadOnlyDictionary<string, Pool> poolsById)
        => FindMissingPhrases(recipe.Slots, poolsById);

    private static IEnumerable<string> ResolveRandomPoolTags(RandomPoolSlot r, IReadOnlyDictionary<string, Pool> poolsById)
    {
        foreach (var t in r.Tags) yield return t;
        if (!string.IsNullOrEmpty(r.PoolId) && poolsById.TryGetValue(r.PoolId, out var pool))
            foreach (var t in pool.Candidates) yield return t;
        foreach (var extraId in r.ExtraPoolIds)
            if (poolsById.TryGetValue(extraId, out var extraPool))
                foreach (var t in extraPool.Candidates) yield return t;
    }

    /// <summary>출력용 표기. '_'를 공백으로 바꾸되 <c>@_@</c>처럼 **글자가 없는 기호 태그**는
    /// 건드리지 않는다 — 바꾸면 "@ @"가 되어 원래 뜻(어질어질한 눈)을 잃는다.</summary>
    internal static string ToDisplay(string tag) =>
        tag.Any(char.IsLetter) ? tag.Replace('_', ' ') : tag;
}
