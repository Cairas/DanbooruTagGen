using System.Text;

namespace DanbooruTagGen.Core.Tokenization;

/// <summary>ComfyUI가 프롬프트 한 줄을 텍스트 인코더에 넣기 전에 하는 일을 그대로 옮긴 것(comfy/sd1_clip.py의
/// escape_important · parse_parentheses · token_weights · unescape_important): 강조 괄호 <c>(글:1.2)</c>·<c>((글))</c>를 풀어
/// 가중치가 같은 조각들로 나누고, <c>\(</c> <c>\)</c>는 글자 괄호로 되돌린다. ComfyUI는 이 조각마다 따로 토큰화하므로,
/// 토큰 수도 조각마다 세어 더해야 정확히 같다(<see cref="TokenCounters.CountLine"/>).</summary>
public static class EncoderText
{
    private const string EscClose = "\0\u0001";
    private const string EscOpen = "\0\u0002";
    private static readonly char[] ParenOrEscape = { '(', ')', '\\' };

    /// <summary>인코더에 들어가는 조각들(빈 조각은 뺀다).</summary>
    public static IReadOnlyList<string> Segments(string line)
    {
        // 괄호·역슬래시가 없으면 나눌 것도 되돌릴 것도 없다(가장 흔한 경우).
        if (line.IndexOfAny(ParenOrEscape) < 0) return line.Length > 0 ? new[] { line } : Array.Empty<string>();
        var escaped = line.Replace("\\)", EscClose).Replace("\\(", EscOpen);
        var result = new List<string>();
        foreach (var seg in TokenWeights(escaped))
        {
            var text = seg.Replace(EscClose, ")").Replace(EscOpen, "(");
            if (text.Length > 0) result.Add(text);
        }
        return result;
    }

    /// <summary>조각을 이은 글(사람이 보기용 — 토큰 수는 <see cref="Segments"/> 기준으로 센다).</summary>
    public static string Of(string line) => string.Concat(Segments(line));

    private static IEnumerable<string> TokenWeights(string s)
    {
        foreach (var x0 in ParseParentheses(s))
        {
            var x = x0;
            if (x.Length >= 2 && x[0] == '(' && x[^1] == ')')
            {
                x = x[1..^1];
                int colon = x.LastIndexOf(':');
                // ComfyUI: 콜론 뒤가 숫자로 읽혀야 가중치로 떼어 낸다(파이썬 float()과 같게 앞뒤 공백 허용).
                if (colon > 0 && double.TryParse(x[(colon + 1)..].Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out _))
                    x = x[..colon];
                foreach (var inner in TokenWeights(x)) yield return inner;
            }
            else yield return x;
        }
    }

    private static List<string> ParseParentheses(string s)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        int nesting = 0;
        foreach (var ch in s)
        {
            if (ch == '(')
            {
                if (nesting == 0)
                {
                    if (current.Length > 0) result.Add(current.ToString());
                    current.Clear().Append('(');
                }
                else current.Append(ch);
                nesting++;
            }
            else if (ch == ')')
            {
                nesting--;
                if (nesting == 0)
                {
                    result.Add(current.Append(')').ToString());
                    current.Clear();
                }
                else current.Append(ch);
            }
            else current.Append(ch);
        }
        if (current.Length > 0) result.Add(current.ToString());
        return result;
    }
}
