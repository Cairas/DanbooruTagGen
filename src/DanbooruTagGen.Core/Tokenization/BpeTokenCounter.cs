using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DanbooruTagGen.Core.Tokenization;

/// <summary>Hugging Face transformers의 느린 토크나이저(CLIPTokenizer · Qwen2Tokenizer)를 토큰 "수"만 세도록 옮긴 것.
/// ComfyUI가 이 둘을 그대로 쓰므로 수가 같아야 한다(기준값 테스트: TokenizerTests). 토큰 수는 BPE 결과 조각 수라 vocab은 필요 없다.
/// <para>CLIP: ftfy가 없을 때의 경로 — BasicTokenizer(제어 문자 제거·공백 정리·한중일 한자 앞뒤 띄움·NFC·소문자) →
/// CLIP 정규식 → 바이트→유니코드 → 끝 글자에 <c>&lt;/w&gt;</c>를 붙인 BPE(merges 앞 48,894개만).</para>
/// <para>Qwen: NFC → Qwen2 정규식 → 바이트→유니코드 → 바이트 수준 BPE(merges 전부).</para>
/// <para>두 경우 모두 글 속의 <c>&lt;|endoftext|&gt;</c>(CLIP은 <c>&lt;|startoftext|&gt;</c>도)는 특수 토큰 하나로 센다.</para></summary>
internal sealed class BpeTokenCounter : ITokenCounter
{
    private readonly Dictionary<(string, string), int> _ranks;
    private readonly Regex _pattern;
    private readonly bool _clip;
    private readonly string[] _special;
    private readonly ConcurrentDictionary<string, int> _wordCache = new(StringComparer.Ordinal);
    /// <summary>캐시를 글자 조각(span)으로 바로 찾는다 — 같은 낱말이 수만 번 나와서, 찾을 때마다 부분 문자열을 만들지 않으려고.</summary>
    private readonly ConcurrentDictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> _wordLookup;

    public ModelKind Model { get; }
    public bool IsExact => true;

    private static readonly string[] ByteToUnicode = BuildByteToUnicode();

    private BpeTokenCounter(ModelKind model, Dictionary<(string, string), int> ranks, Regex pattern, bool clip, string[] special)
    {
        Model = model;
        _ranks = ranks;
        _pattern = pattern;
        _clip = clip;
        _special = special;
        _wordLookup = _wordCache.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    public static BpeTokenCounter Clip(string mergesPath)
    {
        // CLIPTokenizer: merges.txt의 첫 줄(#version) 다음부터 49152 − 256 − 2개만 쓴다.
        var lines = File.ReadAllText(mergesPath, Encoding.UTF8).Trim().Split('\n');
        var ranks = new Dictionary<(string, string), int>();
        int end = Math.Min(lines.Length, 49152 - 256 - 2 + 1);
        for (int i = 1; i < end; i++)
        {
            var parts = lines[i].TrimEnd('\r').Split(' ', StringSplitOptions.RemoveEmptyEntries);   // CRLF로 받은 파일도
            if (parts.Length == 2) ranks.TryAdd((parts[0], parts[1]), i - 1);
        }
        var pattern = new Regex(@"<\|startoftext\|>|<\|endoftext\|>|'s|'t|'re|'ve|'m|'ll|'d|[\p{L}]+|[\p{N}]|[^\s\p{L}\p{N}]+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        return new BpeTokenCounter(ModelKind.IL, ranks, pattern, clip: true, new[] { "<|startoftext|>", "<|endoftext|>" });
    }

    public static BpeTokenCounter Qwen(string mergesPath)
    {
        var ranks = new Dictionary<(string, string), int>();
        int rank = 0, lineNo = 0;
        foreach (var raw in File.ReadLines(mergesPath, Encoding.UTF8))
        {
            var line = raw.Trim();
            if ((lineNo++ == 0 && line.StartsWith("#version:", StringComparison.Ordinal)) || line.Length == 0) continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2) ranks.TryAdd((parts[0], parts[1]), rank);
            rank++;
        }
        var pattern = new Regex(@"(?i:'s|'t|'re|'ve|'m|'ll|'d)|[^\r\n\p{L}\p{N}]?\p{L}+|\p{N}| ?[^\s\p{L}\p{N}]+[\r\n]*|\s*[\r\n]+|\s+(?!\S)|\s+",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);
        return new BpeTokenCounter(ModelKind.Anima, ranks, pattern, clip: false, new[] { "<|endoftext|>" });
    }

    public int Count(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        int total = 0;
        foreach (var (piece, isSpecial) in SplitSpecial(text))
        {
            if (isSpecial) { total++; continue; }
            var normalized = _clip ? ClipNormalize(piece) : piece.Normalize(NormalizationForm.FormC);
            foreach (var m in _pattern.EnumerateMatches(normalized))
            {
                var word = normalized.AsSpan(m.Index, m.Length);
                if (!_wordLookup.TryGetValue(word, out var n))
                {
                    var w = word.ToString();
                    n = CountWord(w);
                    _wordCache.TryAdd(w, n);
                }
                total += n;
            }
        }
        return total;
    }

    /// <summary>특수 토큰 글자를 따로 떼어 낸다(transformers가 추가 토큰을 먼저 쪼개는 것과 같게).</summary>
    private IEnumerable<(string Piece, bool IsSpecial)> SplitSpecial(string text)
    {
        int start = 0;
        while (start < text.Length)
        {
            int best = -1;
            string? which = null;
            foreach (var s in _special)
            {
                int i = text.IndexOf(s, start, StringComparison.Ordinal);
                if (i >= 0 && (best < 0 || i < best)) { best = i; which = s; }
            }
            if (best < 0) { yield return (text[start..], false); yield break; }
            if (best > start) yield return (text[start..best], false);
            yield return (which!, true);
            start = best + which!.Length;
        }
    }

    private int CountWord(string word)
    {
        var bytes = Encoding.UTF8.GetBytes(word);
        var symbols = new List<string>(bytes.Length);
        foreach (var b in bytes) symbols.Add(ByteToUnicode[b]);
        if (_clip) symbols[^1] += "</w>";
        if (symbols.Count < 2) return symbols.Count;

        while (true)
        {
            int bestRank = int.MaxValue, bestIndex = -1;
            for (int i = 0; i < symbols.Count - 1; i++)
                if (_ranks.TryGetValue((symbols[i], symbols[i + 1]), out var r) && r < bestRank) { bestRank = r; bestIndex = i; }
            if (bestIndex < 0) break;

            // 가장 순위가 높은 쌍을 왼쪽부터 겹치지 않게 전부 합친다(transformers의 bpe()와 같은 순서).
            var first = symbols[bestIndex];
            var second = symbols[bestIndex + 1];
            var merged = new List<string>(symbols.Count);
            for (int i = 0; i < symbols.Count;)
            {
                if (i < symbols.Count - 1 && symbols[i] == first && symbols[i + 1] == second)
                {
                    merged.Add(first + second);
                    i += 2;
                }
                else merged.Add(symbols[i++]);
            }
            symbols = merged;
            if (symbols.Count == 1) break;
        }
        return symbols.Count;
    }

    /// <summary>transformers BasicTokenizer(strip_accents=False, do_split_on_punc=False)의 tokenize 후 공백으로 이은 글.</summary>
    private static string ClipNormalize(string text)
    {
        if (IsPlainAscii(text)) return AsciiNormalize(text);
        var sb = new StringBuilder(text.Length + 8);
        foreach (var rune in text.EnumerateRunes())
        {
            int cp = rune.Value;
            if (cp == 0 || cp == 0xFFFD || IsControl(rune)) continue;
            if (IsWhitespace(rune)) { sb.Append(' '); continue; }
            if (IsCjk(cp)) sb.Append(' ').Append(rune.ToString()).Append(' ');
            else sb.Append(rune.ToString());
        }
        var nfc = sb.ToString().Normalize(NormalizationForm.FormC);
        var words = nfc.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++) words[i] = PythonLower(words[i]);
        return string.Join(' ', words);
    }

