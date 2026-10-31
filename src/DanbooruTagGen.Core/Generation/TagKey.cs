using System.Globalization;
using System.Text;
using DanbooruTagGen.Core.Models;

namespace DanbooruTagGen.Core.Generation;

/// <summary>후보 문자열 하나를 이루는 조각. <see cref="Text"/>는 가중치 문법을 벗긴 본문(원래 대소문자),
/// <see cref="Key"/>는 판정용 정규화 키(<see cref="TagKey.Normalize"/>).
/// <see cref="IsImplied"/>: <c>implies:태그</c>로 쓴 판정 전용 조각 — 출력 줄에는 안 나가지만 충돌·검증
/// 같은 판정에서는 그 태그가 있는 것처럼 본다(<see cref="Text"/>는 접두어를 뗀 태그명).</summary>
public readonly record struct TagFragment(string Text, string Key, bool IsImplied = false)
{
    /// <summary>본문에 공백이 있으면 서술 표현(자연어)이다 — 실제 danbooru 태그는 전부 언더스코어로
    /// 저장된다(<see cref="RecipeValidator"/>와 같은 기준).</summary>
    public bool IsPhrase => Text.Any(char.IsWhiteSpace);
}

/// <summary>후보 문자열 앞의 추첨 가중치 접두 <c>N::</c>를 읽은 결과.
/// <see cref="Explicit"/>=false면 접두가 없던 것(가중치 1). <see cref="Error"/>가 있으면 접두 모양은
/// 맞지만 값이 잘못된 것(소수 등) — <see cref="WildcardGenerator.Validate"/>가 막는다.</summary>
public readonly record struct PickWeightSpec(int Weight, string Body, bool Explicit, string? Error);

/// <summary>판정(식별) 전용 태그 키.
///
/// <para>레시피 후보 문자열은 순수 태그(<c>vaginal</c>)만이 아니라 가중치 태그(<c>(vaginal:1.2)</c>),
/// 자연어 문장(<c>(she turns, glancing back:1.1)</c>), 문장 + 동반 태그 묶음
/// (<c>(a fresh bite mark…:1.1), bite_mark</c>)까지 섞여 있다. 출력은 원문 그대로 써야 하지만,
/// 충돌·중복·블록리스트·가중 추첨·순서 배치·검증은 "이 문자열에 어떤 태그가 들어 있나"를 알아야
/// 한다. 예전엔 각자 원문 문자열을 통째로 비교해서 묶음 안의 태그와 가중치 태그를 전부 놓쳤다.
/// 이 클래스가 그 판정을 한 곳으로 모은다. <c>tools/conflict_scan.py</c>의 split_fragments가 같은
/// 규칙의 파이썬 포팅이다 — 한쪽을 고치면 다른 쪽도 같이 고칠 것.</para>
///
/// <para>규칙:
/// 1) 괄호 밖(깊이 0)의 쉼표에서만 자른다. 괄호 안 쉼표(문장 속 쉼표)는 보존한다.
/// 2) 조각 전체가 <c>(본문:가중치)</c> 또는 <c>(본문:LOW~HIGH)</c>로 싸여 있으면 본문만 남긴다.
///    <see cref="WeightRange.Resolve"/>가 범위를 값으로 바꾼 뒤든 전이든 같은 키가 나온다.
/// 3) 괄호 밖에 공백 섞인 맨 조각이 하나라도 있으면 그 문자열은 괄호 없는 자연어 문장이다
///    (<c>thick, viscous cum dripping…</c>) — 그 쉼표는 문장 부호이므로 자르지 않고 통째로 한
///    조각으로 둔다. 안 그러면 문장 속 낱말(<c>thick</c>, <c>submission</c>)이 태그로 둔갑한다.
/// 4) 괄호 짝이 안 맞으면(예: <c>:(</c> 이모티콘) 자르지 않는다(예전 동작 그대로).
/// 5) 키 = 소문자 + 공백 연속을 '_' 하나로. <see cref="Tags.TagDatabase"/>의 검색 정규화
///    (소문자, '_'↔공백 동일시)와 같은 동치 관계다.
/// 6) 문자열 맨 앞의 <c>N::</c>(정수 N)는 랜덤 풀 후보의 추첨 가중치다(<see cref="ParsePickWeight"/>).
///    판정·출력 어디에도 포함되지 않으므로 맨 먼저 벗긴다. Dynamic Prompts의 <c>{N::a|b}</c> 관례와 같은
///    모양이고, 레시피·풀 데이터와 danbooru 사전 어디에도 <c>::</c>가 쓰이지 않아 기존 문자열과 겹치지 않는다.
/// 7) 깊이 0 조각이 <c>implies:태그</c>면 판정 전용 조각(<see cref="TagFragment.IsImplied"/>)이다. 출력은
///    <see cref="OutputText"/>가 뺀다. 이 조각은 규칙 3(맨 자연어 문장 판정)에 참여하지 않는다 —
///    "she peeks through the gap, implies:voyeurism"처럼 괄호 없는 문장 뒤에 붙여도 잡혀야 한다.</para></summary>
public static class TagKey
{
    /// <summary>판정 전용 조각 접두어. 대소문자 무시.</summary>
    public const string ImpliesPrefix = "implies:";

