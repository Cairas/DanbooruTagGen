using System.Text;

namespace DanbooruTagGen.Core.Output;

public static class WildcardWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>레시피 이름들을 파일명으로 바꾼다(입력 순서 그대로).
    ///
    /// 레시피별로 파일을 나눠 뽑으면 그 파일명이 곧 와일드카드 토큰이 되므로, 이름을 최대한
    /// 그대로 살린다(🔞·공백·괄호 유지). 손대는 건 파일 시스템이 거부하는 것뿐이다:
    /// 금지문자 제거, 윈도우가 저장하지 못하는 <b>끝의 공백·마침표</b> 정리, 남은 게 없으면
    /// fallback(레시피 id) 사용.
    ///
    /// <para>이름이 겹치면 뒤엣것에 "-2"부터 번호를 붙인다 — 안 그러면 같은 이름의 팩이
    /// 서로를 조용히 덮어써서 파일 수가 모자란 이유를 알 수 없게 된다. 윈도우 파일
    /// 시스템은 대소문자를 구분하지 않으므로 겹침 판정도 대소문자를 무시한다.</para></summary>
    public static IReadOnlyList<string> ToFileNames(IEnumerable<(string Name, string Fallback)> recipes)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var (name, fallback) in recipes)
        {
            var baseName = Sanitize(name);
            if (baseName.Length == 0) baseName = Sanitize(fallback);
            if (baseName.Length == 0) baseName = "recipe";

            var candidate = baseName;
            for (int n = 2; !used.Add(candidate); n++)
                candidate = $"{baseName}-{n}";

            result.Add(candidate);
        }

        return result;
    }

    private static string Sanitize(string raw)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var kept = new StringBuilder(raw.Length);
        foreach (var c in raw)
            if (Array.IndexOf(invalid, c) < 0)
                kept.Append(c);
        // 윈도우는 이름 끝의 공백·마침표를 저장하지 못한다(조용히 잘리거나 열리지 않는다).
        return kept.ToString().TrimEnd(' ', '.').Trim();
    }

    public static void Write(string path, IReadOnlyList<string> lines, WriteMode mode, bool insertBlankLineSeparator)
    {
        string block = string.Join("\n", lines);

        switch (mode)
        {
            case WriteMode.New:
                if (File.Exists(path))
                    throw new IOException($"파일이 이미 존재합니다: {path}");
                File.WriteAllText(path, block, Utf8NoBom);
                break;

            case WriteMode.Overwrite:
                File.WriteAllText(path, block, Utf8NoBom);
                break;

            case WriteMode.Append:
                string existing = File.Exists(path) ? File.ReadAllText(path) : "";
                var sb = new StringBuilder(existing);
                if (existing.Length > 0)
                {
                    if (!existing.EndsWith('\n')) sb.Append('\n');
                    if (insertBlankLineSeparator) sb.Append('\n');
                }
                sb.Append(block);
                File.WriteAllText(path, sb.ToString(), Utf8NoBom);
                break;
        }
    }
}