    /// <summary>출력 줄 대부분은 ASCII다 — 그러면 NFC·한자·제어 문자 처리가 할 일이 없어 소문자 + 공백 정리만 하면 같다.</summary>
    private static bool IsPlainAscii(string s)
    {
        foreach (var c in s)
            if (c >= 0x7F || (c < 0x20 && c is not ('\t' or '\n' or '\r'))) return false;
        return true;
    }

    private static string AsciiNormalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool pendingSpace = false;
        foreach (var c0 in s)
        {
            var c = c0 is '\t' or '\n' or '\r' ? ' ' : c0;
            if (c == ' ') { pendingSpace = sb.Length > 0; continue; }
            if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
            sb.Append(c is >= 'A' and <= 'Z' ? (char)(c + 32) : c);
        }
        return sb.ToString();
    }

    private static bool IsWhitespace(Rune r) =>
        r.Value is ' ' or '\t' or '\n' or '\r' || Rune.GetUnicodeCategory(r) == UnicodeCategory.SpaceSeparator;

    private static bool IsControl(Rune r)
    {
        if (r.Value is '\t' or '\n' or '\r') return false;
        var c = Rune.GetUnicodeCategory(r);
        return c is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate
            or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned;
    }

    private static bool IsCjk(int cp) =>
        cp is >= 0x4E00 and <= 0x9FFF or >= 0x3400 and <= 0x4DBF or >= 0x20000 and <= 0x2A6DF or >= 0x2A700 and <= 0x2B73F
            or >= 0x2B740 and <= 0x2B81F or >= 0x2B820 and <= 0x2CEAF or >= 0xF900 and <= 0xFAFF or >= 0x2F800 and <= 0x2FA1F;

    /// <summary>파이썬 str.lower()에 가깝게(대부분 같음 — 'İ'처럼 글자 수가 바뀌는 것만 다르다).</summary>
    private static string PythonLower(string s) => s.ToLowerInvariant();

    /// <summary>GPT-2 bytes_to_unicode: 출력 가능한 바이트는 그 글자, 나머지는 256부터 차례로.</summary>
    private static string[] BuildByteToUnicode()
    {
        var bs = new List<int>();
        for (int b = '!'; b <= '~'; b++) bs.Add(b);
        for (int b = 0xA1; b <= 0xAC; b++) bs.Add(b);
        for (int b = 0xAE; b <= 0xFF; b++) bs.Add(b);
        var cs = new List<int>(bs);
        int n = 0;
        for (int b = 0; b < 256; b++)
        {
            if (bs.Contains(b)) continue;
            bs.Add(b);
            cs.Add(256 + n++);
        }
        var map = new string[256];
        for (int i = 0; i < bs.Count; i++) map[bs[i]] = char.ConvertFromUtf32(cs[i]);
        return map;
    }
}