    /// <summary>추첨 가중치 접두 구분자.</summary>
    private const string PickWeightSeparator = "::";

    /// <summary>판정용 정규화: 앞뒤 공백 제거, 소문자, 공백 연속 → '_'.</summary>
    public static string Normalize(string s)
    {
        s = s.Trim();
        bool simple = true;
        foreach (var c in s)
            if (char.IsWhiteSpace(c) || char.IsUpper(c)) { simple = false; break; }
        if (simple) return s;

        var sb = new StringBuilder(s.Length);
        bool inSpace = false;
        foreach (var c in s.ToLowerInvariant())
        {
            if (char.IsWhiteSpace(c))
            {
                if (!inSpace) sb.Append('_');
                inSpace = true;
            }
            else
            {
                sb.Append(c);
                inSpace = false;
            }
        }
        return sb.ToString();
    }

    /// <summary>문자열을 판정용 조각으로 분해한다. 항상 하나 이상을 돌려준다.</summary>
    public static IReadOnlyList<TagFragment> Split(string raw)
    {
        raw ??= "";
        if (raw.Contains(PickWeightSeparator, StringComparison.Ordinal)) raw = ParsePickWeight(raw).Body;

        // 대부분의 순수 태그는 여기서 끝난다.
        if (raw.IndexOf(',') < 0 && raw.IndexOf('(') < 0)
            return new[] { TryImplied(raw, out var single) ? single : Make(raw) };

        var pieces = SplitTopLevel(raw);
        if (pieces is null) return new[] { Make(raw.Trim()) };   // 괄호 짝 불일치 → 통째로

        // 규칙 7: implies: 조각은 따로 떼어 둔다(규칙 3의 "맨 문장" 판정에서 빠진다).
        List<TagFragment>? implied = null;
        if (raw.Contains(ImpliesPrefix, StringComparison.OrdinalIgnoreCase))
        {
            for (int i = pieces.Count - 1; i >= 0; i--)
                if (TryImplied(pieces[i], out var f))
                {
                    (implied ??= new List<TagFragment>()).Insert(0, f);
                    pieces.RemoveAt(i);
                }
            if (pieces.Count == 0) return implied!;
        }
        // 맨 문장일 때 통째로 둘 본문. implies:를 뗀 경우에만 다시 잇는다 — 안 뗐으면 원문 그대로여야
        // 기존 문자열의 키가 1-1 때와 한 글자도 달라지지 않는다.
        string whole = implied is null ? raw.Trim() : string.Join(", ", pieces);

        var frags = new List<TagFragment>(pieces.Count + (implied?.Count ?? 0));
        foreach (var p in pieces)
        {
            var core = StripWeight(p, out bool weighted);
            // 규칙 3: 가중치로 싸이지 않은 공백 조각 = 괄호 없는 자연어 문장 → 자르지 않는다.
            if (pieces.Count > 1 && !weighted && core.Any(char.IsWhiteSpace))
            {
                frags.Clear();
                frags.Add(Make(whole));
                break;
            }
            frags.Add(new TagFragment(core, Normalize(core)));
        }
        if (frags.Count == 0) frags.Add(Make(whole));
        if (implied != null) frags.AddRange(implied);
        return frags;
    }

    /// <summary>후보 문자열 맨 앞의 추첨 가중치 접두 <c>N::</c>를 읽는다(정수 N ≥ 0).
    /// 접두가 없으면 가중치 1·원문 그대로. <c>0.5::</c>처럼 숫자지만 정수가 아니면 <see cref="PickWeightSpec.Error"/>
    /// — ALT 그룹 weight에 소수를 넣었다가 레시피가 통째로 조용히 빠진 사고가 있어, 여기서는 조용히 넘기지
    /// 않고 생성 전 검증에서 명시적으로 막는다.</summary>
    public static PickWeightSpec ParsePickWeight(string raw)
    {
        raw ??= "";
        int sep = raw.IndexOf(PickWeightSeparator, StringComparison.Ordinal);
        if (sep <= 0) return new PickWeightSpec(1, raw, false, null);

        var head = raw[..sep].Trim();
        if (head.Length == 0 || !head.All(c => char.IsAsciiDigit(c) || c == '.' || c == '-' || c == '+'))
            return new PickWeightSpec(1, raw, false, null);   // "a::b"처럼 숫자 접두가 아니면 문법이 아니다

        var body = raw[(sep + PickWeightSeparator.Length)..].TrimStart();
        if (head.All(char.IsAsciiDigit) && int.TryParse(head, NumberStyles.None, CultureInfo.InvariantCulture, out int w))
            return new PickWeightSpec(w, body, true, null);
        return new PickWeightSpec(1, body, true,
            $"추첨 가중치 '{head}::'는 0 이상의 정수여야 합니다(소수·음수 불가). 예: 3::tag");
    }

    /// <summary>출력 줄에 실제로 쓸 문자열: 추첨 가중치 접두와 <c>implies:</c> 조각을 뺀다. 남는 게 없으면 "".
    /// 둘 다 없는 문자열(기존 데이터 전부)은 원문을 **그대로** 돌려준다 — 출력 포맷이 바뀌면 안 된다.
    /// implies: 조각을 뺄 때만 남은 깊이 0 조각을 ", "로 다시 잇는다.</summary>
    public static string OutputText(string raw)
    {
        raw ??= "";
        if (raw.Contains(PickWeightSeparator, StringComparison.Ordinal)) raw = ParsePickWeight(raw).Body;
        if (!raw.Contains(ImpliesPrefix, StringComparison.OrdinalIgnoreCase)) return raw;

        var pieces = SplitTopLevel(raw);
        if (pieces is null) return IsImpliedPiece(raw) ? "" : raw;   // 괄호 짝 불일치 → 통째로 판단
        var kept = pieces.Where(p => !IsImpliedPiece(p)).ToList();
        if (kept.Count == pieces.Count) return raw;
        return string.Join(", ", kept);
    }

    private static bool IsImpliedPiece(string piece) =>
        piece.TrimStart().StartsWith(ImpliesPrefix, StringComparison.OrdinalIgnoreCase);

    private static bool TryImplied(string piece, out TagFragment fragment)
    {
        fragment = default;
        if (!IsImpliedPiece(piece)) return false;
        var name = piece.TrimStart()[ImpliesPrefix.Length..].Trim();
        if (name.Length == 0) return false;
        fragment = new TagFragment(name, Normalize(name), IsImplied: true);
        return true;
    }

    /// <summary>이미 한 항목으로 잘린 문자열(예: conflicts.csv 멤버 하나)의 판정 키 — 가중치만 벗기고
    /// 정규화한다(쉼표로 더 자르지 않는다).</summary>
    public static string KeyOf(string piece) => Normalize(StripWeight(piece, out _));

    /// <summary>"a, b, (문장, 쉼표:1.2)"처럼 **목록**으로 쓴 칸(conflicts.csv의 태그·면제 칸)을 괄호 깊이를
    /// 지키며 자른다. <see cref="Split"/>와 달리 공백 조각이 있어도 통째로 두지 않는다(목록이므로).
    /// 괄호 짝이 안 맞으면 단순 쉼표 분할로 폴백(예전 동작).
    /// <para>괄호 깊이는 구조 괄호(<see cref="IsStructuralParen"/>)로 센다 — 이모티콘(<c>:(</c>)·이스케이프된 괄호
    /// (<c>piledriver \(sex\)</c>, 출력 줄)는 깊이에 안 들어간다. 그래서 "(a, b:1.2), :("도 문장이 안 쪼개진다
    /// (예전엔 짝 불일치로 폴백해 쪼갰다). 이모티콘·이스케이프가 없는 문자열은 예전과 결과가 같다. 앱 UI의 쉼표
    /// 목록 입력칸(태그 직접 입력·ALT 그룹 추가·훅 태그)도 이 함수로 자른다(2-4).</para></summary>
    public static IReadOnlyList<string> SplitList(string field)
    {
        field ??= "";
        var match = MatchStructuralParens(field, out int unmatched);
        if (unmatched > 0)
            return field.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        var result = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < field.Length; i++)
        {
            char c = field[i];
            if (c == ',' && depth == 0)
            {
                AddPiece(field, start, i, result);
                start = i + 1;
            }
            else if (match[i] >= 0) depth += c == '(' ? 1 : -1;
        }
        AddPiece(field, start, field.Length, result);
        return result;
    }

    /// <summary>문자열 하나의 판정 키들(조각 순서).</summary>
    public static IEnumerable<string> Keys(string raw) => Split(raw).Select(f => f.Key);

    /// <summary>여러 문자열(한 줄, 한 슬롯 등)의 판정 키 합집합.</summary>
    public static HashSet<string> KeySet(IEnumerable<string> raws)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in raws)
            foreach (var f in Split(r)) set.Add(f.Key);
        return set;
    }

    /// <summary>문자열 안에서 사전에 실제로 있는 **출력** 태그만(조각 순서). 사전에 없는 조각(자연어·오탈자)은
    /// "매칭 불가"로 보고 뺀다. <c>implies:</c> 조각도 뺀다 — 이 결과는 빈도 가중 추첨이 쓰는데, 거기서
    /// 묻는 건 "이 후보가 그림에 얼마나 잘 나오나"이고 implies: 태그는 그림에 안 나간다.</summary>
    public static List<Tag> KnownTags(string raw, ITagLookup lookup)
    {
        var result = new List<Tag>();
        foreach (var f in Split(raw))
            if (!f.IsImplied && lookup.Lookup(f.Key) is { } t) result.Add(t);
        return result;
    }

    private static TagFragment Make(string s)
    {
        var core = StripWeight(s, out _);
        return new TagFragment(core, Normalize(core));
    }

    /// <summary>깊이 0(괄호 밖)에 있는 <paramref name="separator"/>의 위치들. 괄호 짝이 안 맞으면 null.
    /// conflicts.csv의 <c>A,B vs C,D</c> 편 구분처럼, 괄호 안 자연어 멤버 속 같은 글자를 구분자로 오인하면
    /// 안 되는 곳에서 쓴다.</summary>
    internal static List<int>? IndexesOfTopLevel(string raw, string separator)
    {
        var result = new List<int>();
        int depth = 0;
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (c == '(') depth++;
            else if (c == ')') { if (--depth < 0) return null; }
            else if (depth == 0 && string.CompareOrdinal(raw, i, separator, 0, separator.Length) == 0)
            {
                result.Add(i);
                i += separator.Length - 1;
            }
        }
        return depth == 0 ? result : null;
    }

    /// <summary>괄호 깊이를 추적하며 깊이 0의 쉼표에서만 자른다. 빈 조각은 버린다.
    /// 짝이 안 맞으면 null.</summary>
    private static List<string>? SplitTopLevel(string raw)
    {
        var result = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (c == '(') depth++;
            else if (c == ')')
            {
                if (--depth < 0) return null;
            }
            else if (c == ',' && depth == 0)
            {
                AddPiece(raw, start, i, result);
                start = i + 1;
            }
        }
        if (depth != 0) return null;
        AddPiece(raw, start, raw.Length, result);
        return result;
    }

    private static void AddPiece(string raw, int from, int to, List<string> into)
    {
        var piece = raw[from..to].Trim();
        if (piece.Length > 0) into.Add(piece);
    }

    /// <summary>조각 전체가 <c>(본문:가중치)</c>/<c>(본문:LOW~HIGH)</c>이면 본문만 남긴다(중첩도 벗김).
    /// 여는 괄호가 맨 끝 닫는 괄호와 짝일 때만 — <c>(a:1.1) and (b:1.2)</c>나 <c>piledriver_(sex)</c>는
    /// 건드리지 않는다.</summary>
    internal static string StripWeight(string piece, out bool weighted)
    {
        weighted = false;
        var s = piece.Trim();
        while (s.Length >= 2 && s[0] == '(' && s[^1] == ')' && ClosingParenOf(s) == s.Length - 1)
        {
            var inner = s[1..^1];
            int colon = inner.LastIndexOf(':');
            if (colon <= 0 || !IsWeight(inner[(colon + 1)..])) break;
            s = inner[..colon].Trim();
            weighted = true;
        }
        return s;
    }

    /// <summary>s[0]의 '('와 짝인 ')'의 인덱스. 없으면 -1.</summary>
    private static int ClosingParenOf(string s)
    {
        int depth = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')' && --depth == 0) return i;
        }
        return -1;
    }

    // ───────────── 출력 이스케이프·문법 점검 (2-9, 2-5) ─────────────
    //
    // 위의 판정 키 분해(Split/StripWeight)는 1-1 때와 키가 한 글자도 달라지면 안 되므로 손대지 않고, 출력·점검
    // 전용으로 "구조 괄호" 판정을 따로 둔다. 다른 점은 둘뿐이다:
    //   • 이미 이스케이프된 \( \)는 글자 그대로(구조 아님) — A1111에서 복사해 온 문자열을 두 번 이스케이프하지 않는다.
    //   • 이모티콘 괄호(':' 또는 ';' 바로 뒤의 괄호 — :) :( ;) >:( 등)는 글자 그대로 — 짝 검사에서 빼야
    //     "smile, :(" 같은 정상 태그를 "괄호 짝 불일치"로 몰지 않는다.
    // (본문:가중치)를 알아보는 기준(맨 끝 ':' 뒤가 IsWeight)은 StripWeight와 같다.

    /// <summary>s[i]가 구조 괄호(강조 문법을 이룰 수 있는 괄호)인가. 이스케이프된 괄호와 이모티콘 괄호는 아니다.</summary>
    private static bool IsStructuralParen(string s, int i)
    {
        if (s[i] != '(' && s[i] != ')') return false;
        if (i == 0) return true;
        char prev = s[i - 1];
        return prev != '\\' && prev != ':' && prev != ';';
    }

    /// <summary>구조 괄호의 짝 위치(여는 쪽 인덱스 → 닫는 쪽, 닫는 쪽 → 여는 쪽). 짝 없는 구조 괄호와 비구조
    /// 괄호·다른 글자는 -1. <paramref name="unmatched"/>: 짝 없는 구조 괄호 수.</summary>
    private static int[] MatchStructuralParens(string s, out int unmatched)
    {
        var match = new int[s.Length];
        Array.Fill(match, -1);
        var stack = new Stack<int>();
        unmatched = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (!IsStructuralParen(s, i)) continue;
            if (s[i] == '(') stack.Push(i);
            else if (stack.Count > 0) { int o = stack.Pop(); match[o] = i; match[i] = o; }
            else unmatched++;
        }
        unmatched += stack.Count;
        return match;
    }

    /// <summary>출력 줄 조각의 리터럴 괄호를 <c>\(</c>/<c>\)</c>로 이스케이프한다(A1111/ComfyUI 프롬프트 문법).
    /// <c>piledriver_(sex)</c>처럼 태그 이름에 든 괄호를 그냥 내보내면 강조 그룹으로 읽혀 태그가 깨진다.
    /// <list type="bullet">
    /// <item>가중치 문법 <c>(본문:1.2)</c>의 바깥 괄호는 그대로 둔다(진짜 강조 문법). 본문 안은 재귀로 같은 처리 —
    /// <c>(piledriver_(sex):1.2)</c> → <c>(piledriver_\(sex\):1.2)</c>, <c>((x:1.1):1.2)</c>는 그대로.</item>
    /// <item>가중치 없는 강조 그룹 <c>(문장)</c>·<c>((tag))</c>도 낱말 시작에서 열렸으면 강조 문법으로 보고 바깥을 둔다 —
    /// 작성자가 쓴 문법을 출력 단계가 멋대로 글자로 바꾸면 안 된다(실데이터엔 없음, 사용자가 직접 칠 수 있는 형태).</item>
    /// <item>그 밖의 괄호(낱말에 붙은 태그명 괄호, 이모티콘 <c>:)</c>, 짝 없는 괄호)는 전부 이스케이프. 짝 없는 괄호를 그냥 두면
    /// A1111에서 뒤따르는 태그 전부가 강조 그룹에 빨려 들어가므로, 글자로 묶어 두는 쪽이 피해가 적다
    /// (그런 문자열은 <see cref="FindSyntaxError"/>가 검증에서 따로 지적한다).</item>
    /// <item>이미 이스케이프된 <c>\(</c>는 건드리지 않는다.</item>
    /// </list>
    /// 괄호가 없는 문자열(대부분)은 원문 그대로다.</summary>
    public static string EscapeLiteralParens(string text)
    {
        if (string.IsNullOrEmpty(text) || (text.IndexOf('(') < 0 && text.IndexOf(')') < 0)) return text ?? "";
        var match = MatchStructuralParens(text, out _);
        var sb = new StringBuilder(text.Length + 8);
        AppendEscaped(text, 0, text.Length, match, sb);
        return sb.ToString();
    }

    private static void AppendEscaped(string s, int from, int to, int[] match, StringBuilder sb)
    {
        for (int i = from; i < to; i++)
        {
            char c = s[i];
            if (c == '\\' && i + 1 < to && (s[i + 1] == '(' || s[i + 1] == ')'))
            {
                sb.Append(c).Append(s[i + 1]);
                i++;
                continue;
            }
            // 낱말 시작에서 열린 구조 괄호 = 강조 문법(가중치 있든 없든) → 바깥 괄호 유지, 안쪽만 재귀.
            // 낱말에 붙어 열린 괄호(piledriver_(sex), 38(t))는 태그명의 일부 → 아래에서 글자로 이스케이프.
            if (c == '(' && match[i] > i && match[i] < to && OpensAtWordStart(s, i))
            {
                int j = match[i];
                int colon = j - 1 > i ? s.LastIndexOf(':', j - 1, j - 1 - i) : -1;
                int bodyEnd = colon > i + 1 && IsWeight(s[(colon + 1)..j]) ? colon : j;
                sb.Append('(');
                AppendEscaped(s, i + 1, bodyEnd, match, sb);
                sb.Append(s, bodyEnd, j - bodyEnd + 1);   // ":가중치)" 또는 ")"
                i = j;
                continue;
            }
            if (c == '(' || c == ')') sb.Append('\\');
            sb.Append(c);
        }
    }

    /// <summary>구조 괄호 짝이 맞는가(이모티콘·이스케이프 괄호는 세지 않음). UI 입력칸이 쉼표 목록을 넣기 전에
    /// 본다 — 짝이 안 맞으면 <see cref="SplitList"/>가 단순 쉼표 분할로 폴백해 문장이 조각나 저장된다.</summary>
    public static bool HasBalancedParens(string text)
    {
        if (string.IsNullOrEmpty(text)) return true;
        MatchStructuralParens(text, out int unmatched);
        return unmatched == 0;
    }

    /// <summary>입력 중인 쉼표 목록에서 "지금 치고 있는 항목"이 시작되기 직전의 쉼표 위치(괄호 밖 쉼표만). 없으면 -1.
    /// 자동완성이 마지막 항목만 검색어로 쓸 때 — 아직 안 닫힌 <c>(she is dragged in, sti</c>를 치는 중이면 괄호 안
    /// 쉼표는 항목 구분이 아니므로 -1(=전체가 한 항목)이다. 짝 없는 닫는 괄호는 무시한다(입력 중 상태).</summary>
    public static int LastTopLevelComma(string text)
    {
        if (string.IsNullOrEmpty(text)) return -1;
        int depth = 0, last = -1;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == ',' && depth == 0) last = i;
            else if (IsStructuralParen(text, i))
            {
                if (c == '(') depth++;
                else if (depth > 0) depth--;
            }
        }
        return last;
    }

    /// <summary>s[i]의 '('가 낱말 시작에서 열렸나(문자열 시작·공백·쉼표·여는 괄호 뒤). 아니면 앞 낱말에 붙은 괄호다.</summary>
    private static bool OpensAtWordStart(string s, int i)
    {
        if (i == 0) return true;
        char prev = s[i - 1];
        return char.IsWhiteSpace(prev) || prev is ',' or '(' or '[' or '{' or '|';
    }

    /// <summary><see cref="EscapeLiteralParens"/>의 역: <c>\(</c>/<c>\)</c>를 괄호로. 출력 줄을 다시 읽어 사전과
    /// 대조하는 곳(<see cref="Output.LineStats"/>)이 쓴다.</summary>
    public static string UnescapeParens(string text) =>
        string.IsNullOrEmpty(text) || text.IndexOf('\\') < 0 ? text ?? "" : text.Replace(@"\(", "(").Replace(@"\)", ")");

    /// <summary><c>(x;1.2)</c>·<c>(x：1.2)</c>처럼 ':' 대신 다른 구분자를 쓴 가중치 꼬리.</summary>
    private static readonly System.Text.RegularExpressions.Regex WrongWeightSeparator = new(
        @"\S\s*[;：]\s*[0-9.]+(\s*~\s*[0-9.]+)?\s*$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>괄호·가중치 문법 오류 설명(첫 번째 하나). 없으면 null. 생성은 이런 문자열도 원문 그대로 흘려보내므로
    /// (프롬프트에 깨진 문법이 섞인 줄 모른다) <see cref="RecipeValidator"/>가 경고로 띄운다.
    /// <list type="bullet">
    /// <item>구조 괄호 짝 불일치. 이모티콘(<c>:(</c> <c>;)</c>)·이스케이프 괄호는 세지 않는다.</item>
    /// <item><c>(본문:꼬리)</c>의 꼬리가 가중치 "시도"(글자 없이 숫자가 든 것: <c>1,2</c>, <c>1.2.3</c>)인데 숫자·범위로
    /// 안 읽힘. 꼬리에 글자가 있으면 문장 속 콜론(<c>(she whispers: please~)</c>)이라 지적하지 않는다.</item>
    /// <item>가중치 구분자 오타: <c>(x;1.2)</c>, 전각 콜론.</item>
    /// <item>본문이 빈 가중치: <c>(:1.2)</c>.</item>
    /// </list>
    /// 괄호가 없는 문자열은 언제나 null(추첨 가중치 접두 <c>N::</c>는 벗기고 본다).</summary>
    public static string? FindSyntaxError(string raw)
    {
        raw ??= "";
        if (raw.Contains(PickWeightSeparator, StringComparison.Ordinal)) raw = ParsePickWeight(raw).Body;
        if (raw.IndexOf('(') < 0 && raw.IndexOf(')') < 0) return null;

        var match = MatchStructuralParens(raw, out int unmatched);
        if (unmatched > 0)
        {
            int open = 0, close = 0;
            for (int i = 0; i < raw.Length; i++)
                if (IsStructuralParen(raw, i)) { if (raw[i] == '(') open++; else close++; }
            return $"괄호 짝이 맞지 않습니다(여는 괄호 {open}개, 닫는 괄호 {close}개).";
        }

        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i] != '(' || match[i] <= i) continue;
            int j = match[i];
            var inner = raw[(i + 1)..j];
            int colon = inner.LastIndexOf(':');
            if (colon >= 0)
            {
                var tail = inner[(colon + 1)..].Trim();
                if (IsWeight(tail))
                {
                    if (inner[..colon].Trim().Length == 0)
                        return $"'({inner})'의 가중치 앞 본문이 비어 있습니다.";
                    continue;
                }
                if (tail.Length > 0 && !tail.Any(char.IsLetter) && tail.Any(char.IsAsciiDigit)
                    && tail.IndexOf('(') < 0 && tail.IndexOf(')') < 0)
                    return $"가중치 '{tail}'를 숫자로 읽을 수 없습니다. (tag:1.2) 또는 (tag:1.1~1.3) 형식으로 쓰세요.";
            }
            if (WrongWeightSeparator.IsMatch(inner) && !IsWeight(inner[(colon + 1)..].Trim()))
                return $"'({Shorten(inner)})'의 가중치 구분자는 ':'여야 합니다. 예: (tag:1.2)";
        }
        return null;
    }

    /// <summary>UI 입력칸에서 항목 하나를 넣기 전에 보는 오류: 가중치 범위 오류(<see cref="WeightRange.FindError"/> —
    /// 그대로 넣으면 생성 때 검증 예외로 터진다)와 괄호·가중치 문법 오류(<see cref="FindSyntaxError"/>). 없으면 null.</summary>
    public static string? FindInputError(string piece) => WeightRange.FindError(piece) ?? FindSyntaxError(piece);

    private static string Shorten(string s) => s.Length <= 40 ? s : s[..37] + "…";

    /// <summary>"1.2" 또는 "1.0~1.2"(공백 허용). 범위 오류(역순 등)는 <see cref="WeightRange.FindError"/>가
    /// 따로 막으므로 여기선 숫자 모양만 본다.</summary>
    private static bool IsWeight(string text)
    {
        var parts = text.Split('~');
        if (parts.Length > 2) return false;
        foreach (var p in parts)
            if (!double.TryParse(p.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                return false;
        return true;
    }
}
